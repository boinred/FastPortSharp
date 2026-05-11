# dashboard-ci-add Plan

> **Project**: FastPortSharp
> **Author**: boinred
> **Date**: 2026-05-11
> **Status**: Draft
> **PRD**: (lightweight, Plan에 통합)

---

## Executive Summary

| 관점 | 내용 |
|---|---|
| **Problem** | `FastPortSharp.Dashboard.sln`은 별도 sln으로 격리되어 있어 `build.yml`의 3-OS CI가 빌드하지 않음. Dashboard 회귀가 push 시점에 감지되지 않고 로컬 검증에만 의존. |
| **Solution** | `.github/workflows/dashboard.yml` 신규로 Dashboard sln 전용 CI workflow 추가. macOS + Windows matrix (MAUI workload 요구), Dashboard 관련 path filter로 trigger. |
| **Function/UX/Effect** | Dashboard 관련 PR/push 시 자동 빌드 + 20 tests 실행 → 회귀 자동 감지. 기존 `build.yml` 영향 0. |
| **Core Value** | Dashboard 개발의 CI 안전망 확보. 향후 contributor가 Dashboard 변경 시 즉시 검증, 수동 실행 의존 ↓. |

---

## Context Anchor

| Key | Value |
|-----|-------|
| **WHY** | Dashboard 진화 (7 cycles) 결과 코드 규모 ↑. 회귀 감지를 push/PR 시점으로 앞당겨 안정성 확보. |
| **WHO** | boinred + 미래 contributor (Dashboard 영역 PR 시). |
| **RISK** | (R-1) MAUI workload install 시간 ↑ / (R-2) macOS Release 빌드 시 SkiaSharp warning / (R-3) path filter 누락으로 의도치 않은 trigger |
| **SUCCESS** | dashboard.yml 신규 + macOS/Windows matrix pass + path filter 정확 + build.yml 영향 0 + 단일 commit |
| **SCOPE** | `.github/workflows/dashboard.yml` 신규만. 기존 workflow / 소스 코드 변경 0. |

---

## 1. Overview

### 1.1 Motivation

Dashboard sln (`FastPortSharp.Dashboard.sln`) 격리 정책으로 (`maui-telemetry-dashboard-foundation` cycle 결정):
- 메인 build.yml은 `FastPortSharp.sln`만 빌드 → Dashboard 미검증
- 로컬 `dotnet build/test FastPortSharp.Dashboard.sln`로만 검증
- 7 cycles 동안 Dashboard에 ~500줄 코드 + 20 tests + RTT/Throughput chart 누적

이제 CI 보호선 추가가 적절한 시점:
- Dashboard 회귀를 push/PR 시점에 자동 감지
- 신규 contributor가 안전하게 Dashboard 영역 수정 가능
- 메인 CI 영향 없이 (path filter로 격리)

### 1.2 Platform 제약

| OS | net10.0-maccatalyst | net10.0-windows | net10.0 (test) |
|---|---|---|---|
| macOS | ✅ (workload maui) | ❌ (불가) | ✅ |
| Windows | ❌ (불가) | ✅ (workload maui) | ✅ |
| Linux | ❌ | ❌ | ✅ |

**결론**: macOS + Windows 2-OS matrix가 적합. Linux는 MAUI 프로젝트 빌드 불가하므로 제외.

### 1.3 Out of scope

- 메인 `build.yml` 수정 (path filter 추가 등)
- Dashboard app 자동 실행 (macOS 26 SwiftUI crash 알려진 이슈)
- Code coverage 수집
- Artifact 업로드
- Release binary publishing

---

## 2. Scope

### 2.1 In Scope

| 영역 | 작업 |
|---|---|
| `.github/workflows/dashboard.yml` | 신규 (Dashboard sln 빌드 + test, macOS + Windows matrix) |

### 2.2 Out of Scope

