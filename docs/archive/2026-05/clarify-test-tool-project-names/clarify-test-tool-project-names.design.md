# clarify-test-tool-project-names - Design Document

> Version: 1.0.0 | Date: 2026-05-05 | Status: Draft
> Level: Starter | Plan: docs/01-plan/features/clarify-test-tool-project-names.plan.md

---

## 1. Overview

`clarify-test-tool-project-names`는 엔진/runtime 프로젝트와 test/validation tooling 프로젝트의 이름을 명확히 분리한다.

이번 변경은 naming/reference cleanup이다. 네트워크 동작, load validation threshold, observed metrics schema, benchmark logic은 변경하지 않는다.

## 2. Final Rename Decisions

### 2.1 Rename In This Feature

| Current | New name | Applies to |
|---------|----------|------------|
| `FastPortLoadRunner` | `FastPortTestLoadRunner` | folder, `.csproj`, solution project name, namespace, docs/scripts |
| `FastPortLoadValidation` | `FastPortTestLoadValidation` | folder, `.csproj`, solution project name, namespace, docs/scripts |
| `FastPortSmokeServer` | `FastPortTestSmokeServer` | folder, `.csproj`, solution project name, namespace, appsettings section, docs/scripts |
| `LibCommonTest` | `FastPortTests` | folder, `.csproj`, solution project name, namespace, friend assembly names |

`LibCommonTest`는 이미 `Test`를 포함하지만 실제 테스트 범위가 `LibCommons`만이 아니다. 현재 테스트는 `LibNetworks`, load runner, load validation, smoke server까지 검증하므로 `FastPortTests`가 더 정확하다.

### 2.2 Do Not Rename In This Feature

| Project | Reason |
|---------|--------|
| `LibCommons` | core/shared library |
| `LibNetworks` | network engine library |
| `Protocols` | runtime protocol contract |
| `FastPortServer` | runtime server/template direction |
| `FastPortClient` | role needs a separate decision: runtime client, sample client, or test client |
| `LibTestTelemetry` | handled by `extract-telemetry-contracts-from-network-core` |

## 3. Naming Policy

### 3.1 Project Names

- Engine/core library: `Lib*` or domain name, without `Test`.
- Runtime executable/template: `FastPort*`, without `Test`.
- Shared test/validation library: `LibTest*`.
- Test/validation executable: `FastPortTest*`.
- Test assembly: `FastPortTests`.

### 3.2 Namespace Names

Project rename includes namespace rename for active source files.

| Current namespace | New namespace |
|-------------------|---------------|
| `FastPortLoadRunner` | `FastPortTestLoadRunner` |
| `FastPortLoadValidation` | `FastPortTestLoadValidation` |
| `FastPortSmokeServer` | `FastPortTestSmokeServer` |
| `FastPortSmokeServer.Sessions` | `FastPortTestSmokeServer.Sessions` |
| `LibCommonTest` | `FastPortTests` |

### 3.3 Type Names

Type names that include the old project name should be renamed with the project.

| Current type | New type |
|--------------|----------|
| `FastPortSmokeServer` | `FastPortTestSmokeServer` |
| `FastPortSmokeServerOptions` | `FastPortTestSmokeServerOptions` |
| `FastPortSmokeServerTelemetryOptions` | `FastPortTestSmokeServerTelemetryOptions` |
| `FastPortSmokeServerBackgroundService` | `FastPortTestSmokeServerBackgroundService` |
| `FastPortSmokeClientSession` | `FastPortTestSmokeClientSession` |
| `FastPortSmokeClientSessionFactory` | `FastPortTestSmokeClientSessionFactory` |
| `FastPortSmokeServerTests` | `FastPortTestSmokeServerTests` |
| `FastPortSmokeServerTestHost` | `FastPortTestSmokeServerTestHost` |

Types that are generic within the renamed namespace can keep their names, for example `LoadValidationOptions`, `LoadRunnerCommandBuilder`, `MetricsSnapshot`, and `LoadSession`.

## 4. Config Compatibility

### 4.1 Smoke Server Section

Primary config section changes from:

```text
FastPortSmokeServer
```

to:

```text
FastPortTestSmokeServer
```

Implementation must keep fallback support for the old section during this migration:

```text
FastPortTestSmokeServer -> FastPortSmokeServer -> defaults
```

Active `appsettings.json`, scripts, and docs should use `FastPortTestSmokeServer`.

### 4.2 Environment Variables

Primary environment variables become:

```text
FastPortTestSmokeServer__Host
FastPortTestSmokeServer__Port
```

`FastPortSmokeServer__Host` and `FastPortSmokeServer__Port` remain accepted through the old-section fallback.

### 4.3 Load Validation Runner Project

`LoadValidationOptions` default runner project changes from:

```text
FastPortLoadRunner
```

to:

```text
FastPortTestLoadRunner
```

The existing `--runner-project` option remains unchanged and can still override the runner project path/name.

## 5. File And Reference Changes

### 5.1 Folders And Project Files

| Current path | New path |
|--------------|----------|
| `FastPortLoadRunner/FastPortLoadRunner.csproj` | `FastPortTestLoadRunner/FastPortTestLoadRunner.csproj` |
| `FastPortLoadValidation/FastPortLoadValidation.csproj` | `FastPortTestLoadValidation/FastPortTestLoadValidation.csproj` |
| `FastPortSmokeServer/FastPortSmokeServer.csproj` | `FastPortTestSmokeServer/FastPortTestSmokeServer.csproj` |
| `LibCommonTest/LibCommonTest.csproj` | `FastPortTests/FastPortTests.csproj` |

The solution project GUIDs should be preserved when editing `FastPortCharp.sln`. This keeps the rename diff focused on names and paths.

