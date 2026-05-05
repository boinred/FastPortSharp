# Gap Analysis: cloud-staged-rtt-tail-validation

> Date: 2026-05-05 | Design: docs/02-design/features/cloud-staged-rtt-tail-validation.design.md

---

## Match Rate: 91%

Design items evaluated: 11

Implemented / validated items: 10

Remaining gap: 1

## Summary

`cloud-staged-rtt-tail-validation` met its primary purpose: it re-ran the Azure server/local runner path with the latest receive close, phase completion, operation duration, and per-session RTT diagnostics, then identified the first meaningful instability point.

The important result is that the failure shape does not require 10K to appear. The path remains stable by hard guardrails at 1K, but by `s3-random-3k` the RTT tail and server lifecycle problem are already clear:

- `s3-random-3k` RTT P95: `30,431.21ms`
- `s3-random-3k` RTT P99: `107,050.72ms`
- `s3-random-3k` session RTT p95-of-p95: `118,358.76ms`
- `s3-random-3k` slowest session P95: `208,252.42ms`
- `connect|SocketException|TimedOut = 6`
- server `currentSessions` remained non-zero after runner completion

The staged ladder correctly stopped before `s4-random-5k`.

## Implemented Items

- [x] Azure server was clean-started before load stages.
- [x] Local runner endpoint connectivity was verified before staged validation.
- [x] Cloud smoke validation passed before staged load.
- [x] `s1-fixed-1k` ran and passed.
- [x] `s2-random-1k` ran and passed.
- [x] `s3-random-3k` ran and produced the stop condition.
- [x] `s4-random-5k` and `s5-random-10k` were intentionally not run after the 3K stop condition.
- [x] Hard guardrails were reviewed: peak sessions, final disconnects, socket error rate, and socket error classes.
- [x] Tail diagnostics were reviewed: RTT P95/P99, per-session RTT tail, receive-header/body duration, and send-write duration.
- [x] Runtime result documentation was updated in `docs/load-validation-benchmark-results.md`.

## Result Matrix

| Stage | Result | Peak | Final Disconnects | Socket Errors | RTT P95 | RTT P99 | Session RTT P95-of-P95 | Slowest Session P95 |
|-------|--------|------|------------------:|--------------:|--------:|--------:|-----------------------:|--------------------:|
| smoke | Passed | 25/25 | 0 | 0.00% | 94.27ms | 136.19ms | 161.99ms | 244.30ms |
| `s1-fixed-1k` | Passed | 1000/1000 | 0 | 0.00% | 1,139.18ms | 2,799.12ms | 2,474.44ms | 31,025.53ms |
| `s2-random-1k` | Passed | 1000/1000 | 0 | 0.00% | 4,560.84ms | 10,673.63ms | 11,679.07ms | 74,855.55ms |
| `s3-random-3k` | Passed by guardrail | 2994/3000 | 0 | 0.003% | 30,431.21ms | 107,050.72ms | 118,358.76ms | 208,252.42ms |

## Diagnostics

### Stable Signals

- `s1-fixed-1k` and `s2-random-1k` reached `100%` peak sessions.
- `s1-fixed-1k`, `s2-random-1k`, and `s3-random-3k` had final disconnect count `0`.
- `s1-fixed-1k` and `s2-random-1k` had no socket errors.
- Client `send-write` remained low:
  - `s1-fixed-1k`: avg `0.13ms`, max `14.74ms`
  - `s2-random-1k`: avg `0.11ms`, max `14.89ms`
  - `s3-random-3k`: avg `0.10ms`, max `69.44ms`

### Failure Shape

The cloud tail grows sharply when moving from 1K random to 3K random:

| Metric | `s2-random-1k` | `s3-random-3k` | Change |
|--------|---------------:|---------------:|-------:|
| Max pending requests | `1,820` | `10,372` | `+470%` |
| RTT P95 | `4,560.84ms` | `30,431.21ms` | `+567%` |
| RTT P99 | `10,673.63ms` | `107,050.72ms` | `+903%` |
| Session RTT p95-of-p95 | `11,679.07ms` | `118,358.76ms` | `+913%` |
| Slowest session P95 | `74,855.55ms` | `208,252.42ms` | `+178%` |

Operation duration at `s3-random-3k`:

- `receive-header` max: `212,445.14ms`
- `receive-body` max: `205,681.72ms`
- `send-write` max: `69.44ms`

This keeps the prior interpretation intact: the client send path is not the dominant cause of the 100s-level tail. The dominant signal is receive wait plus connection lifecycle cleanup.

## Missing Items

- [ ] Server lifecycle metrics are not automatically merged into each staged summary.

The split topology still requires a manual server metric check after each stage. That was enough to make the stop decision, but it is not ideal for repeatable reporting:

- initial post-3K server check: `currentSessions = 768`, `pendingSendRequests = 6`
- delayed post-3K server check: `currentSessions = 50`, `pendingSendRequests = 8`
- server socket errors after 3K: `9`

These values are recorded in the Do notes, but they are not in the `summary.json` for `s3-random-3k`.

## Changed Items

- [x] `s4-random-5k` and `s5-random-10k` were skipped.

This is not a design miss. The design explicitly required stopping when 3K showed tail or server lifecycle instability. The skip is the correct behavior.

## Gap Categories

| Category | Item | Status |
|----------|------|--------|
| Match | Existing Azure server/local runner topology | Complete |
| Match | Clean server start before stages | Complete |
| Match | Smoke before staged validation | Complete |
| Match | 1K fixed validation | Complete |
| Match | 1K random validation | Complete |
| Match | 3K random validation | Complete |
| Match | Stop before 5K/10K on lifecycle/tail instability | Complete |
| Match | Hard guardrail review | Complete |
| Match | Tail diagnostic review | Complete |
| Match | Benchmark markdown update | Complete |
| Missing in tooling | Automatic server metric merge for split cloud stage summaries | Open |

## Recommendations

1. Proceed to report for this PDCA item. The validation goal is met and the remaining gap is a follow-up tooling item, not a blocker.
2. Do not run 5K/10K until the 3K lifecycle/tail issue is explained.
3. Split the next PDCA item around server lifecycle visibility:
   - server-side disconnect reason counters;
   - accepted/disconnected/current session reconciliation;
   - pending send requests at disconnect/close;
   - split-run server metric collection attached to each stage summary.
4. Keep engine changes out of the next step until lifecycle telemetry says where the cleanup delay originates.

## Next Steps

- [x] Finish analysis.
- [ ] Run `$pdca report cloud-staged-rtt-tail-validation`.
- [ ] Open a follow-up feature for cloud server lifecycle/disconnect reason telemetry.
