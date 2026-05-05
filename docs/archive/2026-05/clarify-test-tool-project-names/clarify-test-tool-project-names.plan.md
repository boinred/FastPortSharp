# clarify-test-tool-project-names - Plan Document

> Version: 1.0.0 | Date: 2026-05-05 | Status: Draft
> Level: Starter

---

## 1. Overview

### 1.1 Purpose

`clarify-test-tool-project-names`는 엔진/runtime 프로젝트와 테스트/검증용 프로젝트의 이름을 분리해 역할을 더 명확하게 만드는 feature다.

현재 `FastPortLoadRunner`, `FastPortLoadValidation`, `FastPortSmokeServer`는 load test와 validation harness 역할을 하지만 이름만 보면 runtime product/server 구성요소처럼 보일 수 있다. 앞으로 engine/core와 test tooling을 분리하는 흐름에 맞춰 test/validation project에는 `Test`를 명시하는 naming policy를 확정한다.

### 1.2 Background

`extract-telemetry-contracts-from-network-core` 설계에서 shared test telemetry contract 이름을 `LibTestTelemetry`로 정했다. 같은 기준을 실행 프로젝트에도 적용하면 다음 구분이 가능해진다.

| Category | Naming style | Examples |
|----------|--------------|----------|
| Engine/core library | `Lib*`, domain name | `LibCommons`, `LibNetworks`, `Protocols` |
| Runtime executable/template | product/runtime name | `FastPortServer`, `FastPortClient` |
| Test/validation shared library | `LibTest*` | `LibTestTelemetry` |
| Test/validation executable | `FastPortTest*` | `FastPortTestLoadRunner` |

## 2. Goals

### 2.1 Primary Goals

- [ ] 테스트/검증용 프로젝트와 엔진/runtime 프로젝트의 naming policy를 확정한다.
- [ ] rename 대상과 보류 대상을 구분한다.
- [ ] `FastPortLoadRunner`, `FastPortLoadValidation`, `FastPortSmokeServer`의 후보 이름을 결정한다.
- [ ] `.sln`, `.csproj`, namespace, folder, scripts, README, HANDOFF 변경 범위를 정리한다.
- [ ] 기존 archived docs와 benchmark artifact는 historical record로 보존할지 결정한다.
- [ ] implementation 순서와 검증 기준을 정의한다.

### 2.2 Non-Goals

- 네트워크 engine 동작을 바꾸지 않는다.
- load validation threshold나 benchmark 기준을 바꾸지 않는다.
- observed metrics JSONL schema를 바꾸지 않는다.
- cloud resource를 생성하거나 배포 방식을 바꾸지 않는다.
- archived PDCA 문서와 과거 benchmark artifact를 일괄 rewrite하지 않는다.
- `extract-telemetry-contracts-from-network-core`의 telemetry contract 이동과 같은 커밋에 섞지 않는다.

## 3. Scope

### 3.1 In Scope

Primary rename candidates:

| Current project | Candidate name | Reason |
|-----------------|----------------|--------|
| `FastPortLoadRunner` | `FastPortTestLoadRunner` | TCP load test generator |
| `FastPortLoadValidation` | `FastPortTestLoadValidation` | staged load validation runner and artifact evaluator |
| `FastPortSmokeServer` | `FastPortTestSmokeServer` | smoke/load validation server harness |

Review candidates:

| Current project | Candidate name | Default decision |
|-----------------|----------------|------------------|
| `LibCommonTest` | `FastPortTests` or `FastPortTestSuite` | Review in design; it already contains `Test`, but the current name is too narrow |
| `FastPortClient` | TBD | Do not rename until its role is clarified as runtime client, sample client, or test client |

Files and references to review:

- `FastPortCharp.sln`
- `FastPortTestLoadRunner/FastPortTestLoadRunner.csproj`
- `FastPortTestLoadValidation/FastPortTestLoadValidation.csproj`
- `FastPortTestSmokeServer/FastPortTestSmokeServer.csproj`
- `FastPortTests/FastPortTests.csproj`
- `LibNetworks/Properties/AssemblyInfo.cs`
- `FastPortTestLoadRunner/Properties/AssemblyInfo.cs`
- `README.md`
- `README.ko.md`
- `HANDOFF.md`
- `docs/*.md` active documents
- `scripts/cloud/*.sh`
- `FastPortTestLoadRunner/README.md`
- appsettings/config sections and environment variable prefixes for smoke server

