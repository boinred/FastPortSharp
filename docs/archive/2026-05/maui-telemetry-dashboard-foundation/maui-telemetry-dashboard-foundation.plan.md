# maui-telemetry-dashboard-foundation Plan

> **Project**: FastPortSharp
> **Author**: boinred
> **Date**: 2026-05-10
> **Status**: Draft
> **PRD**: [../../00-pm/maui-telemetry-dashboard-foundation.prd.md](../../00-pm/maui-telemetry-dashboard-foundation.prd.md)

---

## Executive Summary

| 관점 | 내용 |
|---|---|
| **Problem** | server가 `ObservedMetricsSnapshot` JSONL을 출력하지만 사람이 실시간으로 볼 수 있는 GUI가 없음. tail/jq로 비효율 분석. |
| **Solution** | .NET MAUI desktop app `FastPortDashboard.Maui` 신설 (macOS Catalyst + Windows desktop). JSONL polling으로 실시간 chart 1-2개 + KPI 4-6개. **별도 sln**(`FastPortSharp.Dashboard.sln`)으로 격리해 기존 build.yml CI 영향 0. **LiveCharts2** 차트 라이브러리 채택. |
| **Function/UX/Effect** | metrics file 경로 지정 → 1초 단위 TPS/RTT P95 chart + KPI. healthy 환경 onboarding ~30초. |
| **Core Value** | 실시간 가시성 → load validation 분석 시간 ↓. 다음 cycle (run viewer / report export) 토대. |

---

## Context Anchor

| Key | Value |
|-----|-------|
| **WHY** | Server JSONL telemetry를 시각화할 GUI 부재로 분석 비효율. 본격 dashboard track의 시드 cycle. |
| **WHO** | boinred (load validation 분석), 미래 contributor / AI agent, MAUI/UI 학습자. |
| **RISK** | (R-1) MAUI workload 부담 / (R-2) chart lib 호환성 / (R-3) JSONL 동시 read race / (R-4) 첫 MAUI 학습 비용 / (R-5) 기존 build.yml CI 회귀 |
| **SUCCESS** | 새 sln macOS Catalyst 빌드 PASS + sample JSONL chart 갱신 + 실제 server live update + 139 tests 회귀 0 + dashboard unit test 1-2개 + 사용법 docs |
| **SCOPE** | 신규 `FastPortDashboard.Maui/` (root) + `FastPortSharp.Dashboard.sln` (root) + LibTestTelemetry 컨트랙트 재사용 + LiveCharts2 + JSONL polling + mock + 단일 view |

---

## 1. Overview

### 1.1 Foundation 의도

이번 cycle은 dashboard track의 **시드** — 단일 view + 단일 chart + 단일 KPI 묶음으로 working sample 구축. 다음 cycle (run viewer, report export, dual-pane comparison)에서 같은 토대를 확장.

### 1.2 Why 별도 sln

직전 cycle (`move-test-projects-to-testprojects-folder`)에서 build.yml은 main push trigger 미사용 (builds.release만)이지만 workflow_dispatch + `FastPortSharp.sln` 기준으로 빌드. MAUI 프로젝트를 `FastPortSharp.sln`에 추가하면:

- macOS GHA runner도 `dotnet workload install maui`가 필요 (수 분 ↑)
- workload 실패 시 기존 139 test 빌드도 깨짐 (single sln dependency)
- builds.release 배포 시점에 MAUI 미준비여도 영향

별도 `FastPortSharp.Dashboard.sln`으로 격리하면:

- 기존 build.yml은 `FastPortSharp.sln`만 빌드 → 회귀 0
- Dashboard 빌드는 별도 workflow (이번 cycle 외) 또는 로컬에서 수동
- 사용자 의도 분리: "엔진 + 게임 서버" vs "운영 도구"

### 1.3 LiveCharts2 채택

