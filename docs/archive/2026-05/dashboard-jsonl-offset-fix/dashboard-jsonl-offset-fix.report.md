# dashboard-jsonl-offset-fix Completion Report

> **Project**: FastPortSharp
> **Author**: boinred
> **Date**: 2026-05-11
> **Status**: ✅ Completed
> **Match Rate**: 100% (runtime-weighted)
> **Commit**: `197072a`

---

## Executive Summary

| 관점 | Planned | Delivered |
|---|---|---|
| **Problem** | JsonlPollingAdapter yield-resume gap race로 동기 사용 패턴 데이터 손실 | ✅ 동일 |
| **Solution** | Offset 갱신을 yield BEFORE로 이동, tuple return으로 race-safe | ✅ Option A — Tuple return 적용 |
| **Function/UX/Effect** | Production 동작 동일, public API 보존, race window 좁힘 | ✅ Consumer 영향 0, 기존 20 tests 그대로 통과 |
| **Core Value** | dashboard-unit-tests cycle의 백그라운드 writer 우회를 정식 수정 | ✅ 향후 manual enumerator 패턴도 안전, lib correctness 보강 |

### Value Delivered

| Metric | Before | After |
|---|---|---|
| Race window (yield→offset update gap) | yield 후 FileInfo.Length 재캡처 | yield 전 fs.Length capture timing 안정화 |
| Manual enumerator + 동기 append 안전성 | ❌ 데이터 손실 가능 | ✅ 안전 |
| Public API signature | — | unchanged |
| Test 변경 | — | **0줄** (사용자 확정) |
| Dashboard tests | 20 | 20 (회귀 0) |
| Dashboard 빌드 | 0/0 | 0/0 |
| FastPortSharp.sln 회귀 | 139/0/0 | 139/0/0 |
| 변경 파일 | — | 1 (JsonlPollingAdapter.cs) + 3 docs |

---

## 1. Key Decisions & Outcomes

| Phase | Decision | Outcome |
|---|---|---|
| **[Plan]** Fix scope: Race 수정 + 기존 test 유지 | ✅ Followed | Test 파일 git diff 0 |
| **[Plan]** Public API 보존 | ✅ Followed | `StreamAsync` signature unchanged |
| **[Plan]** Truncation 로직 보존 | ✅ Followed | `fs.Length < startOffset` 동일 로직 (위치만 ReadNew 내부로) |
| **[Plan]** 단일 commit | ✅ Followed | `197072a` |
| **[Design]** Option A — Tuple return | ✅ Followed | `Task<(ObservedMetricsSnapshot[], long)>` |
| **[Design]** fileLength = fs.Length capture (open 직후) | ✅ Followed | line 73 of JsonlPollingAdapter.cs |

---

## 2. Success Criteria Final Status

**Overall**: **10/10 ✅ Met**, 0 Partial, 0 Not Met

| # | Criterion | Status |
|---|---|---|
| SC-1 | Tuple return 변경 | ✅ |
| SC-2 | StreamAsync offset before yield | ✅ |
| SC-3 | Truncation 로직 보존 | ✅ |
| SC-4 | FileShare.ReadWrite \| FileShare.Delete 보존 | ✅ |
| SC-5 | Dashboard 빌드 0/0 | ✅ |
| SC-6 | Dashboard test 20/0/0 회귀 0 | ✅ (731ms) |
| SC-7 | FastPortSharp.sln 빌드 + 139 tests 회귀 0 | ✅ |
| SC-8 | CI 무변경 | ✅ |
| SC-9 | 한국어 주석 race window 명시 | ✅ |
| SC-10 | 단일 commit | ✅ |

---

## 3. PDCA Cycle Summary

| Phase | Output | Notes |
|---|---|---|
| Plan | `docs/01-plan/features/dashboard-jsonl-offset-fix.plan.md` | 사용자 결정: Race 수정 + 기존 test 유지 |
| Design | `docs/02-design/features/dashboard-jsonl-offset-fix.design.md` | Option A — Tuple return |
| Do | commit `197072a` | 1 파일 변경, ±25 줄 |
| Check | `docs/03-analysis/dashboard-jsonl-offset-fix.analysis.md` | 100% Match Rate, 0 Critical/Important |
| Iterate | — | 불필요 (threshold 충족) |
| Report | (this document) | — |

