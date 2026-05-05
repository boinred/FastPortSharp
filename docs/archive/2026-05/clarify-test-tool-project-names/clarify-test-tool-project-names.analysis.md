# Gap Analysis: clarify-test-tool-project-names

> Date: 2026-05-05 | Design: docs/02-design/features/clarify-test-tool-project-names.design.md

---

## Match Rate: 96%

## Summary

`clarify-test-tool-project-names` implementation matches the design. The test/validation projects were renamed, namespaces and project references were updated, active docs/scripts now use the new names, and the smoke server config migration keeps the legacy section as a fallback.

There are no missing implementation items. The only deviation is verification-related: the design's `dotnet run ... --dry-run` command hung in this local environment after `CSSM_ModuleLoad()`, so the equivalent built Release executable was used to verify the same dry-run output.

## Implemented Items

- [x] `FastPortLoadRunner` renamed to `FastPortTestLoadRunner`.
  - Folder: `FastPortTestLoadRunner/`
  - Project file: `FastPortTestLoadRunner/FastPortTestLoadRunner.csproj`
  - Namespace: `FastPortTestLoadRunner`
  - Solution project entry: `FastPortTestLoadRunner`

- [x] `FastPortLoadValidation` renamed to `FastPortTestLoadValidation`.
  - Folder: `FastPortTestLoadValidation/`
  - Project file: `FastPortTestLoadValidation/FastPortTestLoadValidation.csproj`
  - Namespace: `FastPortTestLoadValidation`
  - Solution project entry: `FastPortTestLoadValidation`

- [x] `FastPortSmokeServer` renamed to `FastPortTestSmokeServer`.
  - Folder: `FastPortTestSmokeServer/`
  - Project file: `FastPortTestSmokeServer/FastPortTestSmokeServer.csproj`
  - Namespace: `FastPortTestSmokeServer`
  - Main type renamed to `FastPortTestSmokeServer`
  - Options/background/session types renamed with the `FastPortTestSmoke*` prefix.

- [x] `LibCommonTest` renamed to `FastPortTests`.
  - Folder: `FastPortTests/`
  - Project file: `FastPortTests/FastPortTests.csproj`
  - Namespace: `FastPortTests`
  - Solution project entry: `FastPortTests`

- [x] Solution references updated while preserving project GUIDs.
  - `FastPortCharp.sln` contains `FastPortTests`, `FastPortTestLoadRunner`, `FastPortTestSmokeServer`, and `FastPortTestLoadValidation`.

- [x] Project references updated.
  - `FastPortTests/FastPortTests.csproj` references the renamed test tooling projects.

- [x] Friend assembly references updated.
  - `LibNetworks/Properties/AssemblyInfo.cs` uses `InternalsVisibleTo("FastPortTests")`.
  - `FastPortTestLoadRunner/Properties/AssemblyInfo.cs` uses `InternalsVisibleTo("FastPortTests")`.
  - `FastPortTestLoadValidation/Properties/AssemblyInfo.cs` also uses `InternalsVisibleTo("FastPortTests")`; this is an implementation detail already present in the renamed project and is consistent with the final test assembly name.

- [x] `LoadValidationOptions` default runner project updated.
  - Default runner is now `FastPortTestLoadRunner`.
  - Existing `--runner-project` override remains unchanged.

- [x] Smoke server config migration implemented.
  - Primary section: `FastPortTestSmokeServer`
  - Legacy fallback section: `FastPortSmokeServer`
  - `FastPortTestSmokeServerConfiguration.GetServerSection(...)` implements the selection.
  - Unit coverage verifies new-section priority and legacy fallback.

- [x] Active docs and scripts updated.
  - `README.md`
  - `README.ko.md`
  - `HANDOFF.md`
  - `FastPortTestLoadRunner/README.md`
  - `docs/loadrunner-os-limits.md`
  - `docs/staged-load-validation-test-guide.md`
  - `docs/load-validation-benchmark-results.md`
  - cloud runbooks and `scripts/cloud/*.sh`

- [x] Historical docs were not bulk rewritten.
  - `docs/archive/**` remains historical.
  - Legacy names remain only where intentionally referenced in the current design/analysis or config fallback tests.

- [x] Old active project folders are gone.
  - `FastPortLoadRunner/`
  - `FastPortLoadValidation/`
  - `FastPortSmokeServer/`
  - `LibCommonTest/`

## Missing Items

None.

## Changed Items

- [x] Verification command deviation.
  - Design requested:

    ```text
    dotnet run -c Release --project FastPortTestLoadValidation -- --profile smoke --dry-run --output artifacts/load-validation/rename-dry-run
    ```

  - In this local environment, `dotnet run` printed `CSSM_ModuleLoad()` and waited without producing the dry-run output.
  - Equivalent Release binary verification passed:

    ```text
    ./FastPortTestLoadValidation/bin/Release/net10.0/FastPortTestLoadValidation --profile smoke --dry-run --output artifacts/load-validation/rename-dry-run
    ```

  - Output confirmed the new default runner:

    ```text
    dotnet run -c Release --project FastPortTestLoadRunner --
    ```

This is a verification-path deviation, not an implementation gap.

## Verification

- [x] `git diff --check`
- [x] `jq empty docs/.pdca-status.json`
- [x] Old active project folders removed:

  ```text
  test ! -d FastPortLoadRunner && test ! -d FastPortLoadValidation && test ! -d FastPortSmokeServer && test ! -d LibCommonTest
  ```

- [x] Solution contains new project entries:
  - `FastPortTests`
  - `FastPortTestLoadRunner`
  - `FastPortTestSmokeServer`
  - `FastPortTestLoadValidation`

- [x] `dotnet build FastPortCharp.sln -c Release`
  - Warnings: 0
  - Errors: 0

- [x] `dotnet test FastPortCharp.sln -c Release --no-build`
  - Passed: 113
  - Failed: 0
  - Skipped: 0

- [x] Release binary dry-run output contains `--project FastPortTestLoadRunner`.

## Residual Risk

- Approved local command prefixes and any external scripts outside this repository may still reference old binary paths. This is outside the repository implementation surface.
- Historical docs intentionally keep old names when they describe past work.

## Recommendations

1. Proceed to `$pdca report clarify-test-tool-project-names`.
2. Do not run iterate for this feature unless the user wants the `dotnet run` dry-run path investigated separately.
3. Keep `extract-telemetry-contracts-from-network-core` as the next architecture cleanup item after reporting or archiving this rename feature.

## Next Steps

- [x] Match rate is above 90%.
- [ ] Run `$pdca report clarify-test-tool-project-names`.
