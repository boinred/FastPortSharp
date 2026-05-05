# cloud-staged-rtt-tail-validation - Design Document

> Version: 1.0.0 | Date: 2026-05-05 | Status: Draft
> Level: Starter | Plan: docs/01-plan/features/cloud-staged-rtt-tail-validation.plan.md

---

## 1. Overview

This feature is a runtime validation feature. It does not change the FastPort engine. It uses the existing Azure server/local runner topology to run staged cloud load validation with the latest client receive close, phase completion, operation duration, and per-session RTT diagnostics.

The key design decision is to run the staged profile one stage at a time and stop on the first meaningful instability instead of jumping directly to 10K.

## 2. Validation Topology

```text
Azure VM
  FastPortTestSmokeServer
  scripts/cloud/server-start.sh
  server.metrics.jsonl

Local Mac
  FastPortTestLoadValidation
  FastPortTestLoadRunner
  artifacts/load-validation/cloud-staged-rtt-tail-validation/
```

The server is restarted before every load stage to avoid stale sessions contaminating the next stage.

## 3. Stage Order

| Order | Stage | Target | Stop Decision |
|-------|-------|--------|---------------|
| 1 | smoke | 10/25 | Required endpoint sanity check |
| 2 | `s1-fixed-1k` | 1,000 | Continue if disconnects/timeouts are 0 |
| 3 | `s2-random-1k` | 1,000 | Continue if random payload does not introduce hard failures |
| 4 | `s3-random-3k` | 3,000 | Stop if tail or server lifecycle instability appears |
| 5 | `s4-random-5k` | 5,000 | Run only if 3K remains clean |
| 6 | `s5-random-10k` | 10,000 | Run only if 5K remains clean |

## 4. Metrics To Evaluate

### 4.1 Hard Guardrails

- `peakSessionRatio`
- `finalDisconnectCount`
- `maxSocketErrorRate`
- `socketErrorCountsByClass`
- `receiveCloseCountsByClass`

### 4.2 Tail Diagnostics

- `maxRttP95Ms`
- `maxRttP99Ms`
- `maxSessionRttP95OfP95Ms`
- `maxSessionRttMaxSessionP95Ms`
- slowest session entries
- `operationDurations.receive-header`
- `operationDurations.receive-body`
- `operationDurations.send-write`

### 4.3 Server Lifecycle Diagnostics

The runner summary does not merge live server metrics in the split topology. Therefore the latest server metric is checked after each stage:

- `currentSessions`
- `totalAcceptedSessions`
- `totalDisconnectedSessions`
- `socketErrorCount`
- `pendingSendRequests`
- `maxPendingSendRequests`
- `sendBackpressureEvents`
- `sendRejectedRequests`

## 5. Stop Conditions

Stop before the next stage when any of the following is true:

- validation summary fails;
- final disconnects are non-zero;
- receive timeout or connection reset appears materially;
- server `currentSessions` remains non-zero after runner completion;
- RTT tail grows enough that the next stage would likely only amplify the same failure shape.

## 6. Artifact Layout

```text
artifacts/load-validation/cloud-staged-rtt-tail-validation/
  smoke/
  s1-fixed-1k/
  s2-random-1k/
  s3-random-3k/
  runner/
  manifest.runner-*.json
  manifest.runner-*.md
```

## 7. Verification

- Build `FastPortTestLoadRunner` and `FastPortTestLoadValidation` in Release.
- Run cloud smoke first.
- Run each selected stage with `dotnet run --no-build`.
- Confirm `summary.md` and `summary.json` exist for each executed stage.
- Check `jq empty docs/.pdca-status.json`.
- Check `git diff --check`.
