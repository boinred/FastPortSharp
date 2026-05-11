# dashboard-jsonl-offset-fix Analysis

> **Project**: FastPortSharp
> **Author**: boinred
> **Date**: 2026-05-11
> **Status**: Check Complete
> **Plan**: `docs/01-plan/features/dashboard-jsonl-offset-fix.plan.md`
> **Design**: `docs/02-design/features/dashboard-jsonl-offset-fix.design.md`
> **Commit**: `197072a`

---

## Context Anchor

| Key | Value |
|-----|-------|
| **WHY** | Race window 좁혀 production correctness 보강 + 동기 사용 패턴 안전화. |
| **WHO** | boinred + 미래 contributor. |
| **RISK** | (R-1) Tuple return / (R-2) 기존 20 tests 회귀 / (R-3) Production 미세 변경 |
| **SUCCESS** | Race 제거 + Dashboard 0/0 + 20 tests 회귀 0 + 회귀 sln 0 + 단일 commit |
| **SCOPE** | `JsonlPollingAdapter.cs` only |

---

## 1. Match Rate Summary

| Axis | Score | Notes |
|---|---|---|
| **Structural** | 100% | 1 파일 변경 (Plan §6.1 정확히 매칭) |
| **Functional** | 100% | Tuple return 적용, offset capture timing 이동, truncation 로직 이동 |
| **Contract (Build/Test)** | 100% | Dashboard 0/0, 20/0/0 회귀 0, 회귀 sln 0/0, 139/0/0 |
| **Runtime** | 100% | 20 tests 실행, 731ms |
| **Overall (runtime-weighted)** | **100%** | (Structural × 0.15) + (Functional × 0.25) + (Contract × 0.25) + (Runtime × 0.35) |

---

## 2. Plan Success Criteria Status

| # | Criterion | Status | Evidence |
|---|---|---|---|
| SC-1 | Tuple return 변경 | ✅ Met | `Task<(ObservedMetricsSnapshot[] Snapshots, long NewOffset)>` |
| SC-2 | StreamAsync 흐름 update (offset before yield) | ✅ Met | `lastReadOffset = newOffset` 다음에 `foreach yield` |
| SC-3 | Truncation 로직 보존 (fs.Length < startOffset → reset) | ✅ Met | ReadNew 내부로 이동, 로직 동일 |
| SC-4 | FileShare.ReadWrite | FileShare.Delete 보존 | ✅ Met | line 65 |
| SC-5 | Dashboard 빌드 0/0 | ✅ Met | 0 errors (2 무해 warnings) |
| SC-6 | Dashboard test 20/0/0 회귀 0 | ✅ Met | 731ms |
| SC-7 | FastPortSharp.sln 빌드 + 139 tests 회귀 0 | ✅ Met | 0/0 + 139/0/0 |
| SC-8 | CI 무변경 | ✅ Met | `.github/workflows/` diff 0 |
| SC-9 | 한국어 주석 race window 명시 | ✅ Met | "consumer-generator yield/resume gap race 회피" 주석 |
| SC-10 | 단일 commit | ✅ Met | `197072a` |

**Met**: 10/10 | **Partial**: 0 | **Not Met**: 0

---

## 3. Functional Deep-Dive

### 3.1 Race Window 분석 (수정 효과)

| 시나리오 | Before | After | Δ |
|---|---|---|---|
| Consumer가 yield 받고 즉시 append (manual enumerator) | ❌ 데이터 손실 | ✅ 안전 | **fix 핵심** |
| Producer가 generator delay 중 append (실 사용) | ✅ 안전 | ✅ 안전 | 변화 없음 (이미 안전했음) |
| File truncate (`fs.Length < offset`) | ✅ 안전 | ✅ 안전 | 위치만 이동 (StreamAsync → ReadNew) |
| 파일 부재 | ✅ 빈 array | ✅ 빈 array + startOffset 보존 | tuple로 명시화 |
| IOException | ✅ 빈 array | ✅ 빈 array + newOffset=startOffset | 변화 없음 |

### 3.2 Public API 보존 검증

