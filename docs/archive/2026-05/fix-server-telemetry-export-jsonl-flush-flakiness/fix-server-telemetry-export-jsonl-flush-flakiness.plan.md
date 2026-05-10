# fix-server-telemetry-export-jsonl-flush-flakiness Plan

> **Project**: FastPortSharp
> **Author**: boinred
> **Date**: 2026-05-10
> **Status**: Draft
> **PRD**: [../../00-pm/fix-server-telemetry-export-jsonl-flush-flakiness.prd.md](../../00-pm/fix-server-telemetry-export-jsonl-flush-flakiness.prd.md)

---

## Executive Summary

| 관점 | 내용 |
|---|---|
| **Problem** | `ServerTelemetryExportBackgroundService_WritesServerObservedJsonl` 테스트가 GHA Windows에서 ~10-20% flake. `Task.Delay(1200)` 고정 sleep이 production 1초 interval 대비 thread pool latency 흡수 마진 부족. |
| **Solution** | 고정 sleep을 **poll-until-ready** 패턴으로 교체. `WaitForFileWithLinesAsync(path, minLines: 1, timeout: 10s, interval: 50ms)`. **Production 0줄 변경**. |
| **Function/UX/Effect** | healthy 환경에서는 ~1초 즈음 즉시 종료, slow runner에서도 deterministic하게 통과. 진단 메시지 포함 timeout 처리. |
| **Core Value** | windows job 5/5 PASS 회복 → main green 신뢰 회복. 직전 cycle과 같은 패턴이 두 번째 timing-test에 재사용되어 표준화. |

---

## Context Anchor

| Key | Value |
|-----|-------|
| **WHY** | 직전 cycle 진행 중 build.yml 1차 실행에서 windows-latest가 본 테스트로 ~20% flake 발생. main green 회복으로 PR 머지 신호 클리어. |
| **WHO** | Repo committer + 외부 contributor + AI agent. |
| **RISK** | (R-1) timeout 부족 / (R-2) helper 외 다른 회귀 / (R-3) flake 재발 (직전 cycle 패턴이라 낮음) |
| **SUCCESS** | windows 5회 PASS + macos/ubuntu 회귀 0 + production 0줄 변경 + 변경 ≤ 50줄 / 1 파일 |
| **SCOPE** | `FastPortTests/ServerTelemetryTests.cs` line 248-285 한 테스트 한정. helper는 같은 파일 내 private static. |

---

## 1. Overview

### 1.1 Problem Recap

`ServerTelemetryExportBackgroundService.ExecuteAsync` (production):
1. `Directory.CreateDirectory` (line 36)
2. `File.Open(FileMode.Create)` (line 44)
3. loop: `await Task.Delay(interval)` (line 52, **interval ≥ 1s**) → write line → flush

Test 현재:
- `await service.StartAsync(ct)` — returns immediately, ExecuteAsync는 thread pool로 schedule
- `await Task.Delay(1200)` — 1.2초 고정 대기
- `await service.StopAsync(ct)` — token cancel
- `Assert.IsTrue(File.Exists(path))` — fail 발생 위치

### 1.2 Race Window

```
healthy:
  T+0    Start
  T+~50  ExecuteAsync runs File.Open
  T+1050 Task.Delay completes → write → file 1 line
  T+1200 test Stop → graceful exit ✅

slow windows runner:
  T+0    Start
  T+~300 ExecuteAsync runs File.Open (thread pool latency)
  T+1300 Task.Delay would complete (intent)
  T+1200 test Stop → token cancel BEFORE write
  result: file may have 0 lines — File.Exists assertion 통과/실패 OS 분산
```

### 1.3 Why Production 0 변경이어야 하는가

