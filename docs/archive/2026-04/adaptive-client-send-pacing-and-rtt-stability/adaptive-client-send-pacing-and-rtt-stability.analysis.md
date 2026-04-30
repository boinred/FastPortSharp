# Gap Analysis: adaptive-client-send-pacing-and-rtt-stability

> Date: 2026-04-29 | Design: docs/02-design/features/adaptive-client-send-pacing-and-rtt-stability.design.md

---

## Match Rate: 92%

설계 체크포인트 25개 중 23개가 충족됐다. 첫 분석에서 남았던 manifest self-description, abandoned permit release test, observed pacing deserialize test, focused 10K validation, benchmark 문서 갱신은 이번 iterate에서 닫혔다.

남은 항목은 구현 누락보다는 운영 판단 리스크다. adaptive-window 기본값은 `NoBufferSpaceAvailable`, socket error rate, final disconnect, receive timeout, RTT P99 기준을 만족했지만 RTT P95와 scheduler drift는 목표보다 높다. 따라서 Report로 진행할 수는 있으나, adaptive policy를 기본값으로 승격하기 전에는 별도 튜닝 pass가 필요하다.

## Implemented Items

- [x] `LoadPacingPolicy`와 `LoadPacingOptions`를 추가했다.
- [x] 기본 policy는 `none`이며 기존 baseline 동작은 유지된다.
- [x] `--pacing-policy`, fixed/adaptive window, RTT threshold, increase interval CLI 옵션을 추가했다.
- [x] 기존 `--max-pending-requests-per-session`은 fixed-window shortcut으로 유지했다.
- [x] `LoadScenario`는 nullable cap 대신 `LoadPacingOptions Pacing`을 사용한다.
- [x] `Program.PrintPlan`은 effective pacing policy와 threshold를 출력한다.
- [x] `OutstandingRequestPacer`가 per-session event-driven gate를 제공한다.
- [x] fixed-window wait path는 1ms polling 대신 waiter signal 기반으로 동작한다.
- [x] adaptive-window는 RTT target/high 기준으로 slow increase / fast decrease를 수행한다.
- [x] `LoadSession` send loop가 permit 예약, send 실패 시 abandon, response 시 RTT 기반 release를 호출한다.
- [x] `MetricsCollector`와 `MetricsSnapshot`에 pacing wait/window counter가 추가됐다.
- [x] `ClientObservedMetricsSnapshot`에 pacing field가 optional/default 값으로 추가됐다.
- [x] `ObservedMetricsExtensions`가 pacing metric을 client observed snapshot으로 매핑한다.
- [x] `FastPortLoadValidation` CLI가 pacing 옵션을 parse하고 runner command로 전달한다.
- [x] `LoadValidationRunManifest`가 policy 문자열뿐 아니라 fixed window, min/initial/max window, RTT threshold, increase interval을 기록한다.
- [x] `LoadValidationEvaluator`가 pacing counter를 stage summary로 집계한다.
- [x] `LoadValidationSummaryWriter`가 compact `Pacing` column을 출력한다.
- [x] load runner option parse, legacy shortcut, invalid mixed option, fixed gate, adaptive increase/decrease unit test가 추가됐다.
- [x] `OutstandingRequestPacer.OnRequestAbandoned()`가 reserved permit을 release하는 직접 unit test가 추가됐다.
- [x] validation option parse, command forwarding, evaluator aggregate, summary writer, manifest option test가 추가됐다.
- [x] observed metrics mapping/JSON output test와 pacing field deserialize round-trip test가 추가됐다.
- [x] local build/test/release build가 통과했다.
- [x] adaptive smoke validation이 통과했고 manifest/summary에 pacing 정보가 출력됐다.
- [x] focused 10K fixed-window event-gate run을 실행했다.
- [x] focused 10K adaptive-window run을 실행했다.
- [x] `docs/load-validation-benchmark-results.md`에 event-gate/adaptive 결과 비교를 반영했다.

## Remaining Items

- [ ] adaptive-window 기본값의 RTT P95는 `16,234.27ms`로 first target `12,000ms`보다 높다.
- [ ] adaptive-window 기본값의 max scheduler drift는 `320.86ms`로 baseline/fixed cap 대비 높다.

## Changed Items

- [ ] 설계는 `OutstandingRequestPacingSnapshot CreateSnapshot()`을 제안했지만 구현은 별도 snapshot DTO 없이 `MetricsCollector`에 직접 기록한다. 현재 telemetry/summary 요구는 충족하므로 accepted deviation으로 둔다.

## Verification

- `dotnet build FastPortCharp.sln`: Passed
- `dotnet test FastPortCharp.sln --no-build`: Passed, 93/93
- `dotnet build FastPortCharp.sln -c Release`: Passed
- `FastPortLoadValidation --profile smoke --pacing-policy adaptive-window ...`: Passed
  - Output: `artifacts/load-validation/adaptive-pacing-iterate-smoke/summary.md`
  - Manifest includes full pacing options.
- focused 10K fixed-window event-gate: Passed
  - Output: `artifacts/load-validation/s5-fixed-cap-4-event-gate/summary.md`
  - `NoBufferSpaceAvailable = 1,149`
  - `receive|IOException|TimedOut = 5,150`
  - RTT P95/P99: `17,832.44ms / 26,785.33ms`
- focused 10K adaptive-window: Passed
  - Output: `artifacts/load-validation/s5-adaptive-pacing-window/summary.md`
  - `NoBufferSpaceAvailable = 1,415`
  - No material receive timeout
  - Socket error rate: `0.05%`
  - RTT P95/P99: `16,234.27ms / 18,420.99ms`
  - Observed window: `1-5`, window `+/- = 142/424`

## Recommendations

1. Proceed to `$pdca report adaptive-client-send-pacing-and-rtt-stability`.
2. Treat adaptive-window as an opt-in validation policy, not a default, until a tuning pass improves RTT P95 and scheduler drift.
3. Create a follow-up feature for adaptive threshold tuning if the next goal is default policy selection.
4. Keep the `CreateSnapshot()` deviation documented unless a consumer needs a separate pacing snapshot object.

## Next Steps

- [ ] `$pdca report adaptive-client-send-pacing-and-rtt-stability`
