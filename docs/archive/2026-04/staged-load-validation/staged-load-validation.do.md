# staged-load-validation - Do Document

> Version: 1.0.0 | Date: 2026-04-28 | Status: Completed
> Design: docs/02-design/features/staged-load-validation.design.md

---

## 1. Implementation Summary

Added `FastPortLoadValidation`, an opt-in console tool for staged load validation. It selects a smoke or staged profile, runs `FastPortLoadRunner` per stage, reads observed JSONL metrics, evaluates pass/fail thresholds, and writes run summaries.

The full 1,000-10,000 session validation remains manual/performance scope. Default unit tests validate the profile definitions, command generation, JSONL parser, evaluator, and summary writer without running high-load sessions.

## 2. Code Changes

| File | Change |
|------|--------|
| `.gitignore` | Ignores generated `artifacts/load-validation/` output. |
| `FastPortCharp.sln` | Adds `FastPortLoadValidation` project. |
| `FastPortLoadValidation/` | New console validation tool. |
| `LibCommonTest/LibCommonTest.csproj` | References `FastPortLoadValidation`. |
| `LibCommonTest/FastPortLoadValidationTests.cs` | Adds unit coverage for options, profiles, command generation, JSONL reader, evaluator, and summary writer. |

## 3. Implemented Components

- `LoadValidationOptions`: parses validation CLI options.
- `LoadValidationProfiles`: defines smoke and staged profile matrices.
- `LoadValidationStage` / threshold / summary records: represent validation input/output contracts.
- `LoadRunnerCommandBuilder`: creates `FastPortLoadRunner` process commands.
- `JsonlObservedMetricsReader`: deserializes `ObservedMetricsSnapshot` JSONL and extracts client observed samples.
- `LoadValidationEvaluator`: computes peak sessions, packet totals, TPS, error ratios, and pass/fail failures.
- `LoadValidationSummaryWriter`: writes `manifest.json`, `summary.json`, and `summary.md`.
- `ProcessRunner`: runs stage processes and captures stdout/stderr logs.
- `Program`: orchestrates selected stages and supports `--dry-run`.

## 4. Supported Commands

Dry-run staged command:

```bash
./FastPortLoadValidation/bin/Debug/net10.0/FastPortLoadValidation --profile staged --stage s5-random-10k --output artifacts/load-validation/dry-run --dry-run
```

Generated command:

```bash
dotnet run -c Release --project FastPortLoadRunner -- --host 127.0.0.1 --port 6628 --sessions 10000 --payload random:4096-16384 --rate 1 --ramp-up 120s --duration 5m --metrics-interval 1s --output artifacts/load-validation/dry-run/s5-random-10k.metrics.jsonl
```

Manual smoke workflow:

```bash
dotnet run -c Release --project FastPortSmokeServer
dotnet run -c Release --project FastPortLoadValidation -- --profile smoke
```

Manual staged workflow:

```bash
dotnet run -c Release --project FastPortSmokeServer
dotnet run -c Release --project FastPortLoadValidation -- --profile staged
```

## 5. Verification

Commands run:

```bash
dotnet build FastPortCharp.sln
dotnet test FastPortCharp.sln --no-build
./FastPortLoadValidation/bin/Debug/net10.0/FastPortLoadValidation --profile staged --stage s5-random-10k --output artifacts/load-validation/dry-run --dry-run
```

Results:

- Build passed: 0 warnings, 0 errors.
- Tests passed: 71 passed, 0 failed.
- Dry-run produced the expected `s5-random-10k` LoadRunner command.

## 6. Notes

- Full staged validation is intentionally not part of default `dotnet test`.
- The first implementation assumes `FastPortSmokeServer` is already running.
- Generated load validation artifacts are ignored by git.
