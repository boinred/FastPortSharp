# fix-base-session-send-fifo-test-flakiness Plan

> **Project**: FastPortSharp
> **Author**: boinred
> **Date**: 2026-05-10
> **Status**: Draft
> **PRD**: [../00-pm/fix-base-session-send-fifo-test-flakiness.prd.md](../../00-pm/fix-base-session-send-fifo-test-flakiness.prd.md)

---

## Executive Summary

| 관점 | 내용 |
|---|---|
| **Problem** | `BaseSession_DoWorkSendBuffers_CompletesMultipleAcceptedItemsInFifoOrder` 외 자매 테스트 1개가 send queue worker와 driver 사이의 race로 GHA macOS에서 비결정적 실패. |
| **Solution** | TestSession에 worker가 첫 batch 빌드 직전 await할 수 있는 **Action delegate hook**을 주입. 두 `TrySendBytes` 모두 enqueue된 뒤 worker 진행 보장. Production 코드 0줄 변경. |
| **Function/UX/Effect** | 영향 받는 두 테스트가 결정적으로 통과. 동일 hook은 향후 timing-민감 테스트에도 재사용 가능. |
| **Core Value** | CI flake 0 → main green 신뢰 회복. PR 머지 마찰 제거. |

---

## Context Anchor

| Key | Value |
|-----|-------|
| **WHY** | 새 `build.yml` (commit `6693573`)이 GHA macos-latest에서 풀 테스트를 처음 돌리며 기존 race를 노출. CI가 빨간 상태로 다음 PR을 막음. |
| **WHO** | Repo committer (boinred), 외부 contributor, AI agent (Claude/Codex). 모두 결정적 CI를 필요로 함. |
| **RISK** | (R-1) hook 추가가 production timing에 영향 / (R-2) 다른 테스트 회귀 / (R-3) 5회 PASS가 충분치 않을 수 있음 |
| **SUCCESS** | macos-latest 5회 연속 PASS + 로컬 `dotnet test` 50회 0 fail + BaseSession.cs 0줄 변경 + 139/139 회귀 0 |
| **SCOPE** | 2 tests (line 222 + line 290 in BaseSessionSendPolicyTests.cs) + TestSession 헬퍼 + (선택) hook 사용 가이드 주석 |

---

## 1. Overview

### 1.1 Problem Recap

`BaseSession.DoWorkSendBuffers`는 `m_SendQueue` (Channel) reader를 돌며 사용 가능한 send item을 그리디하게 batch에 모은다. Test driver가 두 `TrySendBytes`를 연속 호출하더라도, worker가 첫 호출 후 즉시 깨어나서 batch를 만들면 두 번째 호출이 다음 batch로 밀린다.

문제 테스트 두 개는 **첫 batch에 두 buffer가 모두 들어오는 것**을 가정 — race 노출 시 단언 실패.

### 1.2 Why Production Code 변경 0이어야 하는가

- 본 race는 **테스트의 가정**이 부적절한 것이지 production 동작 결함이 아니다. 실제 사용에서는 sender가 두 packet을 한 batch에 묶어 보내려는 보장을 요구하지 않는다.
- Production code에 sync-overhead를 도입하면 throughput 회귀 위험.
- 따라서 fix는 **test fixture 한정**이어야 한다.

---

## 2. Scope

### 2.1 In Scope

- `FastPortTests/BaseSessionSendPolicyTests.cs`의 `TestSession` 클래스에 `Action? onBeforeFirstBatch` (또는 동등 sync delegate) 추가
- 영향 테스트 2건 수정:
  1. `BaseSession_DoWorkSendBuffers_CompletesMultipleAcceptedItemsInFifoOrder` (line 222)
  2. `BaseSession_DoWorkSendBuffers_BatchedSendRespectsChunkLimit` (line 290 — 동일 batching 가정)
