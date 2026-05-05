# Completion Report: throughput-pacing-server-processing-decomposition

> Date: 2026-05-03 | Status: Completed | Level: Starter

---

## 1. Summary

`throughput-pacing-server-processing-decomposition` completed the diagnostic decomposition pass for the current focused same-machine 10K artifact.

This feature did not change runtime networking behavior. It added a post-processing helper that reads an existing `FastPortLoadValidation` `summary.json` and classifies the observed RTT tail into client pacing, client outstanding depth, server send pressure, socket pressure, and scheduler noise.

Final conclusion:

The current 10K RTT tail is broad load pressure, not only a few isolated slow sessions. The strongest next lane is `adaptive-client-pacing-threshold-tuning`, because pacing is already collapsing to a narrow window and spending significant time waiting while outstanding request depth remains broad.

## 2. Related Documents

- Plan: `docs/01-plan/features/throughput-pacing-server-processing-decomposition.plan.md`
- Design: `docs/02-design/features/throughput-pacing-server-processing-decomposition.design.md`
- Do: `docs/02-design/features/throughput-pacing-server-processing-decomposition.do.md`
- Analysis: `docs/03-analysis/throughput-pacing-server-processing-decomposition.analysis.md`
- Script: `scripts/load-validation/decompose-summary.sh`
- Source artifact: `artifacts/load-validation/s5-session-rtt-validation/summary.json`

## 3. Completed Items

- [x] Defined a decomposition model for the current 10K RTT tail.
- [x] Mapped existing client/server summary signals to decomposition segments.
- [x] Added `scripts/load-validation/decompose-summary.sh`.
- [x] Classified RTT tail shape from global RTT P95 and per-session P95-of-P95.
- [x] Classified client pacing pressure from pacing average wait and pacing window range.
- [x] Classified client outstanding depth from pending requests/session.
- [x] Classified server send pressure from pending send requests, backpressure events, and send buffer bytes.
- [x] Classified socket pressure from `NoBufferSpaceAvailable` and receive timeout counts.
- [x] Classified local scheduler noise from max scheduler drift.
- [x] Recommended one next optimization lane.
- [x] Avoided runtime behavior changes.
- [x] Avoided `LibNetworks` changes.

## 4. Quality Metrics

| Check | Result |
|-------|--------|
| Match rate | `92%` |
| Implemented design items | `12 / 13` |
| PDCA iterations | `1` |
| Runtime code changed | No |
| `LibNetworks` changed | No |
| New diagnostic script | `185` lines |
| Feature docs before report | `598` lines |
| Shell syntax check | Passed |
| Script execution | Passed |
| `git diff --check` | Passed |

Verification commands:

```bash
bash -n scripts/load-validation/decompose-summary.sh
scripts/load-validation/decompose-summary.sh artifacts/load-validation/s5-session-rtt-validation/summary.json
git diff --check
```

No `dotnet build` or `dotnet test` was required for this report phase because no C# runtime code changed in this feature.

## 5. Diagnostic Result

Command:

```bash
scripts/load-validation/decompose-summary.sh artifacts/load-validation/s5-session-rtt-validation/summary.json
```

Key output:

| Segment | Finding | Evidence |
|---------|---------|----------|
| RTT tail shape | systemic broad pressure | global P95/per-session P95 gap `5.20%` |
| Client pacing | pacing is actively throttling | average wait `2,857.09ms`, window `1-5` |
| Client outstanding depth | broad outstanding backlog | `3.67` pending requests/session |
| Server send path | server send pressure visible | pending send `1,095`, backpressure `1,583`, buffer `64,204` bytes |
| Socket pressure | socket pressure visible | `NoBufferSpaceAvailable=1,639`, receive timeouts `184` |
| Local scheduler | scheduler drift low | max drift `12.12ms` |

Selected next lane:

```text
adaptive-client-pacing-threshold-tuning
```

## 6. Deviations From Design

- The script emits segment findings and evidence, but not a full metric-to-pipeline segment coverage matrix.
  - This is acceptable because the missing item is optional output polish. The analysis still reached a data-backed next-lane decision.
- No direct timing metrics were added for client write/read duration or smoke-server echo processing duration.
  - This is intentional. Phase 1 derived analysis from existing metrics was sufficient to select the next lane.
- No same-machine 10K rerun was performed.
  - This is acceptable because runtime code did not change. The feature consumed the existing validated 10K artifact.

## 7. Lessons Learned

### Keep

- Keep decomposition and optimization as separate PDCA features.
- Keep `LibNetworks` untouched until lower-risk client/load-runner evidence is exhausted.
- Keep using per-session RTT and merged server metrics when interpreting 10K tail behavior.

### Problem

- Same-machine 10K data is noisy enough that it should not be treated as production capacity proof.
- The current 10K result still has very high RTT P95/P99 despite reaching `10,000 / 10,000` sessions.
- Socket pressure and server send pressure still appear, but pacing pressure is the cleaner next optimization boundary.

### Try

- Tune adaptive client pacing thresholds before another engine send-path change.
- Add client write/read duration aggregates only if pacing tuning still cannot explain the RTT tail.
- Re-run the same decomposition script on cloud split artifacts once OCI A1 capacity is available.

## 8. Follow-up Items

- [ ] Archive this feature after report completion.
- [ ] Start `$pdca pm adaptive-client-pacing-threshold-tuning`.
- [ ] Re-run decomposition against cloud split server/runner artifacts when free-tier OCI capacity becomes available.
- [ ] Consider adding the optional coverage matrix output to `decompose-summary.sh` only if future reports need richer traceability.

Suggested next command:

```text
$pdca archive throughput-pacing-server-processing-decomposition
```
