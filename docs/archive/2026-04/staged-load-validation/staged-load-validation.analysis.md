# Gap Analysis: staged-load-validation

> Date: 2026-04-28 | Design: docs/02-design/features/staged-load-validation.design.md

---

## Match Rate: 100%

16 of 16 design items are implemented.

## Summary

The implementation matches the design. `FastPortLoadValidation` was added as an opt-in console project, keeps high-load execution outside default tests, generates `FastPortLoadRunner` commands for smoke/staged profiles, parses observed JSONL client metrics, evaluates stage thresholds, and writes summaries.

`dotnet test FastPortCharp.sln --no-build` passed with 71 tests, 0 failures.

## Implemented Items

- [x] Added `FastPortLoadValidation` console project.
- [x] Added the project to `FastPortCharp.sln`.
- [x] Kept validation tooling dependent on `LibNetworks` and independent from `FastPortLoadRunner` internals.
- [x] Implemented CLI option parsing for profile, host, port, output, stage, runner project, configuration, dry-run, and continue-on-failure.
- [x] Implemented smoke profile with `smoke-fixed-10` and `smoke-random-25`.
- [x] Implemented staged profile with `s1-fixed-1k`, `s2-random-1k`, `s3-random-3k`, `s4-random-5k`, and `s5-random-10k`.
- [x] Implemented stage/input/output DTOs and threshold defaults.
- [x] Implemented LoadRunner command generation using existing LoadRunner CLI options.
- [x] Implemented process execution with stdout/stderr log capture.
- [x] Implemented observed JSONL reader using `ObservedMetricsJson.SerializerOptions`.
- [x] Implemented stage evaluator with JSON sample, peak session ratio, socket error, disconnect, received packet, and TPS checks.
- [x] Implemented manifest and summary writers for JSON and Markdown.
- [x] Added `artifacts/load-validation/` to `.gitignore`.
- [x] Added `InternalsVisibleTo("LibCommonTest")`.
- [x] Added unit tests for options, profiles, command generation, JSONL reader, evaluator, and summary writer.
- [x] Verified build/test/dry-run behavior.

## Missing Items

None.

## Changed Items (Deviations from Design)

None.

## Evidence

| Design item | Implementation evidence | Status |
|-------------|--------------------------|--------|
| New opt-in project | `FastPortLoadValidation/FastPortLoadValidation.csproj` and solution entry exist. | Match |
| Profile selection | `LoadValidationOptions` and `LoadValidationProfiles` implement `smoke` and `staged`. | Match |
| Stage matrix | `LoadValidationProfiles` defines 2 smoke stages and 5 staged validation stages. | Match |
| Existing server prerequisite | `Program` only runs LoadRunner processes; it does not start `FastPortSmokeServer`. | Match |
| LoadRunner process command | `LoadRunnerCommandBuilder` emits `dotnet run -c ... --project FastPortLoadRunner -- ...`. | Match |
| Observed JSONL parser | `JsonlObservedMetricsReader` deserializes `ObservedMetricsSnapshot` and requires `clientObserved`. | Match |
| Evaluation rules | `LoadValidationEvaluator` checks sample count, peak session ratio, socket error rate, disconnect ratio, received packets, and TPS. | Match |
| Summary output | `LoadValidationSummaryWriter` writes `manifest.json`, `summary.json`, and `summary.md`. | Match |
| Stdout/stderr logs | `ProcessRunner` captures stage stdout/stderr files. | Match |
| Generated artifact ignore | `.gitignore` includes `artifacts/load-validation/`. | Match |
| Unit tests | `LibCommonTest/FastPortLoadValidationTests.cs` covers options, profiles, commands, reader, evaluator, and summary writer. | Match |
| Verification | `dotnet test FastPortCharp.sln --no-build` passed: 71 passed, 0 failed. | Match |

## Validation Notes

Full 1,000 / 3,000 / 5,000 / 10,000 session validation was not executed in this phase. That matches the design: the full staged profile is an opt-in performance workflow and is not part of default `dotnet test`.

Dry-run command generation was verified with:

```bash
./FastPortLoadValidation/bin/Debug/net10.0/FastPortLoadValidation --profile staged --stage s5-random-10k --output artifacts/load-validation/dry-run --dry-run
```

It produced:

```bash
dotnet run -c Release --project FastPortLoadRunner -- --host 127.0.0.1 --port 6628 --sessions 10000 --payload random:4096-16384 --rate 1 --ramp-up 120s --duration 5m --metrics-interval 1s --output artifacts/load-validation/dry-run/s5-random-10k.metrics.jsonl
```

## Recommendations

1. Proceed to `$pdca report staged-load-validation`.
2. Keep full staged load execution manual until a dedicated performance environment and OS limits are documented.
3. Consider a later feature for managed `FastPortSmokeServer` startup if manual two-process operation becomes inconvenient.

## Next Steps

- [x] Proceed to report phase because match rate is above 90%.
