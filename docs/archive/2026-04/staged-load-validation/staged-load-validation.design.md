# staged-load-validation - Design Document

> Version: 1.0.0 | Date: 2026-04-28 | Status: Completed
> Level: Starter | Plan: docs/01-plan/features/staged-load-validation.plan.md

---

## 1. Overview

`staged-load-validation` adds an opt-in validation tool around `FastPortSmokeServer` and `FastPortLoadRunner`. The tool runs a predefined stage matrix, stores observed JSONL metrics per stage, parses the results, and emits machine-readable and human-readable summaries.

The design intentionally keeps high-load validation out of default `dotnet test`. Unit tests should verify stage definitions, command generation, JSONL parsing, and pass/fail evaluation with small sample files. Real 1,000-10,000 session runs are manual/performance workflows.

## 2. Architecture

```text
FastPortSmokeServer
  listens on host:port

FastPortLoadValidation
  selects validation profile
  creates artifacts/load-validation/{run-id}/
  runs FastPortLoadRunner once per stage
  parses each stage JSONL
  evaluates thresholds
  writes manifest.json, summary.json, summary.md

FastPortLoadRunner
  executes one stage
  writes ObservedMetricsSnapshot JSONL
```

### 2.1 Project Boundary

Add a new console project:

```text
FastPortLoadValidation/
  FastPortLoadValidation.csproj
  Program.cs
  LoadValidationOptions.cs
  LoadValidationProfile.cs
  LoadValidationStage.cs
  LoadRunnerCommandBuilder.cs
  JsonlObservedMetricsReader.cs
  LoadValidationEvaluator.cs
  LoadValidationSummaryWriter.cs
  ProcessRunner.cs
  Properties/AssemblyInfo.cs
```

`FastPortLoadValidation` should reference `LibNetworks` so it can deserialize `ObservedMetricsSnapshot`. It should not reference `FastPortLoadRunner` internals. It runs `FastPortLoadRunner` as a process so the validation path matches actual CLI usage.

### 2.2 Dependency Direction

| Project | Dependency |
|---------|------------|
| `FastPortLoadValidation` | `LibNetworks` |
| `FastPortLoadRunner` | existing dependencies only |
| `FastPortSmokeServer` | existing dependencies only |
| `LibNetworks` | no dependency on validation tools |

This preserves the engine/test boundary: validation tooling depends on the engine and telemetry contract, not the reverse.

## 3. CLI Design

### 3.1 Primary Commands

```bash
dotnet run -c Release --project FastPortLoadValidation -- --profile smoke
dotnet run -c Release --project FastPortLoadValidation -- --profile staged
```

### 3.2 Options

| Option | Default | Description |
|--------|---------|-------------|
| `--profile <smoke|staged>` | `smoke` | Select validation profile. |
| `--host <host>` | `127.0.0.1` | Target server host. |
| `--port <port>` | `6628` | Target server port. |
| `--output <dir>` | `artifacts/load-validation/{timestamp}` | Run output directory. |
| `--stage <id>` | all stages | Run only one stage, e.g. `s3-random-3k`. |
| `--runner-project <path>` | `FastPortLoadRunner` | LoadRunner project path. |
| `--configuration <Debug|Release>` | `Release` | Build/run configuration. |
| `--dry-run` | false | Print commands without executing them. |
| `--continue-on-failure` | false | Continue remaining stages after a failed stage. |

### 3.3 Server Mode

First implementation should assume the smoke server is already running. This keeps process lifecycle simple and avoids masking server startup problems.

Recommended workflow:

```bash
dotnet run -c Release --project FastPortSmokeServer
dotnet run -c Release --project FastPortLoadValidation -- --profile staged
```

Managed server startup can be added later as a separate feature if this manual prerequisite becomes painful.

## 4. Stage Profiles

### 4.1 Smoke Profile

The smoke profile is CI-safe and short.

| Stage ID | Sessions | Payload | Duration | Ramp-up | Rate/session |
|----------|----------|---------|----------|---------|--------------|
| `smoke-fixed-10` | 10 | `fixed:1024` | 10s | 2s | 1 |
| `smoke-random-25` | 25 | `random:4096-16384` | 15s | 5s | 1 |

This profile may be run manually or in CI if the smoke server lifecycle is controlled by the test job.

### 4.2 Staged Profile

The staged profile is opt-in and performance oriented.

| Stage ID | Sessions | Payload | Duration | Ramp-up | Rate/session |
|----------|----------|---------|----------|---------|--------------|
| `s1-fixed-1k` | 1000 | `fixed:8192` | 2m | 30s | 1 |
| `s2-random-1k` | 1000 | `random:4096-16384` | 2m | 30s | 1 |
| `s3-random-3k` | 3000 | `random:4096-16384` | 3m | 60s | 1 |
| `s4-random-5k` | 5000 | `random:4096-16384` | 5m | 90s | 1 |
| `s5-random-10k` | 10000 | `random:4096-16384` | 5m | 120s | 1 |

## 5. Data Model

### 5.1 Input DTOs

```csharp
internal sealed record LoadValidationStage(
    string Id,
    int Sessions,
    string Payload,
    int SendRatePerSession,
    TimeSpan RampUp,
    TimeSpan Duration,
    TimeSpan MetricsInterval,
    LoadValidationThresholds Thresholds);

internal sealed record LoadValidationThresholds(
    double MinPeakSessionRatio,
    double MaxSocketErrorRate,
    double MaxDisconnectRatio,
    int MinJsonSamples);
```

### 5.2 Output DTOs

