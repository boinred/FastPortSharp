# loadrunner-10k-session-rtt-validation - Design Document

> Version: 1.0.0 | Date: 2026-04-30 | Status: Completed
> Level: Starter | Plan: docs/01-plan/features/loadrunner-10k-session-rtt-validation.plan.md

---

## 1. Overview

`loadrunner-10k-session-rtt-validation`은 새로 추가된 per-session RTT telemetry를 실제 focused 10K validation에서 검증하고, 다음 병목 feature를 선택하기 위한 측정 설계다.

이 design은 runtime code 변경을 요구하지 않는다. 이미 구현된 `FastPortLoadRunner`, `FastPortLoadValidation`, `ObservedMetrics` 계약을 사용해 다음을 확인한다.

- 10K load에서 `sessionRtt`가 raw observed JSONL에 기록되는지
- `LoadValidationEvaluator`가 `sessionRtt`를 stage summary로 집계하는지
- `summary.md`가 사람이 읽을 수 있는 session RTT tail 정보를 제공하는지
- 기존 latest 10K baseline과 새 결과를 같은 조건으로 비교할 수 있는지
- RTT tail이 전체 세션 문제인지 일부 세션 집중 문제인지 판정할 수 있는지

## 2. Validation Architecture

### 2.1 Runtime Flow

```text
FastPortSmokeServer
  |
  | TCP echo workload
  v
FastPortLoadValidation
  |
  | starts FastPortLoadRunner as a child process
  v
FastPortLoadRunner
  |
  | writes client observed metrics JSONL
  v
JsonlObservedMetricsReader
  |
  | deserializes ClientObservedMetricsSnapshot.SessionRtt
  v
LoadValidationEvaluator
  |
  | aggregates global RTT, session RTT, socket, pacing, pending metrics
  v
LoadValidationSummaryWriter
  |
  | writes summary.json and summary.md
  v
docs/load-validation-benchmark-results.md
```

### 2.2 Scope Boundary

| Area | Decision |
|------|----------|
| Engine code | No changes |
| LoadRunner telemetry code | No changes unless validation reveals missing output |
| Validation command | Reuse Release binaries and staged `s5-random-10k` |
| Pacing | Explicitly use `--pacing-policy adaptive-window` to match the latest adaptive baseline family |
| Benchmark docs | Update `docs/load-validation-benchmark-results.md` after run |
| Artifact storage | Keep generated artifacts under ignored `artifacts/load-validation/` |

## 3. Execution Design

### 3.1 Preflight Build

Build Release binaries before runtime validation.

```bash
dotnet build FastPortCharp.sln -c Release
```

Expected result:

- build passes with 0 errors
- `FastPortSmokeServer/bin/Release/net10.0/FastPortSmokeServer` exists
- `FastPortLoadValidation/bin/Release/net10.0/FastPortLoadValidation` exists

### 3.2 Server Process

Run the smoke server on the default validation port.

```bash
./FastPortSmokeServer/bin/Release/net10.0/FastPortSmokeServer
```

Operational rules:

- Use port `6628`.
- If the port is already occupied by an old smoke server, stop the stale process first.
- Keep the server running until validation finishes.
- Do not commit server runtime logs unless explicitly promoted to docs.

### 3.3 Focused 10K Validation

Use a dedicated output directory so this run is not confused with earlier artifacts.

```bash
./FastPortLoadValidation/bin/Release/net10.0/FastPortLoadValidation \
  --profile staged \
  --stage s5-random-10k \
  --pacing-policy adaptive-window \
  --output artifacts/load-validation/s5-session-rtt-validation
```

The selected stage has the following profile definition.

| Stage | Sessions | Payload | Rate | Ramp-up | Duration | Metrics Interval |
|-------|---------:|---------|-----:|--------:|---------:|-----------------:|
| `s5-random-10k` | 10,000 | `random:4096-16384` | 1/session | 120s | 5m | 1s |

Default adaptive pacing parameters are accepted unless a later run explicitly needs a comparison variant.

| Parameter | Value |
|-----------|------:|
| Min window | 1 |
| Initial window | 4 |
| Max window | 16 |
| RTT target | 12,000ms |
| RTT high watermark | 20,000ms |
| Increase every responses | 256 |

### 3.4 Expected Artifact Layout

