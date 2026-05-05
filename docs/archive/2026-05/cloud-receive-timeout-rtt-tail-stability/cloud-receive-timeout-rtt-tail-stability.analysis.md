# Gap Analysis: cloud-receive-timeout-rtt-tail-stability

> Date: 2026-05-05 | Design: docs/02-design/features/cloud-receive-timeout-rtt-tail-stability.design.md

---

## Match Rate: 94%

Evaluation basis: 16 matched items out of 17 design/acceptance items.

The iterate step closed the local coverage gap and completed cloud smoke validation from a clean Azure server process. The only remaining gap is staged cloud validation, which should be handled as the next runtime validation activity rather than as a blocker for the instrumentation code.

## Summary

The implementation aligns with the design at the code and smoke-validation level.

The feature now distinguishes:

- socket timeout/reset;
- orderly EOF;
- partial EOF while reading a body;
- send/receive phase completion, cancellation, and fault;
- outstanding request count at receive close.

The important design constraint was respected: no engine send path, `BaseSession`, or server send queue behavior was changed.

## Implemented Items

- [x] `ReadAsync == 0` is classified separately from socket exceptions.
  - `FastPortTestLoadRunner/LoadSession.cs`
  - `MetricsCollector.RecordReceiveClose(...)`

- [x] Header EOF, body EOF, and partial body EOF are recorded by operation/reason/class.
  - `receive-header|eof`
  - `receive-body|eof`
  - `receive-body|partial-eof`

- [x] Outstanding request count at receive close is captured.
  - `MaxOutstandingRequestsAtReceiveClose`

- [x] Phase completion reason counters are captured.
  - `send|cancelled`
  - `receive|completed`
  - `receive|faulted`

- [x] Existing socket error classification is preserved.
  - `SocketErrorCountsByPhase`
  - `SocketErrorCountsByType`
  - `SocketErrorCountsByCode`
  - `SocketErrorCountsByClass`

- [x] Existing validation guardrails remain intact.
  - max socket error rate
  - final disconnect count
  - disconnect ratio
  - receive timeout socket class threshold

- [x] Client observed metrics contract has additive fields.
  - `receiveCloseCountsByOperation`
  - `receiveCloseCountsByReason`
  - `receiveCloseCountsByClass`
  - `maxOutstandingRequestsAtReceiveClose`
  - `phaseCompletionCounts`

- [x] Existing JSONL artifacts without the new fields remain readable by defaulted record properties.

- [x] `FastPortTestLoadValidation` propagates close/phase diagnostics to `summary.json`.

- [x] `summary.md` includes receive close and phase completion lines when counters are present.

- [x] Unit tests cover header EOF receive close classification.

- [x] Unit tests cover body EOF receive close classification.

- [x] Unit tests cover partial body EOF receive close classification.

- [x] Unit tests cover observed metrics serialization/deserialization and summary output.

- [x] Azure runbook documents server restart, listener check, and stale-session check before load runs.

- [x] Cloud smoke validation passed after restarting the Azure server with `currentSessions=0`.
  - Run ID: `20260505-154128-smoke`
  - Summary: `artifacts/load-validation/cloud-server-runner-split/smoke/summary.md`
  - `smoke-fixed-10`: passed, peak `10/10`, final disconnects `0`, socket error rate `0.00%`
  - `smoke-random-25`: passed, peak `25/25`, final disconnects `0`, socket error rate `0.00%`

## Missing Items

- [ ] Staged cloud validation has not been executed after this instrumentation change.
  - No new 1K/3K/5K/focused 10K output exists yet for the new close/phase diagnostics.
  - Benchmark markdown should remain unchanged until this exists.
  - This is a runtime validation follow-up, not a local code implementation gap.

## Changed Items

- [x] `scripts/cloud/*.sh` was not changed.
  - Design allowed cloud hygiene to be handled through scripts or runbook.
  - Current implementation updates `docs/azure-server-runner-split-load-validation-runbook.md` instead of adding a restart script.
  - This remains acceptable because the smoke run followed the documented restart/readiness sequence.

- [x] Server lifecycle telemetry was not added.
  - Design marked this as conditional: add only if runner/validation/script diagnostics are still insufficient.
  - Smoke validation did not show disconnects, socket errors, or close counters, so engine telemetry extension is still not justified.

## Verification Evidence

Executed during `do` and `iterate` phases:

```text
dotnet build FastPortCharp.sln -c Release
dotnet test FastPortCharp.sln -c Release --no-build
bash -n scripts/cloud/*.sh
jq empty docs/.pdca-status.json
git diff --check
scripts/cloud/runner-connectivity.sh
scripts/cloud/runner-smoke.sh
```

Result:

- Build passed with `0` warnings and `0` errors.
- Tests passed: `117/117`.
- Shell syntax check passed.
- PDCA status JSON is valid.
- Diff whitespace check passed.
- Azure server connectivity passed.
- Cloud smoke validation passed.

## Recommendations

1. Proceed to `$pdca report cloud-receive-timeout-rtt-tail-stability`.
2. Keep benchmark result markdown unchanged until staged cloud validation produces new artifacts.
3. Run staged cloud validation as the next runtime validation item:
   - 1K
   - 3K
   - 5K
   - focused 10K
4. Add server lifecycle telemetry only if staged validation still leaves reset/timeout/lingering sessions unexplained.

## Next Steps

- [x] Add direct `receive-body|eof` unit coverage.
- [x] Re-run build/test/shell/status checks.
- [x] Run cloud smoke validation from a clean server process.
- [x] Re-run `$pdca analyze cloud-receive-timeout-rtt-tail-stability`.
- [ ] Proceed to `$pdca report cloud-receive-timeout-rtt-tail-stability`.
