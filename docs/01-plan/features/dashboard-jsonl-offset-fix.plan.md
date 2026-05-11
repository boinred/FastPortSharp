# dashboard-jsonl-offset-fix Plan

> **Project**: FastPortSharp
> **Author**: boinred
> **Date**: 2026-05-11
> **Status**: Draft
> **PRD**: (lightweight, Plan에 통합)

---

## Executive Summary

| 관점 | 내용 |
|---|---|
| **Problem** | `JsonlPollingAdapter.StreamAsync`가 yield 후 offset을 갱신하는 race가 있어 producer가 yield-resume gap에 append하면 새 데이터를 영구적으로 건너뜀. dashboard-unit-tests cycle 작성 중 발견. |
| **Solution** | offset 갱신을 yield BEFORE로 이동. `ReadNewSnapshotsAsync`가 (snapshots, newOffset) tuple 반환, StreamAsync는 yield 전에 offset 확정. |
| **Function/UX/Effect** | Mock/JSONL 동작 동일 (현재 production 사용 패턴은 race 노출 안 됨), test 회귀 0, race window 최소화 → 향후 동기 사용 패턴도 안전. |
| **Core Value** | Production code의 correctness 보강. 기존 백그라운드 writer 테스트 패턴이 race 회피용이었음을 명시적으로 fix. |

---

## Context Anchor

| Key | Value |
|-----|-------|
| **WHY** | dashboard-unit-tests cycle에서 발견된 offset race를 정식 수정. 현재 production에서 무해하지만 manual enumerator + 동기 append 패턴에 노출. |
| **WHO** | boinred + 미래 contributor + AI agent. |
| **RISK** | (R-1) Tuple 반환 시 yield 직전 offset 캡처 잘못 / (R-2) 기존 20 tests 회귀 / (R-3) Production 동작 변경으로 dashboard 시각화 회귀 |
| **SUCCESS** | offset race 제거 + Dashboard 빌드 0/0 + Dashboard test 20/0/0 + FastPortSharp.sln 회귀 0 + 단일 commit |
| **SCOPE** | `FastPortDashboard.Core/Adapters/JsonlPollingAdapter.cs` only. Test 변경 0. Maui app / FastPortSharp.sln 변경 0. |

---

## 1. Overview

### 1.1 Race Description

기존 `JsonlPollingAdapter.StreamAsync` 흐름:

```csharp
while (!ct.IsCancellationRequested)
{
    // 1. Read from offset
    var snaps = await ReadNewSnapshotsAsync(lastReadOffset, ct);
    // 2. Yield each snapshot to consumer  ← consumer가 여기서 file append 가능
    foreach (var snap in snaps) yield return snap;
    // 3. Update offset to FileInfo.Length  ← 이미 append된 위치까지 점프
    try { lastReadOffset = new FileInfo(_path).Length; } catch { }
    // 4. Delay
    await Task.Delay(_interval, ct);
}
```

**Race**: Consumer가 step 2 (yield)와 step 3 (offset capture) 사이에 file을 append하면, step 3에서 `FileInfo.Length`가 이미 append된 위치를 가리키므로 다음 iteration에서 append된 데이터를 영구적으로 skip.

### 1.2 Production Impact (현재 무해 사유)

실제 사용 패턴:
- Producer (server)가 백그라운드 thread에서 JSONL append
- Consumer (Dashboard ViewModel)는 yield 후 즉시 다음 MoveNext 호출 (UI thread)
- Producer와 consumer는 분리된 thread → yield-resume gap 동안 producer는 generator의 task delay loop에서 write
- Result: race window이 일치하지 않아 실 사용에서는 데이터 손실 거의 없음

다만 다음 시나리오에서 노출:
- Manual enumerator (`GetAsyncEnumerator()` + `MoveNextAsync`)로 동기 append
- 단위 테스트의 contrived 시나리오 (dashboard-unit-tests cycle T-JA-2/T-JA-3 원래 패턴)

### 1.3 Fix Strategy

1. `ReadNewSnapshotsAsync`가 `(snapshots, newOffset)` tuple 반환
2. `newOffset`은 파일 stream을 연 직후의 `fs.Length` (안정적 snapshot)
3. `StreamAsync`는 read 직후 offset 확정 → yield → delay
4. Yield와 offset update 사이의 race window 제거

### 1.4 Out of scope

- Long-lived FileStream (성능 최적화)
- StreamReader 버퍼링 문제 (partial line at EOF 처리)
- MockPollingAdapter 변경
- 신규 test 추가 (사용자 확정: 기존 20 tests 유지)
- Truncation 로직 변경 (현재 동작 보존)

---

## 2. Scope

### 2.1 In Scope

| 영역 | 작업 |
|---|---|
| `FastPortDashboard.Core/Adapters/JsonlPollingAdapter.cs` | `StreamAsync` + `ReadNewSnapshotsAsync` rewrite (offset capture timing 변경) |

### 2.2 Out of Scope

- `MockPollingAdapter`, `IPollingAdapter` (시그너처 보존)
- ViewModel / MainPage / Tests
- `FastPortSharp.sln` (변경 0)
- CI workflow (변경 0)

### 2.3 Key Constraint

- **Public API 변경 0**: `StreamAsync(CancellationToken ct)` signature 보존, return type `IAsyncEnumerable<ObservedMetricsSnapshot>` 동일.
- **기존 20 tests 회귀 0**: 사용자 확정.
- **Truncation 동작 보존**: `if (currentLength < lastReadOffset) lastReadOffset = 0` 로직 유지.

---

## 3. Requirements

### 3.1 Functional