- 다른 테스트(`TrySendBytes` 1회 호출만 있는 케이스)는 그대로 유지
- 로컬 stress: `dotnet test` 50회 반복 0 fail 자동화 스크립트 (`tests/scripts/repeat-test.sh` 같은 형태)
- CI: macos-latest job 5회 rerun 후 PASS 확인

### 2.2 Out of Scope

- `LibNetworks/Sessions/BaseSession.cs` 변경
- 다른 flaky 테스트 사냥
- Production code의 동기화 정책 변경
- MSTest → xUnit 등 framework 변경
- macOS runner를 matrix에서 제거

---

## 3. Requirements

### 3.1 Functional

- **FR-1**: TestSession은 worker 시작 후 **첫 번째** SendSocketAsync 호출 직전에 1회만 동기화 hook을 await할 수 있어야 한다.
- **FR-2**: Hook이 `null`이면 production 경로와 동일하게 동작 (overhead 0).
- **FR-3**: Hook은 `CancellationToken`을 받아 disconnect 경로에서도 안전하게 빠져나갈 수 있어야 한다.
- **FR-4**: 테스트 코드는 `var firstBatchGate = new TaskCompletionSource(...);` 형태로 gate를 만들고, 두 `TrySendBytes` 후 `firstBatchGate.SetResult()`로 풀 수 있어야 한다.
- **FR-5**: 수정 후 두 테스트는 GHA macos-latest에서 5회 연속 통과해야 한다.

### 3.2 Non-Functional

- **NFR-1**: BaseSession.cs / 다른 production 파일은 단 1줄도 변경되지 않는다.
- **NFR-2**: 변경 라인 수는 FastPortTests/ 내부 ≤ 100 줄.
- **NFR-3**: 새 NuGet 의존 추가 0.
- **NFR-4**: MSTest 호환 유지.
- **NFR-5**: 로컬 `dotnet test FastPortSharp.sln -c Release` 50회 반복 0 fail.

### 3.3 Compatibility

- net10.0 그대로
- 다른 138개 테스트와 충돌 없음
- TestSession이 `internal sealed`이므로 다른 어셈블리 영향 없음

---

## 4. Success Criteria

### 4.1 Definition of Done

- [ ] TestSession에 Action delegate hook 추가 (null-safe, cancellation-aware)
- [ ] 영향 테스트 2건 수정: 두 `TrySendBytes` 사이에 enqueue 보장 동기화
- [ ] `BaseSession.cs` `git diff` 결과 0줄 변경
- [ ] `dotnet build FastPortSharp.sln -c Release` 0 warning / 0 error
- [ ] `dotnet test FastPortSharp.sln -c Release --no-build` 139 / 0 / 0
- [ ] 로컬 50회 반복 stress: 0 fail
- [ ] GHA build.yml macos-latest job 5회 연속 PASS (`gh run rerun --failed` 또는 신규 push)
- [ ] GHA build.yml ubuntu-latest / windows-latest 회귀 0

### 4.2 Quality Criteria

- [ ] hook의 의미를 한국어 주석으로 설명 (XML doc 또는 inline)
- [ ] 변경 file 수 ≤ 2 (테스트 파일 + 선택적 stress runner)
- [ ] PR 단일 commit으로 마무리 가능

---

## 5. Risks and Mitigation

| Risk | Probability | Impact | Mitigation |
|---|---|---|---|
| (R-1) hook이 production 경로에 노출되어 throughput 영향 | Low | High | hook은 TestSession에만 추가. BaseSession은 `partial class` 또는 internal Action을 통해 default no-op. Action delegate 채택 시 production은 null check 1회만 수행. |
| (R-2) 다른 테스트 회귀 | Low | Medium | 변경 후 전체 139 테스트 통과 확인. TestSession 외부에 영향 없음. |
| (R-3) 5회 PASS가 결정성을 완전히 입증하지 못함 | Medium | Medium | 로컬 50회 반복 stress로 보강. 추가 GHA workflow_dispatch에서 100회 stress 실행도 추가 cycle 후보. |
| (R-4) Action delegate hook이 BaseSession 시그니처를 바꿔 production의 다른 호출자에게 노출 | Low | Medium | virtual method override 옵션도 Design 단계에서 비교. internal field로 한정하면 외부 영향 0. |
| (R-5) 자매 테스트 수정 시 batching size 등 다른 가정도 깨질 수 있음 | Medium | Low | 수정 후 두 테스트 모두 50회 반복으로 확인 (Plan SC §4.1). |