| 항목 | Before | After |
|---|---|---|
| `StreamAsync(CancellationToken)` 시그너처 | `IAsyncEnumerable<ObservedMetricsSnapshot>` | 동일 |
| 외부 호출 패턴 | `await foreach (var snap in adapter.StreamAsync(ct))` | 동일 |
| IPollingAdapter interface | unchanged | unchanged |

→ Consumer (ViewModel `PumpAsync`, Test) 변경 0.

### 3.3 Test Compatibility 검증

| Test | 패턴 | 결과 |
|---|---|---|
| T-JA-1 (`Stream_3Lines_Yields3Snapshots`) | 3 line write → CollectAsync(3) | ✅ pass |
| T-JA-2 (`Stream_OffsetPersistsBetweenIntervals`) | 백그라운드 writer 250ms delay append | ✅ pass |
| T-JA-3 (`Stream_FileTruncated_RestartsFromBeginning`) | 백그라운드 truncate + write | ✅ pass |
| T-JA-4 (`Stream_MalformedLine_SkipsAndContinues`) | malformed line filter | ✅ pass |
| T-JA-5 (`Stream_ConcurrentWriteWithReadWriteShare_NoIOException`) | 동시 write FileShare 검증 | ✅ pass |

→ 5/5 JsonlPollingAdapter test pass. 기존 race 회피용 백그라운드 writer 패턴이 fix 후에도 그대로 작동 (race 시나리오는 더 이상 발생하지 않음).

---

## 4. Decision Record Verification

| Decision | Followed? | Evidence |
|---|---|---|
| [Plan] Fix scope: Race 수정 + 기존 test 유지 | ✅ | Test 파일 git diff 0 |
| [Plan] Public API 보존 | ✅ | StreamAsync signature unchanged |
| [Plan] Truncation 로직 보존 | ✅ | `fs.Length < startOffset` 동일 로직 (위치만 이동) |
| [Plan] 단일 commit | ✅ | `197072a` |
| [Design] Option A — Tuple return | ✅ | `Task<(...[], long)>` |
| [Design] fileLength = fs.Length capture timing (open 직후) | ✅ | line 73 |

---

## 5. Gap List

### Severity: Critical
없음.

### Severity: Important
없음.

### Severity: Minor

| # | Gap | Location | Recommendation |
|---|---|---|---|
| G-1 | SkiaSharp OpenGLES warning (SDK 무해) | Build output | 무관, Dashboard 전체 cycle 공통 |
| G-2 | T-JA-2/T-JA-3 백그라운드 writer 패턴 유지 (race 우회 필요 없음에도) | Test | 사용자 확정대로 유지. 향후 cycle에서 manual enumerator 패턴 복원 가능 (선택). |
| G-3 | StreamReader buffer 너머 read 시 partial line at EOF 처리 | ReadNew | 현재 TryDeserialize가 skip하지만 partial line 데이터는 영구 손실 가능. Out of scope, 별도 cycle. |

---

## 6. Runtime Verification

| Level | Status | Detail |
|---|---|---|
| Build Contract | ✅ Pass | Dashboard 0/0 + 회귀 sln 0/0 |
| Unit Tests (Dashboard) | ✅ Pass | 20/0/0 (731ms) |
| Regression Tests | ✅ Pass | 139/0/0 |

---

## 7. Conclusion

**Overall Match Rate: 100%** (runtime-weighted).

- ✅ 10/10 Plan SC Met, 0 Critical/Important Gap
- ✅ Public API 보존 (consumer 영향 0)
- ✅ Dashboard 빌드 0/0, 20 tests 회귀 0
- ✅ FastPortSharp.sln 회귀 0 (139 tests)
- ✅ Race window 명시적으로 좁힘 (production correctness 보강)
- ✅ 단일 commit, 1 file 변경

**Recommendation**: 90% threshold 충족 + Critical/Important 0 → `/pdca report` 즉시 진행. Iterator 불필요.

---

## Version History

| Version | Date | Changes | Author |
|---|---|---|---|
| 0.1 | 2026-05-11 | Initial analysis (Match Rate 100%, 10/10 SC met, 0 Critical/Important) | boinred |