- 기존 workflow 변경 (`build.yml`, `scaffold.yml`)
- 소스 코드 변경
- README/HANDOFF 갱신 (선택 사항, 별도 cycle)
- Linux 빌드
- Release publishing
- Code coverage

### 2.3 Key Constraint

- **기존 build.yml 영향 0**: branch trigger 같음 (builds.release)이지만 path filter로 분리
- **MAUI workload install**: macOS/Windows runner 모두 필요 (~3-5분 추가)
- **3-OS pattern 안 함**: Linux 제외 (MAUI 불가)

---

## 3. Requirements

### 3.1 Functional

- **FR-1**: `.github/workflows/dashboard.yml` 신규 파일
- **FR-2**: Trigger: `push` to `builds.release` + `pull_request` to `builds.release` + `workflow_dispatch`
- **FR-3**: Path filter — Dashboard 관련 파일 변경 시만 실행:
  - `FastPortDashboard.Maui/**`
  - `FastPortDashboard.Core/**`
  - `tests-projects/FastPortDashboardTests/**`
  - `tests-projects/LibTestTelemetry/**` (의존성)
  - `FastPortSharp.Dashboard.sln`
  - `.github/workflows/dashboard.yml` (self-referential)
- **FR-4**: Matrix: `os: [macos-latest, windows-latest]`
- **FR-5**: Steps:
  1. Force LF on Windows (build.yml 동일 패턴)
  2. Checkout
  3. Setup .NET 10
  4. `dotnet workload install maui` (MAUI workload 설치)
  5. `dotnet restore FastPortSharp.Dashboard.sln`
  6. `dotnet build FastPortSharp.Dashboard.sln -c Release --no-restore`
  7. `dotnet test FastPortSharp.Dashboard.sln -c Release --no-build --logger "console;verbosity=normal"`
- **FR-6**: 두 OS 모두 pass 조건. fail-fast: false (한쪽 실패해도 다른 쪽 계속).

### 3.2 Non-Functional

- **NFR-1**: 기존 `build.yml`, `scaffold.yml` 변경 0
- **NFR-2**: 소스 코드 변경 0
- **NFR-3**: Dashboard CI 시간 ≤ 15분 (MAUI workload install 포함)
- **NFR-4**: 단일 commit
- **NFR-5**: `.github/workflows/dashboard.yml` 자체 trigger 시 self-test 가능

### 3.3 Compatibility

- `actions/checkout@v4`, `actions/setup-dotnet@v4` (build.yml과 동일 버전)
- `.NET 10.0.x`
- MAUI workload (`dotnet workload install maui`)

---

## 4. Success Criteria

### 4.1 Definition of Done

- [ ] `.github/workflows/dashboard.yml` 생성
- [ ] Trigger: builds.release + path filter + workflow_dispatch
- [ ] Matrix: macOS + Windows
- [ ] MAUI workload install 단계 포함
- [ ] Dashboard sln Restore + Build + Test 단계
- [ ] 기존 build.yml/scaffold.yml git diff 0
- [ ] 소스 코드 git diff 0
- [ ] 단일 commit
- [ ] 로컬 yaml syntax 검증 (yq/yamllint 또는 GitHub Action `actionlint`)
- [ ] (옵션) 첫 PR에서 실제 CI pass 확인 — 별도 검증

### 4.2 Quality Criteria

- [ ] 한국어 + 영문 comment 혼용 (workflow는 영문 권장, 한국어 보조)
- [ ] build.yml과 동일 스타일 (들여쓰기, naming, key 순서)
- [ ] path filter 누락 0 (5+1 path entries)

---

## 5. Risks and Mitigation