- **FR-1**: `ReadNewSnapshotsAsync(long startOffset, CancellationToken)` → `Task<(ObservedMetricsSnapshot[] Snapshots, long NewOffset)>` 반환
- **FR-2**: `NewOffset`은 파일 open 직후 `fs.Length` 값 (race-safe snapshot)
- **FR-3**: Truncation detection: `fs.Length < startOffset`이면 startOffset=0으로 리셋하여 처음부터 다시 read
- **FR-4**: `StreamAsync` 흐름: `(snaps, newOffset) = await ReadNew()` → `lastReadOffset = newOffset` → `foreach yield` → `delay`
- **FR-5**: 기존 외부 동작 보존 — Mock/JSONL polling 동일하게 동작, Dashboard UI 영향 0

### 3.2 Non-Functional

- **NFR-1**: `dotnet build FastPortSharp.Dashboard.sln -c Release` 0/0
- **NFR-2**: `dotnet test FastPortSharp.Dashboard.sln -c Release --no-build` 20/0/0 회귀 0
- **NFR-3**: `dotnet build FastPortSharp.sln -c Release` 회귀 0/0
- **NFR-4**: `dotnet test FastPortSharp.sln -c Release --no-build` 139/0/0
- **NFR-5**: CI workflow 변경 0
- **NFR-6**: 단일 commit
- **NFR-7**: 한국어 주석으로 race window 좁힘 명시

### 3.3 Compatibility

- `IPollingAdapter.StreamAsync` signature 그대로
- `IAsyncEnumerable<ObservedMetricsSnapshot>` 반환
- FileShare.ReadWrite | FileShare.Delete (memory: fileshare-windows-gotcha) 보존

---

## 4. Success Criteria

### 4.1 Definition of Done

- [ ] `ReadNewSnapshotsAsync` tuple 반환으로 변경
- [ ] `StreamAsync` 흐름 update (offset before yield)
- [ ] Truncation 로직 보존 (`fs.Length < startOffset` 검사)
- [ ] FileShare.ReadWrite | FileShare.Delete 보존
- [ ] Dashboard 빌드 0/0
- [ ] Dashboard test 20/0/0 회귀 0
- [ ] FastPortSharp.sln 빌드 + 139 tests 회귀 0
- [ ] CI 무변경
- [ ] 한국어 주석 race window 좁힘 명시
- [ ] 단일 commit

### 4.2 Quality Criteria

- [ ] Public API signature 변경 0
- [ ] 변경 파일 ≤ 2 (Adapter + docs)
- [ ] 한국어 주석 컨벤션 유지

---

## 5. Risks and Mitigation

| Risk | Probability | Impact | Mitigation |
|---|---|---|---|
| (R-1) `fs.Length` capture timing 부정확 (StreamReader buffer past length) | Low | Low | `fs.Length`를 open 직후 즉시 capture. StreamReader 버퍼링이 length 너머로 못 감 (file 끝). |
| (R-2) 기존 20 tests 회귀 | Low | High | 백그라운드 writer 패턴 test는 fix와 무관하게 통과. 빌드 + test로 즉시 검증. |
| (R-3) Production polling 동작 미세 변경 | Low | Medium | Behavior 자체는 동일, race window만 좁힘. 수동 실행 검증으로 회귀 확인. |
| (R-4) Tuple deconstruction C# 호환 | Low | Low | `net10.0` 표준 지원, 추가 작업 0. |

---

## 6. Impact Analysis

### 6.1 영향 받는 파일

| 파일 | 작업 | 예상 라인 |
|---|---|---|
| `FastPortDashboard.Core/Adapters/JsonlPollingAdapter.cs` | edit (StreamAsync + ReadNewSnapshotsAsync) | ±20 |

총 1 파일 + 2 docs.

### 6.2 영향 받지 않는 영역

- `MockPollingAdapter`, `IPollingAdapter` interface
- ViewModel / MainPage / XAML
- 20 tests (백그라운드 writer 패턴이 fix 후에도 그대로 통과)
- `FastPortSharp.sln` 전체
- CI workflow

### 6.3 CI Impact

CI workflow 변경 0.

---

## 7. Architecture Considerations

### 7.1 Decision Confirmed (Plan Checkpoint)

| Decision | Choice | Rationale |
|---|---|---|
| Fix scope | **Race 수정 + 기존 test 그대로 유지** | 사용자 확정. 최소 변경. |
| Public API | 보존 (`IAsyncEnumerable` interface 동일) | Consumer 영향 0 |
| Truncation 로직 | 보존 (`fs.Length < startOffset` 리셋) | 검증된 동작 |
| Single commit | Yes | 일관성 |

### 7.2 Open Decisions for Design Phase

- **Tuple return vs ref/out parameter**: tuple 권장 (C# 7+ 표준 패턴)
- **Truncation detection 위치**: 현재는 yield 후. fix 후엔 ReadNew 안에서 처리할지 StreamAsync에서 별도 처리할지.

---

## 8. Convention Prerequisites

- 한국어 주석
- 단일 commit
- Race window 좁힘 명시 (memory: fileshare-windows-gotcha 같은 lesson 가능)

---

## 9. Next Steps

1. `/pdca design dashboard-jsonl-offset-fix`
   - 3 option:
     - **A**: Tuple 반환 (ReadNewSnapshotsAsync가 newOffset도 반환) — Recommended
     - **B**: `ref long lastReadOffset` 파라미터 (C# 7 ref locals, 가독성 ↓)
     - **C**: 별도 `GetCurrentOffset()` method (race window 더 커짐, anti-pattern)
2. `/pdca do dashboard-jsonl-offset-fix` (단일 세션, ≤ 8 turn 추정)
3. `analyze` → `report` → `archive`

---

## Version History

| Version | Date | Changes | Author |
|---|---|---|---|
| 0.1 | 2026-05-11 | Initial plan (Race 수정 + 기존 test 유지, 1 파일 변경, tuple return) | boinred |