- production의 `Task.Delay(interval)` semantics는 정상. 1초 interval로 주기 export는 의도된 정책.
- 테스트의 가정("1.2초 안에 적어도 1줄 쓰일 것")이 너무 빠듯한 게 문제.
- production 안에 "instant first write" hook을 도입하면 oneshot vs periodic 의미가 흐려짐.
- 따라서 fix는 **테스트의 단언 시점을 polling으로 변경**하면 충분.

---

## 2. Scope

### 2.1 In Scope

- `FastPortTests/ServerTelemetryTests.cs:248-285` 단일 테스트 수정
- Polling helper 추가 (private static, 같은 파일 내) — 직전 cycle BatchedFifoObserver와 동일 isolation 정책
- `await Task.Delay(1200)` 제거, polling 기반 wait + 단언 순서 재구성
- (선택) 동일 helper로 다른 timing test에도 적용 가능 — 단, 본 cycle은 1개만 수정

### 2.2 Out of Scope

- `FastPortTestSmokeServer/ServerTelemetryExportBackgroundService.cs` 변경
- `LibNetworks/`, `LibCommons/`, `FastPortServer/` 등 모든 엔진/server 코드
- `IntervalSeconds` clamping 정책 변경
- helper를 별도 `Helpers/` 디렉토리로 분리 (YAGNI: 사용처 1곳)
- 다른 timing-dependent test audit
- Windows GHA runner 환경 변경

---

## 3. Requirements

### 3.1 Functional

- **FR-1**: helper는 `(filePath, minLines, timeout, pollInterval)` 시그니처로 동작.
- **FR-2**: 조건 만족 시 즉시 return — 추가 sleep 없음.
- **FR-3**: timeout 도달 시 `Assert.Fail` (또는 `AssertionFailedException`) — 마지막 관찰 상태(file 존재 여부, line count) 메시지 포함.
- **FR-4**: 테스트의 모든 기존 단언 (`SentBytes`, `PendingSendRequests`, `SendBufferBytes`, `TotalAcceptedSessions`, `TotalSendRequests`) 그대로 유지.
- **FR-5**: 수정 후 windows-latest에서 5회 연속 PASS.

### 3.2 Non-Functional

- **NFR-1**: production code 0줄 변경 (`git diff` 검증).
- **NFR-2**: 변경 라인 ≤ 50줄 (test 파일 단일).
- **NFR-3**: 새 NuGet 의존 0.
- **NFR-4**: MSTest 호환 (기존 `[TestMethod]`, `Assert.*` API).
- **NFR-5**: healthy 환경 wall-clock ≤ 2초 (현재 1.2초보다 미세 증가 허용).

### 3.3 Compatibility

- net10.0
- 다른 138 테스트와 충돌 0
- helper의 `private static` scope이라 외부 노출 없음

---

## 4. Success Criteria

### 4.1 Definition of Done

- [ ] `ServerTelemetryTests.cs`에 polling helper 추가 (`WaitForFileWithLinesAsync` 또는 동등한 이름)
- [ ] 대상 테스트의 `await Task.Delay(1200)` → `await WaitForFileWithLinesAsync(path, 1, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(50))`
- [ ] helper에 timeout 도달 시 진단 메시지 포함 fail 처리
- [ ] `FastPortTestSmokeServer/ServerTelemetryExportBackgroundService.cs` `git diff` = 0줄
- [ ] `dotnet build FastPortSharp.sln -c Release` 0/0
- [ ] `dotnet test --no-build` 139/0/0
- [ ] 로컬 `tests/scripts/repeat-tests.sh 50 "FullyQualifiedName~ServerTelemetryExport"` 0 fail
- [ ] GHA build.yml windows-latest 5회 연속 PASS
- [ ] ubuntu-latest / macos-latest 5/5 회귀 0

### 4.2 Quality Criteria

- [ ] helper의 의미를 한국어 주석으로 1-2줄 (직전 cycle 패턴)
- [ ] 변경 file 1개 (test 파일만)
- [ ] PR/commit 단일 (직전 cycle처럼 single commit)

---

## 5. Risks and Mitigation

