# Completion Report: adaptive-client-send-pacing-and-rtt-stability

> Date: 2026-04-30 | Level: Starter | Match Rate: 92%

---

## 1. Summary

### 1.1 Feature Overview

`adaptive-client-send-pacing-and-rtt-stability`는 focused 10K load에서 남아 있던 client-side `send|IOException|NoBufferSpaceAvailable`을 줄이기 위해 LoadRunner에 event-driven fixed/adaptive outstanding request pacing을 추가한 feature다.

구현은 server send queue/drain 정책을 다시 바꾸지 않고, load generator 쪽 pacing policy를 opt-in으로 추가했다. fixed-window는 기존 cap diagnostic을 polling 없이 재검증하기 위한 기준점이고, adaptive-window는 RTT 기반으로 per-session outstanding window를 조절하는 후보 정책이다.

### 1.2 Final Match Rate

`92%` (Target: `90%`)

첫 분석의 `84%`에서 iterate 후 `92%`로 개선됐다. manifest self-description, abandoned permit release test, observed pacing deserialize test, focused 10K validation, benchmark 문서 갱신 gap을 닫았다.

## 2. Completed Items

- [x] `LoadPacingPolicy` / `LoadPacingOptions` 추가
- [x] `none`, `fixed-window`, `adaptive-window` policy 지원
- [x] legacy `--max-pending-requests-per-session`를 fixed-window shortcut으로 유지
- [x] event-driven `OutstandingRequestPacer` 추가
- [x] send 실패 시 `OnRequestAbandoned()`로 reserved permit release
- [x] RTT 기반 adaptive increase/decrease 구현
- [x] pacing wait/window metrics 추가
- [x] observed client metric DTO에 pacing field 추가
- [x] `FastPortLoadValidation` CLI forwarding 추가
- [x] manifest에 effective pacing options 기록
- [x] validation summary에 `Pacing` column 추가
- [x] load runner, validation, observed metrics test 보강
- [x] focused 10K fixed-window event-gate run 실행
- [x] focused 10K adaptive-window run 실행
- [x] `docs/load-validation-benchmark-results.md`에 결과 비교 반영

## 3. Quality Metrics

| Metric | Value |
|--------|-------|
| Final match rate | `92%` |
| PDCA iterations | `3` |
| Feature files changed | `17` code/test/benchmark files |
| Build | `dotnet build FastPortCharp.sln` passed |
| Tests | `dotnet test FastPortCharp.sln --no-build` passed, `93/93` |
| Release build | `dotnet build FastPortCharp.sln -c Release` passed |
| Smoke validation | Passed |
| focused 10K fixed-window event-gate | Passed |
| focused 10K adaptive-window | Passed |

## 4. Focused 10K Results

| Metric | Baseline | Event-Gate Cap 4 | Adaptive Window |
|--------|---------:|-----------------:|----------------:|
| Peak sessions | `10,000 / 10,000` | `10,000 / 10,000` | `10,000 / 10,000` |
| Final disconnects | `0` | `89` | `0` |
| Max pending request count | `36,653` | `37,294` | `36,384` |
| Max pending send requests | `905` | `219` | `212` |
| Server send backpressure events | `4,153` | `1,453` | `501` |
| Max send buffer bytes | `195,683` | `63,586` | `63,233` |
| `send\|IOException\|NoBufferSpaceAvailable` | `7,344` | `1,149` | `1,415` |
| `receive\|IOException\|TimedOut` | None material | `5,150` | None material |
| Socket error rate | `0.55%` | `0.36%` | `0.05%` |
| RTT P95 | `10,611.83ms` | `17,832.44ms` | `16,234.27ms` |
| RTT P99 | `12,949.47ms` | `26,785.33ms` | `18,420.99ms` |
| Max scheduler drift | `12.04ms` | `56.04ms` | `320.86ms` |

## 5. Result Interpretation

Adaptive-window is the better current candidate. It keeps peak sessions at `100%`, keeps final disconnects at `0`, removes material receive timeouts, lowers socket error rate to `0.05%`, and reduces `NoBufferSpaceAvailable` from `7,344` to `1,415`.

Fixed event-gate cap `4` confirms that removing polling alone is not enough. It lowers NoBuffer, but still creates a large receive-timeout tradeoff (`5,150`) and keeps RTT tail above target.

Adaptive-window should remain opt-in for validation. It meets the first NoBuffer, socket error rate, disconnect, receive timeout, and RTT P99 targets, but RTT P95 (`16,234.27ms`) and max scheduler drift (`320.86ms`) need a dedicated tuning pass before default policy selection.

## 6. Deviations from Design

- `OutstandingRequestPacingSnapshot CreateSnapshot()` was not implemented as a separate DTO. The implementation records pacing state directly into `MetricsCollector` / `MetricsSnapshot`, which satisfies the current JSONL and summary requirements with less surface area.
- Adaptive pacing is implemented in `FastPortLoadRunner` only. This matches the design decision to avoid promoting the policy into `LibNetworks` before validation proves broader runtime value.

## 7. Related Documents

- Plan: `docs/01-plan/features/adaptive-client-send-pacing-and-rtt-stability.plan.md`
- Design: `docs/02-design/features/adaptive-client-send-pacing-and-rtt-stability.design.md`
- Analysis: `docs/03-analysis/adaptive-client-send-pacing-and-rtt-stability.analysis.md`
- Benchmark results: `docs/load-validation-benchmark-results.md`
- Adaptive 10K summary: `artifacts/load-validation/s5-adaptive-pacing-window/summary.md`
- Event-gate cap 4 summary: `artifacts/load-validation/s5-fixed-cap-4-event-gate/summary.md`

## 8. Follow-up Items

- [ ] Create a follow-up feature for adaptive pacing threshold tuning.
- [ ] Investigate max scheduler drift under adaptive-window.
- [ ] Tune for RTT P95 target `<= 12,000ms` without regressing `NoBufferSpaceAvailable`.
- [ ] Keep adaptive-window opt-in until a tuned run clears the remaining P95/drift risk.

## 9. Next Steps

- [ ] `$pdca archive adaptive-client-send-pacing-and-rtt-stability`
