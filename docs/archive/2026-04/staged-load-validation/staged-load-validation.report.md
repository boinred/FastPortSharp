# Completion Report: staged-load-validation

> Date: 2026-04-28 | Status: Completed | Match Rate: 100%

---

## 1. Summary

`staged-load-validation` added an opt-in staged load validation tool for FastPortSharp. The new `FastPortLoadValidation` console project can run predefined smoke/staged profiles through `FastPortLoadRunner`, consume observed JSONL metrics, evaluate pass/fail thresholds, and write validation summaries.

Completion rate: 100%

Full 1,000 / 3,000 / 5,000 / 10,000 session execution was not run in this phase. That is intentional: the feature implements the repeatable tool and parser/evaluator path, while actual high-load runs remain an opt-in performance workflow.

## 2. Related Documents

- Plan: `docs/01-plan/features/staged-load-validation.plan.md`
- Design: `docs/02-design/features/staged-load-validation.design.md`
- Do: `docs/02-design/features/staged-load-validation.do.md`
- Analysis: `docs/03-analysis/staged-load-validation.analysis.md`

## 3. Completed Items

- Added `FastPortLoadValidation` console project.
- Added `FastPortLoadValidation` to `FastPortCharp.sln`.
- Added `artifacts/load-validation/` to `.gitignore`.
- Added smoke profile:
  - `smoke-fixed-10`
  - `smoke-random-25`
- Added staged profile:
  - `s1-fixed-1k`
  - `s2-random-1k`
  - `s3-random-3k`
  - `s4-random-5k`
  - `s5-random-10k`
- Added LoadRunner command generation.
- Added process execution with stdout/stderr log capture.
- Added observed JSONL parser for `ObservedMetricsSnapshot`.
- Added evaluator for session ratio, socket error rate, disconnect ratio, received packets, TPS, and sample count.
- Added `manifest.json`, `summary.json`, and `summary.md` writers.
- Added unit tests for options, profiles, command generation, JSONL reader, evaluator, and summary writer.

## 4. Quality Metrics

| Metric | Result |
|--------|--------|
| Match rate | 100% |
| Design items implemented | 16 / 16 |
| Missing items | 0 |
| Deviations | 0 |
| Build command | `dotnet build FastPortCharp.sln` |
| Build result | 0 warnings, 0 errors |
| Test command | `dotnet test FastPortCharp.sln --no-build` |
| Test result | 71 passed, 0 failed |
| Dry-run command | `./FastPortLoadValidation/bin/Debug/net10.0/FastPortLoadValidation --profile staged --stage s5-random-10k --output artifacts/load-validation/dry-run --dry-run` |
| Dry-run result | Generated expected `s5-random-10k` LoadRunner command |

## 5. Manual Usage

Smoke validation:

```bash
dotnet run -c Release --project FastPortSmokeServer
dotnet run -c Release --project FastPortLoadValidation -- --profile smoke
```

Full staged validation:

```bash
dotnet run -c Release --project FastPortSmokeServer
dotnet run -c Release --project FastPortLoadValidation -- --profile staged
```

Single 10,000 session dry-run:

```bash
dotnet run -c Release --project FastPortLoadValidation -- --profile staged --stage s5-random-10k --dry-run
```

## 6. Remaining Limits

- The full staged profile has not been executed on a performance-ready machine.
- `FastPortSmokeServer` startup is still a manual prerequisite.
- OS limits such as file descriptors/socket backlog are documented as operational concerns, not auto-tuned.
- RTT thresholds are recorded for trend comparison but not used for hard pass/fail yet.

## 7. Lessons Learned

### Keep

- Keep high-load validation opt-in, separate from default `dotnet test`.
- Keep generated load validation artifacts ignored by git.
- Keep validation consuming observed DTO JSONL instead of depending on LoadRunner internals.

### Problem

- Running via `dotnet run` for dry-run verification in this environment was slower/noisier than executing the built validation binary directly.

### Try

- Add managed smoke server startup later if the two-process manual workflow becomes repetitive.
- Add baseline result capture after running the staged profile on a machine with confirmed OS limits.
- Consider latency threshold rules only after collecting baseline trend data.

## 8. Next Steps

1. Archive with `$pdca archive staged-load-validation` after review.
2. Commit the implementation, report, and existing `loadrunner-observed-jsonl` archive movement together.
3. Run the opt-in smoke profile against `FastPortSmokeServer`.
4. Prepare performance environment prerequisites before running the full staged profile.