| Risk | Probability | Impact | Mitigation |
|---|---|---|---|
| (R-1) MAUI workload install 실패 (network/SDK 호환) | Low | High | `actions/setup-dotnet@v4` 다음에 명시적 `dotnet workload install maui` 실행. 실패 시 step 실패 → CI red. |
| (R-2) macOS Release 빌드 SkiaSharp OpenGLES warning | Low | Low | Warning은 무해 (build.yml의 회귀 sln도 빌드 통과). `warning-as-error` 비활성. |
| (R-3) Path filter 누락으로 의도치 않은 trigger | Medium | Low | 6 path entries 명시. PR 단계에서 actual trigger 확인. |
| (R-4) Windows runner Maui workload + Catalyst 호환 | Low | Medium | Catalyst는 macOS only. Windows runner는 `net10.0-windows` TFM만 빌드. 자동 분기 (`<TargetFrameworks Condition="...">`로 처리됨). |
| (R-5) CI 시간 ↑ (~15분 추가) | Low | Low | Path filter로 Dashboard 변경 시만 실행 → 평소엔 영향 0. |

---

## 6. Impact Analysis

### 6.1 영향 받는 파일

| 파일 | 작업 | 예상 라인 |
|---|---|---|
| `.github/workflows/dashboard.yml` | new | ~70 |

총 1 파일 + 2 docs.

### 6.2 영향 받지 않는 영역

- `.github/workflows/build.yml` (3-OS, FastPortSharp.sln)
- `.github/workflows/scaffold.yml`
- 소스 코드 (FastPortDashboard.Maui, .Core, tests, LibTestTelemetry 등)
- `.pdca-status.json`
- README/HANDOFF (선택 cycle로 분리)

### 6.3 CI Impact

| Scenario | Before | After |
|---|---|---|
| Push to main (no dashboard change) | build.yml (3-OS, ~10분) | 동일 |
| Push to builds.release (no dashboard change) | build.yml (3-OS, ~10분) | 동일 (dashboard.yml path filter 차단) |
| Push to builds.release (dashboard 변경) | build.yml (~10분) | build.yml (~10분) + dashboard.yml (~15분) |
| PR to builds.release (dashboard 변경) | build.yml | build.yml + dashboard.yml |

---

## 7. Architecture Considerations

### 7.1 Decisions (Auto mode, Plan Checkpoint)

| Decision | Choice | Rationale |
|---|---|---|
| Platform matrix | **macOS + Windows** | MAUI workload 가능한 OS만. Linux 제외 (불가). |
| Trigger | **builds.release branch + path filter** | build.yml과 동일 branch + path filter로 격리, 평소 CI 영향 0 |
| Path scope | Dashboard 관련 6 path entries | Maui app + Core lib + Tests + LibTestTelemetry + sln + self |
| MAUI workload | install 단계 명시 | `setup-dotnet`만으론 maui workload 미포함 |
| 단일 commit | Yes | 일관성 |

### 7.2 Open Decisions for Design Phase

- **Caching**: NuGet/MAUI workload caching (`actions/cache@v4`) 추가 여부 (CI 시간 단축 ~50% 가능)
- **fail-fast**: matrix fail-fast=false (build.yml 동일) vs true
- **Test logger**: `console;verbosity=normal` vs `console;verbosity=detailed` (debug용)

---

## 8. Convention Prerequisites

- 한국어 + 영문 comment 혼용
- build.yml과 동일 스타일 (들여쓰기 2 spaces, key 순서, action 버전)
- 단일 commit

---

## 9. Next Steps

1. `/pdca design dashboard-ci-add`
   - 3 option:
     - **A**: Minimal — MAUI workload install + Restore + Build + Test (Recommended)
     - **B**: + NuGet/workload caching (`actions/cache@v4`)
     - **C**: + Artifact upload (Dashboard bundle) + code coverage
2. `/pdca do dashboard-ci-add` (단일 세션, ≤ 6 turn 추정)
3. `analyze` → `report` → `archive`

---

## Version History

| Version | Date | Changes | Author |
|---|---|---|---|
| 0.1 | 2026-05-11 | Initial plan (Auto mode: macOS+Windows matrix + builds.release branch + path filter) | boinred |
