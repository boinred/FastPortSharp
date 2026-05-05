# cloud-staged-rtt-tail-validation - Completion Report

> Version: 1.0.0 | Date: 2026-05-05 | Status: Completed
> Match Rate: 91% | Iterations: 1

---

## 1. Summary

`cloud-staged-rtt-tail-validation` completed the staged cloud runtime validation that followed `cloud-receive-timeout-rtt-tail-stability`.

This feature did not modify engine send/receive logic. It used the existing Azure server/local runner topology and the newly added diagnostics to identify where the cloud RTT tail and connection lifecycle problem first becomes meaningful.

Final decision:

- Smoke passed.
- `s1-fixed-1k` passed.
- `s2-random-1k` passed.
- `s3-random-3k` passed by hard validation guardrails, but produced the stop condition.
- `s4-random-5k` and `s5-random-10k` were intentionally not run.

The important result is that the failure shape does not require 10K. It is already visible at 3K random payload.

## 2. Related Documents

| Phase | Document |
|-------|----------|
| Plan | `docs/01-plan/features/cloud-staged-rtt-tail-validation.plan.md` |
| Design | `docs/02-design/features/cloud-staged-rtt-tail-validation.design.md` |
| Do | `docs/02-design/features/cloud-staged-rtt-tail-validation.do.md` |
| Analysis | `docs/03-analysis/cloud-staged-rtt-tail-validation.analysis.md` |
| Benchmark Summary | `docs/load-validation-benchmark-results.md` |

## 3. Completed Items

- [x] Created a scoped follow-up plan for staged cloud validation.
- [x] Designed a one-stage-at-a-time cloud validation ladder.
- [x] Restarted the Azure test server before each load stage.
- [x] Verified local runner connectivity to the Azure endpoint.
- [x] Ran cloud smoke validation.
- [x] Ran `s1-fixed-1k`.
- [x] Ran `s2-random-1k`.
- [x] Ran `s3-random-3k`.
- [x] Stopped before 5K/10K after the 3K stop condition.
- [x] Updated `docs/load-validation-benchmark-results.md` with staged cloud results.
- [x] Wrote gap analysis and identified follow-up tooling work.

## 4. Runtime Results

| Stage | Result | Peak | Final Disconnects | Socket Errors | RTT P95 | RTT P99 | Session RTT P95-of-P95 | Slowest Session P95 |
|-------|--------|------|------------------:|--------------:|--------:|--------:|-----------------------:|--------------------:|
| smoke | Passed | 25/25 | 0 | 0.00% | 94.27ms | 136.19ms | 161.99ms | 244.30ms |
| `s1-fixed-1k` | Passed | 1000/1000 | 0 | 0.00% | 1,139.18ms | 2,799.12ms | 2,474.44ms | 31,025.53ms |
| `s2-random-1k` | Passed | 1000/1000 | 0 | 0.00% | 4,560.84ms | 10,673.63ms | 11,679.07ms | 74,855.55ms |
| `s3-random-3k` | Passed by guardrail | 2994/3000 | 0 | 0.003% | 30,431.21ms | 107,050.72ms | 118,358.76ms | 208,252.42ms |

Artifact paths:

- `artifacts/load-validation/cloud-staged-rtt-tail-validation/smoke/summary.md`
- `artifacts/load-validation/cloud-staged-rtt-tail-validation/s1-fixed-1k/summary.md`
- `artifacts/load-validation/cloud-staged-rtt-tail-validation/s2-random-1k/summary.md`
- `artifacts/load-validation/cloud-staged-rtt-tail-validation/s3-random-3k/summary.md`

## 5. Stop Condition

Stop point: `s3-random-3k`

Reasons:

- RTT P95 rose to `30,431.21ms`.
- RTT P99 rose to `107,050.72ms`.
- Session RTT p95-of-p95 rose to `118,358.76ms`.
- Slowest session P95 reached `208,252.42ms`.
- Client socket classification recorded `connect|SocketException|TimedOut = 6`.
- Server sessions lingered after runner completion:
  - immediate post-run check: `currentSessions = 768`, `pendingSendRequests = 6`
  - delayed check: `currentSessions = 50`, `pendingSendRequests = 8`
  - server socket errors after 3K: `9`

5K and 10K would likely amplify the same problem instead of answering a new question.

## 6. Interpretation

The staged cloud result keeps the previous diagnosis intact:

- client `send-write` is not the dominant cause;
- `send-write` max at 3K is only `69.44ms`;
- the dominant signal is receive wait plus connection lifecycle cleanup;
- receive-header and receive-body max values exceed `200s` at 3K;
- server session reconciliation lags after runner completion.

This shifts the next work away from generic send-path tuning and toward lifecycle visibility:

- server-side disconnect reason counters;
- accepted/disconnected/current session reconciliation;
- pending send requests at close/disconnect;
- automatic server metric attachment to split cloud stage summaries.

## 7. Quality Metrics

| Metric | Result |
|--------|--------|
| Match rate | `91%` |
| Iteration count | `1` |
| Cloud smoke | Passed |
| 1K fixed stage | Passed |
| 1K random stage | Passed |
| 3K random stage | Stop condition found |
| Benchmark markdown update | Completed |
| PDCA status JSON | Passed |
| Diff whitespace | Passed |

Verification commands used in this feature:

```text
dotnet build FastPortTestLoadRunner/FastPortTestLoadRunner.csproj -c Release
dotnet build FastPortTestLoadValidation/FastPortTestLoadValidation.csproj -c Release
scripts/cloud/runner-connectivity.sh
scripts/cloud/runner-smoke.sh
dotnet run --no-build -c Release --project FastPortTestLoadValidation -- --profile staged --stage s1-fixed-1k ...
dotnet run --no-build -c Release --project FastPortTestLoadValidation -- --profile staged --stage s2-random-1k ...
dotnet run --no-build -c Release --project FastPortTestLoadValidation -- --profile staged --stage s3-random-3k ...
jq empty docs/.pdca-status.json
git diff --check
```

Full solution tests were not rerun during the report step because this feature produced runtime validation artifacts and documentation changes only after the existing diagnostic code was already validated.

## 8. Remaining Gap

The only remaining gap is tooling, not the validation decision:

- server lifecycle metrics are not automatically merged into each split cloud stage summary.

Manual server checks were enough to identify the stop condition, but repeatable reporting needs server metric collection attached to each stage.

## 9. Lessons Learned

### Keep

- Keep cloud server/local runner as a distinct validation path from same-machine local tests.
- Keep hard receive timeout and disconnect guardrails.
- Keep restarting the Azure test server before each load stage.
- Keep ladder-style validation instead of jumping directly to 10K.

### Problem

- 1K random already shows large per-session tail.
- 3K random reproduces the lifecycle/tail problem without needing 10K.
- Validation summaries currently miss post-run server lifecycle state unless checked manually.

### Try

- Add server lifecycle/disconnect reason telemetry next.
- Attach server metric snapshots to each split cloud stage summary.
- Re-run 3K after lifecycle telemetry exists before attempting 5K.

## 10. Next Steps

Recommended next commands:

```text
$pdca archive cloud-staged-rtt-tail-validation
$pdca pm cloud-server-lifecycle-disconnect-telemetry
```