---

## 6. Impact Analysis

### 6.1 영향 받는 파일

| 파일 | 변경 형태 | 예상 라인 |
|---|---|---|
| `FastPortTests/BaseSessionSendPolicyTests.cs` | 수정 (TestSession + 2 테스트) | ~50-80 |
| (신규 후보) `tests/scripts/repeat-tests.sh` | 신규 (50회 반복 헬퍼) | ~30 |
| (선택) `LibNetworks/Sessions/BaseSession.cs` | **변경 0** (검증 axis) | 0 |

### 6.2 영향 받지 않는 영역

- production 모든 .cs 파일
- 다른 138 테스트
- scaffold-game-server.{sh,ps1} 및 tests/scaffold/
- 두 GHA workflow (build.yml, scaffold.yml)

### 6.3 Performance Impact

- production: 0 (null check 1회만, hook=null)
- test: 첫 batch 직전 await 1회 추가 (~ms)

---

## 7. Architecture Considerations

### 7.1 Decision Confirmed (Plan Checkpoint)

| Decision | Choice | Rationale |
|---|---|---|
| Hook style | **Action delegate** (internal Func/Action on BaseSession or TestSession) | virtual method override보다 가벼움. partial class보다 명시적. production overhead 0 (null check). |
| Scope | **2 테스트 동시 수정** | 동일 race 가정을 가진 자매 테스트 (`BatchedSendRespectsChunkLimit`)도 선제적 stabilise. flake 재발 방지. |
| Stress 강도 | **로컬 50 + CI 5 rerun** | PRD 기본값. flake 재발 시 100회로 escalate. |

### 7.2 Open Decisions for Design Phase

- Action delegate를 `BaseSession.cs`에 두느냐 (internal hook) vs `TestSession.cs`에 두느냐 (subclass 전용 field)
- TestSession의 hook이 `WaitForSendTelemetryRegistrationAsync`를 override하는지, `BuildSendSegmentsAsync` 직전 새 hook을 추가하는지
- 50회 반복 stress runner를 신규 스크립트로 만드는지, `dotnet test --filter` + bash for-loop로 inline 처리하는지

---

## 8. Convention Prerequisites

- `Set-StrictMode` / `set -euo pipefail` 등 기존 컨벤션 그대로
- 한국어 주석 컨벤션 (CLAUDE.md global) 적용
- TestSession 명명 (`m_FirstBatchGate` 등 — 기존 `m_` prefix와 일치)
- 한 commit으로 요약 가능한 단일 PR 형태 권장

---

## 9. Next Steps

1. `/pdca design fix-base-session-send-fifo-test-flakiness`
   - 3 architecture options 비교:
     - **A**: BaseSession에 internal Action hook (1줄)
     - **B**: TestSession에서 WaitForSendTelemetryRegistrationAsync 인라인 override (BaseSession에 protected virtual 1개 추가)
     - **C**: Test-level TaskCompletionSource gate를 sendBatchOverride 안에서만 사용 (BaseSession 0 변경)
   - SCOPE의 "production 0 변경" 게이트가 있어 **C가 가장 보수적**. A/B와 trade-off 검토.
2. `/pdca do ...`
3. `/pdca analyze ...`
4. `/pdca report ...` + `/pdca archive ...`

---

## Version History

| Version | Date | Changes | Author |
|---|---|---|---|
| 0.1 | 2026-05-10 | Initial plan (Action delegate hook, 2 tests, 50 local + 5 CI rerun) | boinred |
