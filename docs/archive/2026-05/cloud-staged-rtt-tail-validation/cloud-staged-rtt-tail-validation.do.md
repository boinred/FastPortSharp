# cloud-staged-rtt-tail-validation - Do Notes

> Date: 2026-05-05 | Status: Stopped at 3K random

---

## 1. Execution Summary

This run started the cloud staged validation ladder after the receive close and phase completion diagnostics were added.

Topology:

```text
Azure server VM: FastPortTestSmokeServer
Local runner: FastPortTestLoadValidation / FastPortTestLoadRunner
Output root: artifacts/load-validation/cloud-staged-rtt-tail-validation/
```

The server was restarted before each load stage. 5K and 10K were not executed because 3K random produced a stop condition.

## 2. Commands Executed

Readiness and smoke:

```text
scripts/cloud/runner-connectivity.sh
scripts/cloud/runner-smoke.sh
```

Stage runs:

```text
dotnet run --no-build -c Release --project FastPortTestLoadValidation -- --profile staged --stage s1-fixed-1k --host <redacted> --port 6628 --pacing-policy adaptive-window --output artifacts/load-validation/cloud-staged-rtt-tail-validation/s1-fixed-1k --runner-no-build
dotnet run --no-build -c Release --project FastPortTestLoadValidation -- --profile staged --stage s2-random-1k --host <redacted> --port 6628 --pacing-policy adaptive-window --output artifacts/load-validation/cloud-staged-rtt-tail-validation/s2-random-1k --runner-no-build
dotnet run --no-build -c Release --project FastPortTestLoadValidation -- --profile staged --stage s3-random-3k --host <redacted> --port 6628 --pacing-policy adaptive-window --output artifacts/load-validation/cloud-staged-rtt-tail-validation/s3-random-3k --runner-no-build
```

## 3. Results

| Stage | Result | Peak | Final Disconnects | Socket Errors | RTT P95 | RTT P99 | Session RTT P95-of-P95 | Slowest Session P95 |
|-------|--------|------|------------------:|--------------:|--------:|--------:|-----------------------:|--------------------:|
| smoke | Passed | 25/25 | 0 | 0.00% | 94.27ms | 136.19ms | 161.99ms | 244.30ms |
| `s1-fixed-1k` | Passed | 1000/1000 | 0 | 0.00% | 1,139.18ms | 2,799.12ms | 2,474.44ms | 31,025.53ms |
| `s2-random-1k` | Passed | 1000/1000 | 0 | 0.00% | 4,560.84ms | 10,673.63ms | 11,679.07ms | 74,855.55ms |
| `s3-random-3k` | Passed by guardrail | 2994/3000 | 0 | 0.003% | 30,431.21ms | 107,050.72ms | 118,358.76ms | 208,252.42ms |

## 4. Diagnostics

### 4.1 s1-fixed-1k

- Summary: `artifacts/load-validation/cloud-staged-rtt-tail-validation/s1-fixed-1k/summary.md`
- Run ID: `20260505-162709-staged`
- Receive close counters: none
- Phase completion counters: none
- Operation max:
  - `receive-header`: `32,634.68ms`
  - `receive-body`: `16,485.75ms`
  - `send-write`: `14.74ms`

### 4.2 s2-random-1k

- Summary: `artifacts/load-validation/cloud-staged-rtt-tail-validation/s2-random-1k/summary.md`
- Run ID: `20260505-163155-staged`
- Receive close counters: none
- Phase completion counters: none
- Operation max:
  - `receive-header`: `47,584.00ms`
  - `receive-body`: `66,810.64ms`
  - `send-write`: `14.89ms`

### 4.3 s3-random-3k

- Summary: `artifacts/load-validation/cloud-staged-rtt-tail-validation/s3-random-3k/summary.md`
- Run ID: `20260505-163556-staged`
- Client-side socket classification:
  - `connect|SocketException|TimedOut = 6`
- Operation max:
  - `receive-header`: `212,445.14ms`
  - `receive-body`: `205,681.72ms`
  - `send-write`: `69.44ms`
- Server metric shortly after runner completion:
  - initial check: `currentSessions = 768`, `pendingSendRequests = 6`
  - delayed check: `currentSessions = 50`, `pendingSendRequests = 8`
  - server socket errors: `9`

## 5. Stop Decision

Stop before `s4-random-5k`.

Reasons:

- 3K passed only by hard validation guardrails, but RTT tail is already severe.
- `s3-random-3k` created connect timeouts.
- The server still had lingering sessions after runner completion.
- This reproduces the same cloud lifecycle/tail shape seen in the previous 10K baseline at a smaller stage.

## 6. Immediate Interpretation

The first meaningful failure point is not 10K. The problem starts showing clearly by `s3-random-3k`.

Client send is still not the primary suspect:

- `send-write` average remains near `0.10ms`.
- `send-write` max is `69.44ms`, which is not large enough to explain 100s-level RTT tails.

The dominant signal is still receive wait and connection lifecycle:

- `receive-header` and `receive-body` max exceed `200s` at 3K.
- server session cleanup lags after runner completion.
- connect timeouts appear at 3K.

## 7. Next

Proceed to `$pdca analyze cloud-staged-rtt-tail-validation`.

The analysis should decide whether the next implementation should add server lifecycle telemetry, server-side disconnect reason counters, or a controlled runner/server placement comparison before more engine changes.
