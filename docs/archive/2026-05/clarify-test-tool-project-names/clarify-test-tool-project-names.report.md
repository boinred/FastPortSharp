# Completion Report: clarify-test-tool-project-names

> Date: 2026-05-05 | Status: Completed | Match Rate: 96%

---

## 1. Summary

`clarify-test-tool-project-names` completed the test/tooling project rename cleanup. The repository now distinguishes runtime/engine projects from test and validation tooling through explicit `Test` naming.

The feature was intentionally scoped to naming, references, docs, and compatibility wiring. It did not change network engine behavior, load pacing, validation thresholds, observed metrics schema, or benchmark data.

## 2. Related Documents

- Plan: `docs/01-plan/features/clarify-test-tool-project-names.plan.md`
- Design: `docs/02-design/features/clarify-test-tool-project-names.design.md`
- Analysis: `docs/03-analysis/clarify-test-tool-project-names.analysis.md`

## 3. Completed Items

### 3.1 Project Renames

- [x] `FastPortLoadRunner` -> `FastPortTestLoadRunner`
- [x] `FastPortLoadValidation` -> `FastPortTestLoadValidation`
- [x] `FastPortSmokeServer` -> `FastPortTestSmokeServer`
- [x] `LibCommonTest` -> `FastPortTests`

The rename was applied to folders, project files, solution entries, namespaces, relevant type names, and active docs/scripts.

### 3.2 Solution And References

- [x] `FastPortCharp.sln` contains the new project names and paths.
- [x] Project GUIDs were preserved.
- [x] `FastPortTests/FastPortTests.csproj` references the renamed test tooling projects.
- [x] `InternalsVisibleTo` references now target `FastPortTests`.

### 3.3 Smoke Server Config Compatibility

- [x] New primary config section: `FastPortTestSmokeServer`
- [x] Legacy fallback config section: `FastPortSmokeServer`
- [x] New env vars are used in scripts:
  - `FastPortTestSmokeServer__Host`
  - `FastPortTestSmokeServer__Port`
- [x] Legacy section remains accepted through `FastPortTestSmokeServerConfiguration.GetServerSection(...)`.
- [x] Unit tests verify new-section priority and legacy fallback.

### 3.4 Load Validation Default Runner

- [x] Default runner project changed to `FastPortTestLoadRunner`.
- [x] Existing `--runner-project` override remains unchanged.
- [x] Release dry-run output confirms generated commands use `--project FastPortTestLoadRunner`.

### 3.5 Docs And Scripts

- [x] Active docs and scripts were updated to the new names.
- [x] Historical docs under `docs/archive/**` were intentionally left unchanged.
- [x] Benchmark artifact payloads were not rewritten.

## 4. Quality Metrics

| Metric | Result |
|--------|--------|
| Match rate | 96% |
| Missing implementation items | 0 |
| Release build | Passed |
| Release tests | Passed |
| Test count | 113 passed, 0 failed, 0 skipped |
| `git diff --check` | Passed |
| `docs/.pdca-status.json` JSON validation | Passed |

## 5. Verification

Commands verified during implementation/check:

```text
dotnet build FastPortCharp.sln -c Release
dotnet test FastPortCharp.sln -c Release --no-build
./FastPortTestLoadValidation/bin/Release/net10.0/FastPortTestLoadValidation --profile smoke --dry-run --output artifacts/load-validation/rename-dry-run
git diff --check
jq empty docs/.pdca-status.json
```

Build result:

```text
Warnings: 0
Errors: 0
```

Test result:

```text
Passed: 113
Failed: 0
Skipped: 0
```

Dry-run output confirmed:

```text
dotnet run -c Release --project FastPortTestLoadRunner --
```

## 6. Deviations

The design specified a `dotnet run ... --dry-run` verification command. In this local environment, that command printed `CSSM_ModuleLoad()` and waited without producing dry-run output. The same dry-run was verified through the built Release executable instead.

This is a verification-path deviation only. It is not an implementation gap.

## 7. Lessons Learned

### Keep

- Keep project naming explicit about runtime versus test/validation responsibilities.
- Keep legacy config fallback for migration-sensitive executable renames.
- Keep historical archive docs unchanged to preserve past context.

### Problem

- Mechanical rename can accidentally rewrite PDCA design/plan tables from old->new into new->new. Those docs needed manual restoration.
- Local `dotnet run` can behave differently from direct Release executable execution in this environment.

### Try

- For future large rename work, treat docs that describe historical or design-time old names as protected ranges.
- Prefer direct Release binary verification when the goal is checking CLI output after a successful build.

## 8. Residual Risks

- External scripts outside this repository may still reference old project paths.
- Previously approved local command prefixes may still point to old binary paths.
- `FastPortClient` role remains undecided and should be handled as a separate feature if needed.

## 9. Next Steps

- [ ] Archive this feature:

  ```text
  $pdca archive clarify-test-tool-project-names
  ```

- [ ] Resume architecture cleanup:

  ```text
  $pdca do extract-telemetry-contracts-from-network-core
  ```