| 후보 | Foundation 적합성 |
|---|---|
| **LiveCharts2** | MAUI Catalyst + Windows desktop 공식 지원, 실시간 갱신 API 안정, MIT |
| Microsoft.Maui.Graphics + 자체 chart | 의존 0이지만 Foundation 시점에 chart 코드 직접 구현은 학습 비용 ↑ |
| OxyPlot.Maui | 과학용 차트 강함, MAUI 지원이 아직 제한적 |

### 1.4 LibTestTelemetry 의존 결정

`FastPortDashboard.Maui`가 `ObservedMetricsSnapshot` JSONL을 deserialize하려면 `LibTestTelemetry`의 data contract 필요. 직전 cycle에서 LibTestTelemetry는 `tests-projects/`로 이동했지만, 본 cycle은 production 프로젝트가 test-prefix lib를 참조하는 형태로 진행 — 다음 cycle 후보 (`rename-libtesttelemetry-to-libtelemetrycontracts`)로 이름 정리 검토.

본 cycle은 단순히 `<ProjectReference Include="..\tests-projects\LibTestTelemetry\LibTestTelemetry.csproj" />` 추가.

---

## 2. Scope

### 2.1 In Scope

#### 신규 프로젝트
- `FastPortDashboard.Maui/` (repo root, production)
- `FastPortSharp.Dashboard.sln` (repo root)

#### TargetFramework
- `net10.0-maccatalyst`
- `net10.0-windows10.0.19041.0`

#### 기능 (단일 view)
- **File picker** (또는 textbox + browse) — `server.metrics.jsonl` path
- **Connect** 버튼 → polling 시작
- **Live chart 1-2개**:
  - TPS (총 send/recv per second)
  - RTT P95 (server-observed)
- **Numeric KPI** 4-6개:
  - 현재 active sessions
  - Total sent bytes
  - Total recv packets
  - Pending send requests
  - Last update timestamp
- **Mock data 모드**: 실제 server 없이 sample JSONL로 UI 검증
- **상태 표시**: "Connected", "Polling", "Disconnected" 등

#### 데이터 어댑터
- JSONL polling helper (직전 cycle의 lesson 적용 — `FileShare.ReadWrite` 명시)
- `ObservedMetricsJson.SerializerOptions`로 deserialize
- 1초 polling 간격 (server export interval 1s와 일치)

#### Unit test
- JSONL adapter 테스트 1-2개 (sample JSONL → snapshot 파싱)
- mock chart data 어댑터 테스트 1개

#### Docs
- `FastPortDashboard.Maui/README.md` — 빌드/실행/스크린샷 가이드 1 페이지
- root `README.md` 폴더 구조 1 line 추가
- `HANDOFF.md` Roadmap §4 status 업데이트

### 2.2 Out of Scope

- iOS / Android 빌드 (별도 cycle)
- Multi-run 비교 / side-by-side
- PDF / HTML report export
- 실시간 control (server에 명령 전송)
- 인증 / multi-user
- Dashboard용 GHA workflow 신설 (이번 cycle은 로컬 빌드만)
- LibTestTelemetry 이름 변경 / 위치 변경
- `FastPortSharp.sln`에 dashboard 통합

---

## 3. Requirements

### 3.1 Functional

- **FR-1**: `dotnet build FastPortSharp.Dashboard.sln -c Release -f net10.0-maccatalyst` macOS에서 0 warning / 0 error.
- **FR-2**: 실행 후 file picker로 sample `server.metrics.jsonl` 지정 시 chart 1초 단위 갱신.
- **FR-3**: Mock data 토글 → 실제 file 없이 UI 동작.
- **FR-4**: KPI 6개 모두 `ObservedMetricsSnapshot.ServerObserved`에서 실측 값으로 표시.
- **FR-5**: JSONL 파일 truncation/rotation 시 graceful 재시작 (Foundation은 재시작 후 재연결 OK).
- **FR-6**: dashboard adapter unit test 1-2개 + chart binding test 1개 통과.

### 3.2 Non-Functional