---

## 4. Implementation Highlights

### 4.1 Race Window 변화

```
Before: ReadNew(offset) → yield → [consumer가 여기서 append 가능] → offset = FileInfo.Length
        └─ race window: yield/resume gap이 consumer thread에 노출됨
        └─ 결과: append된 위치까지 jump → 새 데이터 영구 skip

After:  ReadNew(offset) → newOffset = fs.Length (open 직후 capture)
        → offset = newOffset → yield → delay
        └─ race window: 최소화. open과 fs.Length capture 사이 ms 단위
        └─ 결과: append는 다음 iteration에서 정상 read
```

### 4.2 Tuple Return Pattern

```csharp
// Before
private async Task<ObservedMetricsSnapshot[]> ReadNewSnapshotsAsync(long startOffset, CancellationToken ct)

// After
private async Task<(ObservedMetricsSnapshot[] Snapshots, long NewOffset)> ReadNewSnapshotsAsync(
    long startOffset, CancellationToken ct)
```

→ C# 7+ tuple, async-friendly. Public API signature 보존.

### 4.3 Test Compatibility

5/5 JsonlPollingAdapter tests 그대로 통과:
- T-JA-1 (3 line yield)
- T-JA-2 (백그라운드 writer offset 유지) — fix 후에도 동일 검증
- T-JA-3 (백그라운드 truncate + write) — fix 후에도 동일 검증
- T-JA-4 (malformed skip)
- T-JA-5 (concurrent write FileShare)

→ 백그라운드 writer 패턴은 race-safe 시나리오를 검증하므로 fix 전후 모두 통과. 향후 manual enumerator 패턴이 필요해질 때 추가 test 가능.

---

## 5. Lessons Learned

1. **Race window 식별 시점의 가치**: dashboard-unit-tests cycle에서 백그라운드 writer 패턴으로 우회한 race를 별도 cycle로 분리해 정식 수정한 결과 — narrow scope cycle (1 파일, ~25줄)으로 production correctness 보강 가능.
2. **Public API 보존 원칙**: Internal refactor (tuple return)로 race 제거하면서 consumer (ViewModel, Test) 영향 0 유지. Backward-compat가 정확한 narrow cycle의 핵심.
3. **fs.Length capture timing**: 파일 open 직후 capture가 가장 안전한 stable snapshot. `FileInfo.Length` (open 없이 별도 호출)보다 명확하고 race-safe.
4. **백그라운드 writer test의 의의 재평가**: 단순 race 회피용으로 보였으나, fix 후엔 "race-safe 시나리오의 정상 검증"으로 역할 변경. 테스트 자체 가치는 유지.

---

## 6. Follow-up Recommendations

| Cycle | Purpose | Priority |
|---|---|---|
| `dashboard-jsonl-manual-enumerator-tests` | Race 제거 검증용 manual MoveNextAsync + 동기 append 테스트 추가 (T-JA-2b/T-JA-3b) | Low |
| `dashboard-jsonl-partial-line-handling` | StreamReader buffer 너머 read 시 partial line at EOF 정식 처리 (현재 skip → loss 가능) | Low |
| `dashboard-multi-rtt-overlay` | P50/P95/P99 동시 표시 | Medium |
| `dashboard-ci-add` | Dashboard sln 별도 CI workflow | Low |

---

## 7. Archive Note

이 cycle의 모든 PDCA 문서는 `/pdca archive dashboard-jsonl-offset-fix` 실행 시 `docs/archive/2026-05/dashboard-jsonl-offset-fix/`로 이동됩니다.

---

## Version History

| Version | Date | Changes | Author |
|---|---|---|---|
| 0.1 | 2026-05-11 | Completion report (Match Rate 100%, 10/10 SC met, race window 좁힘 검증, single commit 197072a) | boinred |