```text
artifacts/load-validation/s5-session-rtt-validation/
  manifest.json
  summary.json
  summary.md
  s5-random-10k.metrics.jsonl
  s5-random-10k.stdout.log
  s5-random-10k.stderr.log
```

If server metrics are separately enabled later, `*.combined.metrics.jsonl` may also appear. It is not required for this feature.

## 4. Data Contract

### 4.1 Raw Client Observed JSONL

The raw client observed JSONL should include `clientObserved.sessionRtt` once at least one RTT sample exists.

Expected object shape:

```json
{
  "clientObserved": {
    "sessionRtt": {
      "trackedSessionCount": 10000,
      "eligibleSessionCount": 10000,
      "excludedLowSampleSessionCount": 0,
      "minSamplesPerSession": 8,
      "p50OfSessionP95Ms": 0,
      "p95OfSessionP95Ms": 0,
      "p99OfSessionP95Ms": 0,
      "maxSessionP95Ms": 0,
      "maxSessionP99Ms": 0,
      "maxSessionMaxMs": 0,
      "slowestSessions": []
    }
  }
}
```

Values above are illustrative; the validation checks field presence and measured values, not exact constants.

### 4.2 Summary JSON

`summary.json` stores session RTT values as stage-level summary fields on each `LoadValidationStageSummary`.

Required fields:

| Field | Meaning |
|-------|---------|
| `sessionRttTrackedSessionCount` | Number of sessions with any tracked RTT sample |
| `sessionRttEligibleSessionCount` | Sessions with at least `minSamplesPerSession` samples |
| `sessionRttExcludedLowSampleSessionCount` | Sessions excluded from tail stats due to low samples |
| `maxSessionRttP50OfP95Ms` | Maximum observed p50 of per-session P95 values across samples |
| `maxSessionRttP95OfP95Ms` | Maximum observed p95 of per-session P95 values across samples |
| `maxSessionRttP99OfP95Ms` | Maximum observed p99 of per-session P95 values across samples |
| `maxSessionRttMaxSessionP95Ms` | Worst session P95 across samples |
| `maxSessionRttMaxSessionP99Ms` | Worst session P99 across samples |
| `maxSessionRttMaxSessionMaxMs` | Worst session max RTT across samples |
| `slowestSessions` | Slow session entries selected by P95/P99/max tie-breakers |

### 4.3 Summary Markdown

`summary.md` should show:

- table column: `Session RTT`
- formatted value: `eligible={eligible}/{tracked}, p50/p95/p99-of-p95=...ms, max-p95=...ms`
- low sample line: `session RTT excluded low-sample sessions = ...`
- up to five slow session lines:
  - `slow session {id} p95=...ms p99=...ms max=...ms samples={sampleCount}/{totalSampleCount}`

## 5. Verification Design

### 5.1 Required Commands

After the run, inspect artifacts with deterministic shell checks.

```bash
sed -n '1,180p' artifacts/load-validation/s5-session-rtt-validation/summary.md
```

```bash
rg -n "Session RTT|slow session|session RTT excluded" \
  artifacts/load-validation/s5-session-rtt-validation/summary.md
```

```bash
rg -n "\"sessionRtt\"|\"sessionRttTrackedSessionCount\"|\"slowestSessions\"" \
  artifacts/load-validation/s5-session-rtt-validation
```

### 5.2 Required Metrics to Capture

Record these values into the analysis/report and benchmark document.

| Category | Metrics |
|----------|---------|
| Run identity | output path, started time, completed time, result |
| Capacity | peak sessions, final disconnects, active ratio |
| Throughput | max TPS, sent/received packet counts |
| Queue pressure | max pending request count, max pending send requests, max send buffer bytes |
| Server pressure | send backpressure events, rejected send, drain yield |
| Socket errors | socket error rate and Top classifications |
| Pacing | wait count/avg, observed window range, window increase/decrease |
| Global RTT | RTT P95, RTT P99 |
| Session RTT | tracked, eligible, excluded, p50/p95/p99-of-session-P95, max session P95/P99/max |
| Slow sessions | Top 5 session id, sample count, P95, P99, max |

## 6. Interpretation Design

### 6.1 Tail Concentration Heuristics

These rules guide the next PM decision. They are not hard pass/fail gates.