### 5.2 Project References

References to update:

| File | Update |
|------|--------|
| `FastPortTestLoadValidation/FastPortTestLoadValidation.csproj` | project name/path only |
| `FastPortTestSmokeServer/FastPortTestSmokeServer.csproj` | project name/path only |
| `FastPortTests/FastPortTests.csproj` | references to renamed test tooling projects |
| `FastPortCharp.sln` | project names and paths |

### 5.3 Friend Assemblies

Because the test assembly changes from `LibCommonTest` to `FastPortTests`, update:

| File | Change |
|------|--------|
| `LibNetworks/Properties/AssemblyInfo.cs` | `InternalsVisibleTo("FastPortTests")` |
| `FastPortTestLoadRunner/Properties/AssemblyInfo.cs` | `InternalsVisibleTo("FastPortTests")` |

If `LibTestTelemetry` later needs internal test access, it should use `FastPortTests` from the start.

### 5.4 Active Docs And Scripts

Update active docs and scripts only:

- `README.md`
- `README.ko.md`
- `HANDOFF.md`
- `FastPortTestLoadRunner/README.md`
- `docs/loadrunner-os-limits.md`
- `docs/staged-load-validation-test-guide.md`
- `docs/load-validation-benchmark-results.md`
- `docs/cloud-server-runner-split-load-validation-runbook.md`
- `docs/azure-server-runner-split-load-validation-runbook.md`
- `docs/01-plan/features/*.md` active files only when they describe current next steps
- `docs/02-design/features/*.md` active files only when they describe current next steps
- `scripts/cloud/server-start.sh`
- `scripts/cloud/runner-10k.sh`
- `scripts/cloud/runner-smoke.sh`

Do not bulk rewrite:

- `docs/archive/**`
- `BenchmarkDotNet.Artifacts/**`
- existing benchmark result payloads under `artifacts/**`

## 6. Implementation Order

1. Confirm `extract-telemetry-contracts-from-network-core` is either completed or consciously paused.
2. Rename folders:
   - `FastPortLoadRunner` -> `FastPortTestLoadRunner`
   - `FastPortLoadValidation` -> `FastPortTestLoadValidation`
   - `FastPortSmokeServer` -> `FastPortTestSmokeServer`
   - `LibCommonTest` -> `FastPortTests`
3. Rename `.csproj` files inside each folder.
4. Update `FastPortCharp.sln` project names and paths while preserving GUIDs.
5. Update all project references.
6. Rename namespaces and type names in active source files.
7. Update `InternalsVisibleTo` attributes to `FastPortTests`.
8. Update `LoadValidationOptions` default runner project to `FastPortTestLoadRunner`.
9. Update smoke server config section handling:
   - read `FastPortTestSmokeServer` first
   - fallback to `FastPortSmokeServer`
10. Update `appsettings.json` section name to `FastPortTestSmokeServer`.
11. Update active docs and scripts.
12. Run build/tests.

## 7. Test Plan

### 7.1 Required Build/Test

```text
dotnet build FastPortCharp.sln -c Release
dotnet test FastPortCharp.sln -c Release --no-build
```

### 7.2 Focused Verification

Run a dry-run validation command so the generated runner command confirms the new default project name:

```text
dotnet run -c Release --project FastPortTestLoadValidation -- --profile smoke --dry-run --output artifacts/load-validation/rename-dry-run
```

Expected dry-run output should include:

```text
dotnet run -c Release --project FastPortTestLoadRunner --
```

### 7.3 Config Fallback Verification

The smoke server should accept both config section names:

- `FastPortTestSmokeServer`
- `FastPortSmokeServer`

At minimum, keep unit coverage for option defaults and add or update coverage for fallback behavior if the config selection is factored into a testable helper.

## 8. Risks And Mitigations

| Risk | Mitigation |
|------|------------|
| Rename diff becomes too large to review | Restrict to project/folder/namespace/docs/scripts; no behavior changes |
| `InternalsVisibleTo` breaks internal tests | Update friend assembly references in the same rename step |
| Smoke server config rename breaks cloud scripts | Update scripts to use `FastPortTestSmokeServer__*` and keep old-section fallback |
| Load validation still invokes old runner project | Update `LoadValidationOptions` default and tests that assert command output |
| Active docs drift from commands | Update active docs and runbooks in the same feature |
| Historical docs become inaccurate if rewritten | Do not rewrite archive/history folders |

## 9. Non-Goals Confirmed

- No network engine behavior change.
- No load pacing or threshold tuning.
- No telemetry schema change.
- No benchmark data recalculation.
- No cloud resource creation.
- No archive document rewrite.

## 10. Acceptance Criteria

- [ ] Solution contains `FastPortTestLoadRunner`, `FastPortTestLoadValidation`, `FastPortTestSmokeServer`, and `FastPortTests`.
- [ ] Old project folders no longer exist in active source.
- [ ] Namespaces match new project names.
- [ ] Active docs/scripts use new project names.
- [ ] Smoke server supports new config section and old-section fallback.
- [ ] `FastPortTestLoadValidation` default dry-run command uses `FastPortTestLoadRunner`.
- [ ] Release build passes.
- [ ] Release tests pass.

## 11. Dependency On Telemetry Extraction

This feature can technically be implemented before or after `extract-telemetry-contracts-from-network-core`, but the preferred order is:

1. Finish `extract-telemetry-contracts-from-network-core`.
2. Implement `clarify-test-tool-project-names`.

Reason: telemetry contract extraction changes project references, and this rename changes project paths/names. Keeping them separate avoids mixing architectural movement with mechanical rename churn.

## 12. Next Phase

Recommended next command:

```text
$pdca do clarify-test-tool-project-names
```
