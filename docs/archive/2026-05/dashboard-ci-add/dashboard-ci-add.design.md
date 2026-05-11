# dashboard-ci-add Design

> **Project**: FastPortSharp
> **Author**: boinred
> **Date**: 2026-05-11
> **Status**: Draft
> **Plan**: `docs/01-plan/features/dashboard-ci-add.plan.md`

---

## Context Anchor

| Key | Value |
|-----|-------|
| **WHY** | Dashboard 회귀를 push/PR 시점에 자동 감지하여 안정성 확보. |
| **WHO** | boinred + 미래 contributor. |
| **RISK** | (R-1) MAUI workload install 시간 / (R-2) path filter 누락 / (R-3) Catalyst-Windows OS 분기 |
| **SUCCESS** | dashboard.yml 신규 + 2-OS pass + path filter 정확 + build.yml 영향 0 + 단일 commit |
| **SCOPE** | `.github/workflows/dashboard.yml` only |

---

## 1. Overview

`.github/workflows/dashboard.yml` 신규 workflow로 Dashboard sln 전용 CI 추가. macOS + Windows matrix, `dotnet workload install maui` + restore + build + test. Path filter로 평소 trigger 차단.

---

## 2. Architecture Decision

### 2.1 Options Compared

| Option | Caching | CI 시간 | 선택 |
|---|---|---|---|
| **A — Minimal (선택)** | 없음 (NuGet/MAUI workload install 매번) | ~15분 | ✅ |
| B — NuGet + workload caching (`actions/cache@v4`) | NuGet 폴더 + MAUI workload cache | ~7분 | ❌ (현재 trigger 빈도 낮음, complexity ↑) |
| C — Self-hosted runner | macOS/Windows self-hosted | ~5분 | ❌ (overengineering) |

### 2.2 Selected: Option A — Minimal

**선택 근거**:
- Path filter로 trigger 빈도 낮음 → caching 효과 미미
- build.yml과 동일 패턴 (caching 없음) → 일관성
- 향후 trigger 빈도 ↑ 시 별도 cycle (`dashboard-ci-cache`)로 추가 가능

---

## 3. Detailed Design

### 3.1 dashboard.yml 전체 구조

```yaml
name: dashboard

# Builds and tests FastPortSharp.Dashboard.sln across macOS + Windows.
# Companion to build.yml (which builds FastPortSharp.sln). Triggered only
# when Dashboard-related files change to keep main CI unaffected.

on:
  push:
    branches:
      - builds.release
    paths:
      - 'FastPortDashboard.Maui/**'
      - 'FastPortDashboard.Core/**'
      - 'tests-projects/FastPortDashboardTests/**'
      - 'tests-projects/LibTestTelemetry/**'
      - 'FastPortSharp.Dashboard.sln'
      - '.github/workflows/dashboard.yml'
  pull_request:
    branches:
      - builds.release
    paths:
      - 'FastPortDashboard.Maui/**'
      - 'FastPortDashboard.Core/**'
      - 'tests-projects/FastPortDashboardTests/**'
      - 'tests-projects/LibTestTelemetry/**'
      - 'FastPortSharp.Dashboard.sln'
      - '.github/workflows/dashboard.yml'
  workflow_dispatch:

permissions:
  contents: read

jobs:
  build-test:
    name: ${{ matrix.os }}
    strategy:
      fail-fast: false
      matrix:
        os: [macos-latest, windows-latest]

    runs-on: ${{ matrix.os }}

    steps:
      - name: Force LF line endings on Windows
        if: runner.os == 'Windows'
        shell: bash
        run: |
          git config --global core.autocrlf false
          git config --global core.eol lf

      - name: Checkout
        uses: actions/checkout@v4
        with:
          fetch-depth: 1

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: dotnet --info
        shell: bash
        run: dotnet --info

      - name: Install MAUI workload
        shell: bash
        run: dotnet workload install maui

      - name: Restore
        shell: bash
        run: dotnet restore FastPortSharp.Dashboard.sln

      - name: Build (Release)
        shell: bash
        run: dotnet build FastPortSharp.Dashboard.sln -c Release --no-restore

      - name: Test (Release)
        shell: bash
        run: dotnet test FastPortSharp.Dashboard.sln -c Release --no-build --logger "console;verbosity=normal"
```