| Pattern | Evidence | Interpretation | Next Candidate |
|---------|----------|----------------|----------------|
| Broad slowdown | `maxSessionRttMaxSessionP95Ms` is close to `maxSessionRttP95OfP95Ms`; slowest Top 5 are not far above p95-of-p95 | Most sessions are slow together | throughput/pacing/server processing decomposition |
| Concentrated tail | `maxSessionRttMaxSessionP95Ms` is much larger than `maxSessionRttP95OfP95Ms`; Top 5 dominate | A small set of sessions create the tail | fairness/starvation/session backlog analysis |
| Unreliable session tail | `sessionRttExcludedLowSampleSessionCount` is large relative to tracked sessions | Not enough samples per session | measurement reliability/run stability pass |
| Receive-linked tail | slow session max/P99 is high while receive timeout classification is material | Tail may be receive/socket path related | receive-timeout/socket-error correlation |

### 6.2 Baseline Comparison

Compare the new run against the latest committed benchmark baseline:

`artifacts/load-validation/s5-send-channel-queue-batch-pool-adaptive/summary.md`

Baseline values to carry forward:

| Metric | Baseline |
|--------|---------:|
| Peak sessions | `9,975 / 10,000` |
| Final disconnects | `2` |
| Max TPS | `7,901.40` |
| Max pending request count | `38,246` |
| Max pending send requests | `1,282` |
| Server send backpressure events | `0` |
| `send\|IOException\|NoBufferSpaceAvailable` | `0` |
| `receive\|IOException\|TimedOut` | `1,266` |
| Socket error rate | `0.12%` |
| RTT P95 | `17,796.60ms` |
| RTT P99 | `27,398.15ms` |
| Max scheduler drift | `19.66ms` |

The new run is not required to improve these values. Its purpose is to explain the tail shape.

## 7. Benchmark Document Update

Update `docs/load-validation-benchmark-results.md` after analysis.

Add a new section under or near `BaseSession Send Channel Queue Follow-up`:

```markdown
## Session RTT Validation Follow-up

This run validates whether the latest focused 10K RTT tail is broad or concentrated by using per-session RTT telemetry.

- Baseline: `artifacts/load-validation/s5-send-channel-queue-batch-pool-adaptive/summary.md`
- Session RTT validation: `artifacts/load-validation/s5-session-rtt-validation/summary.md`
```

Include:

- comparison table for previous global metrics
- session RTT table
- slowest session Top 5 summary
- interpretation paragraph
- next feature recommendation

## 8. Test Strategy

No new unit tests are required by default because this feature validates runtime artifacts, and the previous feature already added coverage for:

- session RTT calculation
- JSON serialization/deserialization
- LoadValidation summary aggregation
- Markdown rendering

If validation reveals missing fields or broken summary output, add targeted tests before changing code.

Minimum non-runtime verification:

```bash
dotnet test FastPortCharp.sln --no-build
```

Minimum runtime verification:

```bash
./FastPortLoadValidation/bin/Release/net10.0/FastPortLoadValidation \
  --profile staged \
  --stage s5-random-10k \
  --pacing-policy adaptive-window \
  --output artifacts/load-validation/s5-session-rtt-validation
```

## 9. Implementation Order

1. Confirm Release build.
2. Start `FastPortSmokeServer`.
3. Run focused 10K validation with adaptive-window pacing.
4. Inspect `summary.md` for `Session RTT` and slow session lines.
5. Inspect `summary.json` for `sessionRtt*` stage fields.
6. Inspect raw JSONL for `clientObserved.sessionRtt`.
7. Compare global metrics with the previous latest 10K baseline.
8. Classify RTT tail using the interpretation heuristics.
9. Update `docs/load-validation-benchmark-results.md`.
10. Write PDCA analysis/report with the next feature recommendation.

## 10. Acceptance Checklist

- [ ] Release build passes.
- [ ] 10K validation run completes.
- [ ] `summary.md` contains `Session RTT`.
- [ ] `summary.md` contains slow session lines or records why none exist.
- [ ] `summary.json` contains non-zero session RTT summary fields.
- [ ] raw observed JSONL contains `clientObserved.sessionRtt`.
- [ ] benchmark document is updated.
- [ ] next bottleneck feature is selected from the run evidence.