- **NFR-1**: 기존 `FastPortSharp.sln` 빌드/테스트 회귀 0.
- **NFR-2**: 신규 sln 빌드 wall-clock ≤ 60초 (initial workload restore 제외).
- **NFR-3**: chart 1초 갱신 시 CPU usage 미미 (≤ 5% on idle laptop).
- **NFR-4**: 새 NuGet 의존: LiveCharts2 + Microsoft.Maui.Controls + skin 의존 (불가피한 transitive 외 직접 추가는 LiveCharts2 1개).
- **NFR-5**: 직전 cycle의 file share lesson 준수 — reader는 `FileShare.ReadWrite` 명시.

### 3.3 Compatibility

- net10.0 + MAUI workload (사용자 환경에 dotnet workload install maui 필요)
- macOS Catalyst (보유 환경)
- Windows desktop 빌드 통과 (실제 실행은 사용자 환경 외)
- 기존 139 tests 회귀 0
- `tests/scaffold/run.sh` 회귀 0 (scaffold-related path 무영향)

---

## 4. Success Criteria

### 4.1 Definition of Done

- [ ] `FastPortDashboard.Maui/` 프로젝트 생성 (csproj + Program.cs + MauiProgram.cs + MainPage.xaml.cs)
- [ ] `FastPortSharp.Dashboard.sln` 생성 (FastPortDashboard.Maui + tests-projects/LibTestTelemetry 참조)
- [ ] LiveCharts2 NuGet 추가
- [ ] JSONL polling 어댑터 (private class) 구현 + `FileShare.ReadWrite` 명시
- [ ] Mock data 모드 동작
- [ ] TPS chart 1초 갱신
- [ ] RTT P95 chart 1초 갱신 (없으면 TPS 1개로 시작)
- [ ] KPI 4-6개 표시
- [ ] `dotnet build FastPortSharp.Dashboard.sln -c Release -f net10.0-maccatalyst` 0/0
- [ ] `dotnet build FastPortSharp.sln -c Release` 회귀 0/0
- [ ] `dotnet test FastPortSharp.sln -c Release --no-build` 139/0/0
- [ ] Dashboard adapter unit test 1-2개 통과 (별도 test 프로젝트 or `FastPortDashboard.Maui.Tests` — Foundation은 dashboard csproj 내부 단위 테스트로도 OK)
- [ ] `tests/scaffold/run.sh` 7/7 회귀 0
- [ ] 실제 `FastPortTestSmokeServer` 띄우고 dashboard에서 live update 확인 (수동 검증 1회)
- [ ] `FastPortDashboard.Maui/README.md` 작성 (빌드/실행/스크린샷)
- [ ] root `README.md` / `README.ko.md` 폴더 구조 1 line 추가
- [ ] `HANDOFF.md` Roadmap §4에 Foundation 완료 기록

### 4.2 Quality Criteria

- [ ] 한국어 주석 컨벤션 적용
- [ ] 변경 file ≤ 25 (신규 프로젝트 일반 file 수 + 수정 docs)
- [ ] 단일 commit으로 마무리 (직전 cycle 패턴) — 단, 폴더 추가 + sln 추가 + docs는 한 commit에 묶기

---

## 5. Risks and Mitigation

| Risk | Probability | Impact | Mitigation |
|---|---|---|---|
| (R-1) MAUI workload 미설치 | Medium | High | 별도 sln으로 격리. README에 `dotnet workload install maui` 명시. CI는 별도 cycle. |
| (R-2) LiveCharts2 macOS Catalyst 호환 이슈 | Low | Medium | 공식 sample 검증 후 도입. 문제 시 Microsoft.Maui.Graphics 자체 구현 (Plan §1.3 fallback). |
| (R-3) JSONL 동시 read race (windows) | Low | Medium | 직전 cycle의 lesson 적용 — `FileShare.ReadWrite` 명시. |
| (R-4) 첫 MAUI 학습 비용 | Medium | Low | Foundation scope 협소화. 단일 view + mock data로 시작. |
| (R-5) 기존 build.yml CI 회귀 | Low | High | 별도 sln으로 격리 → build.yml은 FastPortSharp.sln만 빌드 → 회귀 0. |
| (R-6) LibTestTelemetry 이름 의문 (production이 test-prefix lib 참조) | Low | Low | 본 cycle은 그대로 사용. 다음 cycle 후보 `rename-libtesttelemetry-to-libtelemetrycontracts`로 정리. |

