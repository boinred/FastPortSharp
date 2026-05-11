# dashboard-e2e-mock-tests Analysis

> **Project**: FastPortSharp · **Date**: 2026-05-11 · **Commit**: `e6474c1`

## Match Rate: 100%

| Axis | Score |
|---|---|
| Structural | 100% (1 신규 파일, Plan §6 매칭) |
| Functional | 100% (5 E2E scenarios 모두 구현) |
| Contract | 100% (25/0/0, 회귀 sln 0/0/139/0/0) |
| Runtime | 100% (25 tests 실제 실행, 1s) |

## Plan SC

| # | Criterion | Status |
|---|---|---|
| SC-1 | MockE2ETests.cs 신규 (5 tests) | ✅ |
| SC-2 | 5 tests 모두 pass | ✅ |
| SC-3 | 기존 20 tests 회귀 0 (총 25) | ✅ |
| SC-4 | Dashboard 빌드 0/0 | ✅ |
| SC-5 | FastPortSharp.sln 회귀 0 | ✅ |
| SC-6 | Test 실행 시간 < 5s | ✅ (1s) |
| SC-7 | CI workflow path filter 자동 trigger 확인 | ✅ (dashboard.yml의 `tests-projects/FastPortDashboardTests/**` glob에 자동 포함) |
| SC-8 | 단일 commit | ✅ |

**Met: 8/8** (100%)

## E2E Coverage Audit

| Scenario | Component coverage |
|---|---|
| E2E-1 Full pipeline | MockAdapter + StreamAsync + ApplySnapshot + ServerObserved + ClientObserved 매핑 |
| E2E-2 RTT percentiles | TimedRttPoint(P50/P95/P99) + ApplyClientSnapshot |
| E2E-3 Cancellation | PumpAsync + CancellationToken propagation + ViewModel state invariants |
| E2E-4 Monotonic KPI | ServerObservedMetricsSnapshot 통계 누적 + Toolkit ObservableProperty setter |
| E2E-5 Lifecycle | UseMock + ConnectCommand (RelayCommand async) + State transitions + DisconnectCommand |

→ ViewModel + Adapter pipeline의 핵심 경로 모두 cover. UI render 의존성 0.

## Gap List

| Severity | Gap | Recommendation |
|---|---|---|
| Critical | 없음 | — |
| Important | 없음 | — |
| Minor | JSONL adapter E2E 미포함 | 별도 cycle `dashboard-jsonl-e2e-tests` 가능 (tmp file + producer 시뮬레이션) |

## Conclusion

**Match Rate 100%**, 0 Critical/Important. UI 실행 검증이 차단된 macOS 26 환경에서 비즈니스 로직 신뢰 verification 경로 확보. 25 tests 1s 실행으로 CI 비용 미미. Mock pipeline + ViewModel lifecycle 모두 cover.

직전 cycle (`dashboard-revert-skcanvasview-keep-data`)의 Critical SC-7 (Debug crash 미해결)은 본 cycle scope 외이나, **verification 경로 전환 완료**로 macOS 26 crash가 비즈니스 로직 회귀 감지에 미치는 영향 0이 됨.