```csharp
internal sealed record LoadValidationRunManifest(
    string RunId,
    DateTimeOffset StartedAt,
    string Profile,
    string Host,
    int Port,
    IReadOnlyList<LoadValidationStage> Stages);

internal sealed record LoadValidationStageSummary(
    string StageId,
    bool Passed,
    int TargetSessions,
    int PeakCurrentSessions,
    double PeakSessionRatio,
    long TotalSentPackets,
    long TotalReceivedPackets,
    double MaxSocketErrorRate,
    long FinalDisconnectCount,
    double MaxTps,
    double MaxSentBytesPerSecond,
    double MaxReceivedBytesPerSecond,
    double MaxRttP95Ms,
    double MaxRttP99Ms,
    int JsonSamples,
    string MetricsPath,
    IReadOnlyList<string> Failures);

internal sealed record LoadValidationRunSummary(
    string RunId,
    bool Passed,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    IReadOnlyList<LoadValidationStageSummary> Stages);
```

### 5.3 JSONL Input

Each line is deserialized as:

```csharp
ObservedMetricsSnapshot snapshot;
ClientObservedMetricsSnapshot client = snapshot.ClientObserved;
```

A line without `clientObserved` is invalid for LoadRunner validation.

## 6. Evaluation Rules

### 6.1 Default Thresholds

| Rule | Default |
|------|---------|
| Minimum JSON samples | 3 |
| Minimum peak session ratio | 0.95 |
| Maximum socket error rate | 0.01 |
| Maximum disconnect ratio | 0.05 |

### 6.2 Stage Pass Conditions

A stage passes only if all conditions are true:

- JSONL file exists and every non-empty line is valid observed JSON.
- At least `MinJsonSamples` samples contain `clientObserved`.
- `PeakCurrentSessions / TargetSessions >= MinPeakSessionRatio`.
- `MaxSocketErrorRate <= MaxSocketErrorRate`.
- `FinalDisconnectCount / TargetSessions <= MaxDisconnectRatio`.
- `TotalReceivedPackets > 0`.
- `MaxTps > 0`.

Latency fields are recorded for trend comparison, but this feature does not fail on RTT thresholds yet. RTT thresholds should be introduced only after baseline data exists.

### 6.3 Run Pass Conditions

A full run passes if every executed stage passes. If `--continue-on-failure` is false, the first failed stage stops the run and the summary is still written.

## 7. File Output

Output directory:

```text
artifacts/load-validation/{yyyyMMdd-HHmmss}-{profile}/
```

Files:

```text
manifest.json
summary.json
summary.md
{stage-id}.metrics.jsonl
{stage-id}.stdout.log
{stage-id}.stderr.log
```

`artifacts/load-validation/` must be added to `.gitignore` so generated results are not committed by default.

## 8. Command Generation

For each stage, `LoadRunnerCommandBuilder` produces:

```bash
dotnet run -c Release --project FastPortLoadRunner -- \
  --host 127.0.0.1 \
  --port 6628 \
  --sessions 10000 \
  --payload random:4096-16384 \
  --rate 1 \
  --ramp-up 120s \
  --duration 5m \
  --metrics-interval 1s \
  --output artifacts/load-validation/{run-id}/s5-random-10k.metrics.jsonl
```

`--dry-run` prints these commands and writes no summaries.

## 9. Implementation Order

1. Add `.gitignore` entry for `artifacts/load-validation/`.
2. Add `FastPortLoadValidation` project and include it in `FastPortCharp.sln`.
3. Implement option parsing and profile/stage models.
4. Implement LoadRunner command generation.
5. Implement process execution with stdout/stderr log capture.
6. Implement JSONL parser using `ObservedMetricsJson.SerializerOptions`.
7. Implement stage/run evaluator and summary writers.
8. Add unit tests for profiles, command generation, parser, and evaluator.
9. Run `dotnet test`.

## 10. Test Plan

### 10.1 Unit Tests

Add tests in `LibCommonTest`.

Recommended tests:

- `LoadValidationOptions_TryParse_UsesDefaults`
- `LoadValidationProfiles_StagedProfile_HasExpectedStages`
- `LoadRunnerCommandBuilder_BuildsStageCommand`
- `JsonlObservedMetricsReader_ReadsClientObservedSamples`
- `LoadValidationEvaluator_PassesHealthySamples`
- `LoadValidationEvaluator_FailsLowPeakSessions`
- `LoadValidationEvaluator_FailsSocketErrors`

Expose validation internals to `LibCommonTest` with:

```csharp
[assembly: InternalsVisibleTo("LibCommonTest")]
```

### 10.2 Integration / Manual Verification

Manual smoke:

```bash
dotnet run -c Release --project FastPortSmokeServer
dotnet run -c Release --project FastPortLoadValidation -- --profile smoke
```

Manual staged:

```bash
dotnet run -c Release --project FastPortSmokeServer
dotnet run -c Release --project FastPortLoadValidation -- --profile staged
```

### 10.3 Default Verification

```bash
dotnet test
```

Full staged validation is not part of default tests.

## 11. Operational Notes

- High session counts may require OS file descriptor limits to be raised before running.
- A failed 10,000 session stage is not automatically an engine regression; summary output should preserve enough data to distinguish environment limits from engine behavior.
- Do not commit generated `artifacts/load-validation/` output unless a future report explicitly requires selected baseline artifacts.

## 12. Acceptance Criteria

- [ ] `FastPortLoadValidation` can generate smoke and staged stage definitions.
- [ ] Validation commands use existing `FastPortLoadRunner` CLI options.
- [ ] JSONL parser consumes `ObservedMetricsSnapshot` client metrics.
- [ ] Evaluator produces pass/fail stage summaries.
- [ ] `summary.json` and `summary.md` are written for executed runs.
- [ ] `artifacts/load-validation/` is ignored by git.
- [ ] Unit tests cover profile, command generation, parser, and evaluator.
- [ ] `dotnet test` passes.
