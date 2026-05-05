# throughput-pacing-server-processing-decomposition - Do Notes

> Plan: docs/01-plan/features/throughput-pacing-server-processing-decomposition.plan.md
> Design: docs/02-design/features/throughput-pacing-server-processing-decomposition.design.md
> Status: Completed

## Implemented In This Pass

Added a summary decomposition helper:

- `scripts/load-validation/decompose-summary.sh`

The script reads a `FastPortLoadValidation` `summary.json` file and emits a Markdown diagnostic report for:

- RTT tail shape
- client pacing pressure
- client outstanding request depth
- server send path pressure
- socket pressure
- scheduler noise
- recommended next optimization lane

Default input:

```bash
scripts/load-validation/decompose-summary.sh
```

Explicit input:

```bash
scripts/load-validation/decompose-summary.sh artifacts/load-validation/s5-session-rtt-validation/summary.json
```

## Runtime Behavior

No runtime behavior was changed.

No `LibNetworks`, `FastPortLoadRunner`, `FastPortLoadValidation`, or `FastPortSmokeServer` code path was modified in this pass. This implements Phase 1 from the design: derived diagnostic mapping from existing metrics.

## Verification Output

Command:

```bash
scripts/load-validation/decompose-summary.sh artifacts/load-validation/s5-session-rtt-validation/summary.json
```

Key result:

| Segment | Finding | Evidence |
|---------|---------|----------|
| RTT tail shape | systemic broad pressure | global P95 and per-session P95-of-P95 gap ratio `5.20%` |
| Client pacing | pacing is actively throttling | average wait `2857.09ms`, window `1-5` |
| Client outstanding depth | broad outstanding backlog | `3.67` pending requests/session |
| Server send path | server send pressure visible | pending send `1,095`, backpressure `1,583`, buffer `64,204` bytes |
| Socket pressure | socket pressure visible | `NoBufferSpaceAvailable=1,639`, receive timeouts `184` |
| Local scheduler | scheduler drift low | max drift `12.12ms` |

Recommended next lane:

```text
adaptive-client-pacing-threshold-tuning
```

## Interpretation

The latest same-machine 10K run is still not a production capacity proof.

The artifact indicates broad RTT pressure rather than a small set of isolated slow sessions:

- global RTT P95: `19,210.39ms`
- per-session P95-of-P95: `18,211.02ms`
- gap ratio: `5.20%`

The strongest local signal is that adaptive pacing is already collapsing to a narrow window and spending significant time waiting, while outstanding request depth is still broad. Server send/socket pressure is also visible, but the next lowest-risk decomposition/optimization should tune pacing thresholds before touching engine send behavior again.

## Verification

- `bash -n scripts/load-validation/decompose-summary.sh`
- `scripts/load-validation/decompose-summary.sh artifacts/load-validation/s5-session-rtt-validation/summary.json`
- `git diff --check`

No `dotnet build` or `dotnet test` was required because the pass added a shell diagnostic helper and documentation only.

## Next

Run gap analysis:

```text
$pdca analyze throughput-pacing-server-processing-decomposition
```

Likely follow-up feature after report/archive:

```text
$pdca pm adaptive-client-pacing-threshold-tuning
```