---

## 6. Impact Analysis

### 6.1 영향 받는 파일

| 영역 | 형태 | 예상 라인 |
|---|---|---|
| `FastPortDashboard.Maui/` (new project) | NEW | ~300-500 (xaml + cs + csproj) |
| `FastPortSharp.Dashboard.sln` | NEW | ~50 |
| `FastPortDashboard.Maui/README.md` | NEW | ~50 |
| `README.md` 폴더 구조 | edit | 1 line |
| `README.ko.md` 폴더 구조 | edit | 1 line |
| `HANDOFF.md` Roadmap §4 | edit | ~5 lines |

### 6.2 영향 받지 않는 영역

- `FastPortSharp.sln` 및 13개 기존 프로젝트
- `tests/` (PDCA scaffold infra)
- `scripts/`, `.github/workflows/`
- `tests-projects/`의 5 프로젝트 (LibTestTelemetry 참조만 추가)
- 직전 cycle docs

### 6.3 Performance Impact

- 기존 139 tests: 0 (별도 sln)
- 기존 빌드 시간: 0
- Dashboard 빌드: workload restore 1회 후 30-60초

---

## 7. Architecture Considerations

### 7.1 Decision Confirmed (Plan Checkpoint)

| Decision | Choice | Rationale |
|---|---|---|
| 프로젝트 이름 | **`FastPortDashboard.Maui`** | 사용자 명시. 기존 `FastPortGameServerTemplate.SampleClient` 명명 패턴 + .Maui suffix. |
| Sln 전략 | **별도 `FastPortSharp.Dashboard.sln`** | 사용자 명시. build.yml CI 격리, MAUI workload 부담 차단. |
| Chart library | **LiveCharts2** | 사용자 명시. MAUI Catalyst 공식 지원, 실시간 API 안정, MIT. |
| LibTestTelemetry 의존 | tests-projects/LibTestTelemetry 직접 참조 | YAGNI: 이름 정리는 다음 cycle. |
| Mock data 모드 | sample JSONL 1개 (artifacts/load-validation에서 1개 선정) | 실 server 없이도 UI 검증 가능. |
| Polling 간격 | **1초** | server export interval 1s와 일치. |

### 7.2 Open Decisions for Design Phase

- `FastPortDashboard.Maui` 내부 layer 구조: MVVM (ViewModel) vs code-behind (단순 binding)
- Chart 갱신 방식: ObservableCollection vs INotifyPropertyChanged
- File watcher (FileSystemWatcher) vs poll-based (Task.Delay 1s)
- Mock data 토글 위치: 메뉴 vs 시작 화면

---

## 8. Convention Prerequisites

- 한국어 주석 컨벤션 그대로
- production 프로젝트 명명 (`FastPort` prefix)
- MAUI 표준 layout (Platforms/, Resources/, MauiProgram.cs)
- 직전 cycles의 file share lesson 적용

---

## 9. Next Steps

1. `/pdca design maui-telemetry-dashboard-foundation`
   - 3 architecture options:
     - **A**: Code-behind only (단순) — Foundation 적합
     - **B**: MVVM 표준 (ViewModel + INotifyPropertyChanged)
     - **C**: Reactive Extensions / Rx pattern
   - File watcher vs polling 결정
   - Mock data 형식 정함
   - Session Guide 정의
2. `/pdca do ...` — 다중 세션 가능 (project skeleton / data adapter / UI / polish)
3. Check + Report + Archive

---

## Version History

| Version | Date | Changes | Author |
|---|---|---|---|
| 0.1 | 2026-05-10 | Initial plan (별도 sln + LiveCharts2 + FastPortDashboard.Maui + LibTestTelemetry 직접 참조) | boinred |