### 3.2 Out of Scope

- `LibCommons`, `LibNetworks`, `Protocols` rename.
- `FastPortServer` rename.
- `FastPortClient` rename unless design explicitly proves it is test-only.
- `docs/archive/**` bulk rewrite.
- `BenchmarkDotNet.Artifacts/**` rewrite.
- Generated `bin/` or `obj/` output cleanup beyond normal build outputs.

## 4. Proposed Naming Policy

### 4.1 Project Names

- Shared test libraries use `LibTest*`.
- Test executable tools use `FastPortTest*`.
- Runtime executable projects keep `FastPort*` without `Test`.
- Unit/integration test assemblies must include `Test` or `Tests`.

### 4.2 Namespace Names

Project rename should normally include namespace rename for active source files.

Examples:

| Current namespace | Candidate namespace |
|-------------------|---------------------|
| `FastPortLoadRunner` | `FastPortTestLoadRunner` |
| `FastPortLoadValidation` | `FastPortTestLoadValidation` |
| `FastPortSmokeServer` | `FastPortTestSmokeServer` |

### 4.3 Config Compatibility

For `FastPortSmokeServer` rename, design must decide whether to:

- migrate config section from `FastPortSmokeServer` to `FastPortTestSmokeServer`, or
- keep old config section temporarily for compatibility.

Default preference: support the old section as fallback for one migration pass, while active docs and scripts use the new name.

## 5. Success Criteria

- [ ] Design document contains final rename list and deferred list.
- [ ] Implementation updates solution/project/folder/namespace references consistently.
- [ ] Active docs and scripts use the new project names.
- [ ] `InternalsVisibleTo` references are updated if `LibCommonTest` is renamed.
- [ ] Smoke server config section compatibility is explicitly handled.
- [ ] `dotnet build FastPortCharp.sln -c Release` passes.
- [ ] `dotnet test FastPortCharp.sln -c Release --no-build` passes.
- [ ] Load validation dry-run command still prints valid runner command.

## 6. Schedule

| Phase | Target Date | Status |
|-------|------------|--------|
| Plan | 2026-05-05 | In Progress |
| Design | TBD | Pending |
| Implementation | TBD | Pending |
| Check | TBD | Pending |
| Report | TBD | Pending |

## 7. Risks & Mitigations

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| Rename touches many references and hides behavior changes | High | Medium | Limit feature to naming/reference changes only |
| Cloud scripts still call old project names | High | High | Update `scripts/cloud/*.sh` and active runbooks |
| `InternalsVisibleTo` breaks tests if test assembly is renamed | High | Medium | Update friend assembly names in the same implementation step |
| Config section rename breaks existing environment variables | Medium | Medium | Keep fallback support for old smoke server section during migration |
| Historical docs become misleading if rewritten | Medium | Low | Do not bulk rewrite `docs/archive/**`; active docs only |
| Existing approved local command prefixes reference old binary paths | Low | Medium | Document new commands; do not rely on old approval rules for implementation correctness |

## 8. Architecture Considerations

Recommended dependency and role view after naming cleanup:

```text
LibCommons / LibNetworks / Protocols
  ^
  |
FastPortServer / FastPortClient

LibNetworks
  ^
  |
LibTestTelemetry
  ^
  |
FastPortTestSmokeServer / FastPortTestLoadRunner / FastPortTestLoadValidation
```

`LibTestTelemetry` extraction should ideally land before this rename implementation. That keeps telemetry contract movement separate from project identity churn.

## 9. References

- `docs/02-design/features/extract-telemetry-contracts-from-network-core.design.md`
- `FastPortCharp.sln`
- `README.md`
- `README.ko.md`
- `HANDOFF.md`
- `scripts/cloud/server-start.sh`
- `scripts/cloud/runner-10k.sh`
- `scripts/cloud/runner-smoke.sh`
- `docs/staged-load-validation-test-guide.md`
- `docs/cloud-server-runner-split-load-validation-runbook.md`
- `docs/azure-server-runner-split-load-validation-runbook.md`

## 10. Next Phase

Recommended next command:

```text
$pdca design clarify-test-tool-project-names
```