| Risk | Probability | Impact | Mitigation |
|---|---|---|---|
| (R-1) timeout 10초 부족 | Low | Medium | PRD §6 R-1: 1.2초 고정의 8× margin. flake 재발 시 30초로 escalate (Plan SC §4 update). |
| (R-2) 다른 138 테스트 회귀 | Low | Medium | 변경이 1개 함수 본문 + 1개 private static helper만이라 noise 0. 빌드 + 전체 test pass 검증. |
| (R-3) helper 재사용 로직 누락 | Low | Low | 본 cycle은 1곳만 사용 — YAGNI. 다음 비슷한 flake 발생 시 helper를 `Helpers/`로 추출하는 별도 micro-cycle. |
| (R-4) windows runner 환경 자체 분산 | Low | Low | 5회 rerun으로 결정성 입증. 50회 로컬 stress로 보강. |

---

## 6. Impact Analysis

### 6.1 영향 받는 파일

| 파일 | 변경 형태 | 예상 라인 |
|---|---|---|
| `FastPortTests/ServerTelemetryTests.cs` | modify (test 1 + helper 1) | ~30-50 |
| (검증 axis) `FastPortTestSmokeServer/ServerTelemetryExportBackgroundService.cs` | **변경 0** | 0 |
| (검증 axis) `LibNetworks/`, `LibCommons/`, `FastPortServer/` | **변경 0** | 0 |

### 6.2 영향 받지 않는 영역

- production 코드 전체
- 다른 138 테스트
- scaffold-game-server.{sh,ps1} / tests/scaffold/
- build.yml / scaffold.yml

### 6.3 Performance Impact

- production: 0
- test: healthy 환경 ~1.0-1.2초 (현재와 거의 동일, 오히려 미세히 빨라질 수 있음)

---

## 7. Architecture Considerations

### 7.1 Decision Confirmed (Plan Checkpoint)

| Decision | Choice | Rationale |
|---|---|---|
| Helper 위치 | **테스트 파일 내부 private static** | YAGNI: 사용처 1곳. 직전 cycle BatchedFifoObserver 동일 패턴. 추후 사용처 ≥ 3 시점에 `Helpers/`로 추출. |
| Timeout | **10초** | 현재 1.2초 대비 8× 마진. healthy 환경에는 영향 없음 (조건 즉시 감지). |
| Polling interval | **50ms** | 응답성 vs CPU 부하 균형. 1.2초 안에 ~24회 polling. |
| Stress 강도 | **로컬 50회 + GHA 5회 rerun** | 직전 cycle과 동일 게이트. |

### 7.2 Open Decisions for Design Phase

- Helper 시그니처 정확한 형태 (Task<bool> vs throws on timeout vs Result wrapper)
- timeout 도달 시 메시지 형식 (last observed state 포함 여부)
- StopAsync 호출 시점 (polling 성공 후 즉시 vs 추가 buffer 시간)

---

## 8. Convention Prerequisites

- 한국어 주석 컨벤션 (CLAUDE.md global) 적용
- `m_` prefix는 production 컨벤션이고 test private은 PascalCase / camelCase 그대로
- 변경 단일 commit으로 마무리

---

## 9. Next Steps

1. `/pdca design fix-server-telemetry-export-jsonl-flush-flakiness`
   - 3 architecture options:
     - **A**: 테스트 안에서 inline polling loop (helper 없음)
     - **B**: 테스트 파일 내부 private static helper (Recommended — 직전 cycle 일관성)
     - **C**: `Helpers/AsyncWait.cs` 신규 분리
2. `/pdca do ...` (단일 세션, ≤ 20 turn 추정)
3. `/pdca analyze` + `report` + `archive`

---

## Version History

| Version | Date | Changes | Author |
|---|---|---|---|
| 0.1 | 2026-05-10 | Initial plan (polling helper, timeout 10s, test-only fix, prod 0 변경 게이트) | boinred |
