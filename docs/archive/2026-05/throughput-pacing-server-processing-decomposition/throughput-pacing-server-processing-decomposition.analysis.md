# Gap Analysis: throughput-pacing-server-processing-decomposition

> Date: 2026-05-01 | Design: docs/02-design/features/throughput-pacing-server-processing-decomposition.design.md

---

## Match Rate: 92%

## Summary

The implementation matches the diagnostic-first intent of the design.

Runtime behavior was not changed. Instead, `scripts/load-validation/decompose-summary.sh` performs Phase 1 from the design by reading an existing `summary.json` and producing a Markdown decomposition report.

The current implementation is sufficient to proceed to report because it:

- decomposes the latest 10K RTT tail from existing metrics,
- identifies systemic broad pressure,
- surfaces pacing, pending request, server send, and socket-pressure signals,
- recommends a single next optimization lane: `adaptive-client-pacing-threshold-tuning`.

The only meaningful gap is that the script does not emit a full metric-to-pipeline segment mapping table. The design asked for mapping existing summary fields to model segments; the script currently emits segment findings and evidence, but not the complete coverage matrix.

## Implemented Items

- [x] Read latest `FastPortLoadValidation` `summary.json`.
- [x] Emit Markdown diagnostic report.
- [x] Report RTT tail shape using global RTT P95 and per-session P95-of-P95 gap.
- [x] Report client pacing pressure using pacing average wait and pacing window range.
- [x] Report client outstanding request depth using pending requests/session.
- [x] Report server send pressure using pending send requests, backpressure events, and send buffer bytes.
- [x] Report socket pressure using `NoBufferSpaceAvailable` and receive timeout counts.
- [x] Report scheduler noise using max scheduler drift.
- [x] Recommend exactly one next optimization lane.
- [x] Avoid runtime behavior changes.
- [x] Avoid `LibNetworks` changes.
- [x] Document results in the feature do notes.
- [x] Verify script syntax and execution.

## Missing Items

- [ ] Full coverage matrix output from the script:
  - design segment
  - existing summary field
  - source component
  - current observed value

## Changed Items (Deviations from Design)

- [x] No direct runtime metric additions were implemented.
  - This is intentional and aligned with Phase 1, because existing metrics were enough to choose the next lane.
- [x] No smoke or 10K rerun was performed.
  - This is acceptable because runtime code was not changed; the implementation is a post-processing diagnostic helper.

## Evidence

Command:

```bash
scripts/load-validation/decompose-summary.sh artifacts/load-validation/s5-session-rtt-validation/summary.json
```

Result:

| Segment | Finding | Evidence |
|---------|---------|----------|
| RTT tail shape | systemic broad pressure | gap ratio `5.20%` |
| Client pacing | pacing is actively throttling | average wait `2857.09ms`, window `1-5` |
| Client outstanding depth | broad outstanding backlog | `3.67` pending requests/session |
| Server send path | server send pressure visible | pending send `1,095`, backpressure `1,583`, buffer `64,204` bytes |
| Socket pressure | socket pressure visible | `NoBufferSpaceAvailable=1,639`, receive timeouts `184` |
| Local scheduler | scheduler drift low | max drift `12.12ms` |

Recommended next lane:

```text
adaptive-client-pacing-threshold-tuning
```

## Match Calculation

| Design Item | Status |
|-------------|--------|
| Diagnostic summary mapping without runtime behavior change | Match |
| Current artifact can be analyzed locally | Match |
| RTT tail shape is classified | Match |
| Client pacing pressure is classified | Match |
| Client outstanding depth is classified | Match |
| Server send pressure is classified | Match |
| Socket pressure is classified | Match |
| Scheduler noise is classified | Match |
| Single next optimization lane is recommended | Match |
| Do notes capture verification and interpretation | Match |
| Full metric-to-segment coverage matrix emitted by script | Missing |
| Runtime timing metrics deferred unless needed | Match |
| Engine queue residency hook deferred | Match |

Implemented: `12 / 13`

Match rate:

```text
12 / 13 = 92%
```

## Recommendations

1. Proceed to report because match rate is above 90%.
2. Treat the missing coverage matrix as optional polish unless the user wants richer script output before archive.
3. Start the next optimization feature as `adaptive-client-pacing-threshold-tuning` after this feature is reported/archived.
4. Re-run this decomposition script against cloud split artifacts when OCI A1 capacity becomes available.

## Next Steps

- [x] Match rate is above 90%.
- [ ] Run `$pdca report throughput-pacing-server-processing-decomposition`.