### 3.2 build.yml과의 비교

| 항목 | build.yml | dashboard.yml |
|---|---|---|
| Trigger branch | builds.release | builds.release (동일) |
| Path filter | 없음 (모든 변경) | 6 paths (Dashboard 영역만) |
| Matrix | ubuntu + macos + windows (3-OS) | macos + windows (2-OS) |
| MAUI workload install | 없음 | **있음** (필요) |
| Target sln | FastPortSharp.sln | **FastPortSharp.Dashboard.sln** |
| 들여쓰기 | 2 spaces | 2 spaces (동일) |
| Action versions | checkout@v4, setup-dotnet@v4 | 동일 |

### 3.3 Path Filter 6 entries 근거

| Entry | 이유 |
|---|---|
| `FastPortDashboard.Maui/**` | Maui app source + csproj + XAML |
| `FastPortDashboard.Core/**` | ViewModel + Adapter lib |
| `tests-projects/FastPortDashboardTests/**` | Test source + csproj |
| `tests-projects/LibTestTelemetry/**` | Data contract dependency |
| `FastPortSharp.Dashboard.sln` | sln 자체 변경 (project add/remove) |
| `.github/workflows/dashboard.yml` | self-modification 시 self-test |

→ 누락 0. `FastPortDashboard.Maui.csproj` 변경은 `FastPortDashboard.Maui/**` glob에 포함.

### 3.4 File-Level Changes

| 파일 | 작업 | 예상 라인 |
|---|---|---|
| `.github/workflows/dashboard.yml` | new | ~70 |

---

## 4. Risks and Mitigation

| Risk | Mitigation |
|---|---|
| (R-1) MAUI workload install 실패 | Step 분리 (workload install → restore), 실패 시 명확한 step 식별 가능 |
| (R-2) Path filter 누락 | 6 entries 명시, build.yml과 비교 가능, dashboard.yml self-reference 포함 |
| (R-3) Catalyst-Windows OS 분기 | csproj `<TargetFrameworks Condition>` 이미 처리 (Foundation cycle), workflow는 OS만 분리 |
| (R-4) MAUI workload install 매번 (caching 없음) | Path filter로 trigger 빈도 ↓ → 충분. 향후 별도 cycle에서 caching 추가 가능. |
| (R-5) GHA syntax error | 로컬 yaml 검증 (yq 또는 `gh workflow view`) |

---

## 5. Implementation Guide

### 5.1 Implementation Order

1. `.github/workflows/dashboard.yml` 작성 (~70 lines)
2. yaml syntax 검증 — `yq eval '.' < dashboard.yml > /dev/null` 또는 GHA actionlint
3. build.yml 변경 0 검증 — `git diff .github/workflows/build.yml`
4. 단일 commit
5. (옵션) 실제 PR 또는 push 시 CI 실행 검증 — 본 cycle에서는 yaml 작성까지

### 5.2 Session Plan

총 ≤ 5 turn 예상.

---

## 6. Test Plan

| Level | Test | Pass Criteria |
|---|---|---|
| YAML syntax | `yq eval '.' < dashboard.yml > /dev/null` | exit 0 |
| Path filter coverage | grep 6 paths in dashboard.yml | all present |
| Diff build.yml | `git diff .github/workflows/build.yml` | empty |
| Diff scaffold.yml | `git diff .github/workflows/scaffold.yml` | empty |
| Diff source | `git diff FastPortDashboard.Maui/ FastPortDashboard.Core/ tests-projects/` | empty |
| Local dry-run | `actionlint .github/workflows/dashboard.yml` (옵션) | no errors |
| Runtime CI | 실제 PR/push 시 macOS+Windows pass | 별도 사용자 검증 |

---

## 7. Out of Scope

- NuGet/MAUI workload caching
- Self-hosted runner
- Code coverage
- Artifact upload (Dashboard bundle)
- Release publishing
- Linux 빌드
- 메인 build.yml path filter 추가 (별도 cycle 가능)

---

## Version History

| Version | Date | Changes | Author |
|---|---|---|---|
| 0.1 | 2026-05-11 | Initial design (Option A — Minimal, macOS+Windows, 6 path filter) | boinred |
