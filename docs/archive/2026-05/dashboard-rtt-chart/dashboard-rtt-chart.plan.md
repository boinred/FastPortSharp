# dashboard-rtt-chart Plan

> **Project**: FastPortSharp
> **Author**: boinred
> **Date**: 2026-05-11
> **Status**: Draft
> **PRD**: (lightweight, Plan에 통합)

---

## Executive Summary

| 관점 | 내용 |
|---|---|
| **Problem** | Dashboard에 client RTT 시각화 없음. ClientObservedMetricsSnapshot에 `RttAverageMs`/`RttP50/P95/P99Ms` 필드가 있지만 ViewModel은 ServerObserved만 사용. Chart placeholder만 존재 (`최근 sample 수: N`). |
| **Solution** | Microcharts.Maui를 통해 Client RTT line chart 추가. `ClientRttSeries` (ObservableCollection<TimedDoublePoint>) 신규 + `ApplyClientSnapshot` 로직 + MainPage.xaml에 ChartView. P95Ms 기본, 추후 P50/P99 확장 가능. |
| **Function/UX/Effect** | Mock 모드에서 RTT 시뮬레이션 + JSONL polling 시 실데이터 표시. Foundation cycle의 KPI placeholder 다음 영역에 chart 렌더. |
| **Core Value** | Dashboard 첫 visual chart 완성 → 사용자가 단순 KPI 숫자 외 시계열 트렌드를 볼 수 있음. 다음 cycle (throughput chart, multi-metric)의 reference pattern 확보. |

---

## Context Anchor

| Key | Value |
|-----|-------|
| **WHY** | KPI 수치만으로는 trend 파악 어려움. RTT는 게임서버 성능의 핵심 지표 (latency budget). |
| **WHO** | boinred + 미래 contributor + 게임서버 운영자/개발자. |
| **RISK** | (R-1) Microcharts MAUI 10 호환 / (R-2) MockPollingAdapter ClientObserved 부재 / (R-3) macOS Catalyst rendering 회귀 / (R-4) Series memory growth |
| **SUCCESS** | RTT line chart 렌더 + Mock 데이터로 chart 갱신 + Dashboard 빌드 0/0 + 18 tests 회귀 0 + FastPortSharp.sln 회귀 0 + 수동 실행 chart 시각 확인 |
| **SCOPE** | `FastPortDashboard.Core` (ClientRtt series + ApplyClientSnapshot) + `FastPortDashboard.Maui` (Microcharts pkg + MainPage.xaml ChartView). Production 코드 (LibTestTelemetry 등) 변경 0. |

---

## 1. Overview

### 1.1 Motivation

직전 cycle 종료 시점 dashboard 상태:
- 6 KPI numeric label (CurrentSessions, TotalAccepted, TotalSentBytes 등) ✅
- ThroughputSeries collection (server SentBytesPerSecond) — 데이터 수집되나 visual chart 미존재 ⚠️
- Chart placeholder: `최근 sample 수: N` (chart 자리만 차지)

본 cycle은:
- **새 series**: ClientRttSeries (P95Ms 기본)
- **Chart visual**: Microcharts.Maui LineChart로 placeholder 자리에 실제 chart 렌더
- **Adapter**: MockPollingAdapter가 ClientObserved도 함께 yield하도록 수정 (현재는 ServerObserved만)

### 1.2 Why RTT (cycle 명칭 그대로)

| 선택 안 | 채택? |
|---|---|
| Server Throughput (기존 series 재활용) | ❌ — cycle 명칭과 어긋남. 별도 cycle (`dashboard-throughput-chart`)로 분리. |
| **Client RTT P95Ms** | ✅ — 게임서버 latency budget의 핵심 지표. ClientObservedMetricsSnapshot에 이미 contract 존재. |
| 다중 metric (server + client) | ❌ — overscope. 본 cycle은 RTT 단일에 집중. |

### 1.3 Why Microcharts (사용자 확정)

| Lib | 채택? |
|---|---|
| LiveCharts2 | ❌ — Foundation cycle의 macOS 26 SwiftUI bridging crash가 재현될 risk. 별도 cycle에서 stable 버전 검증 후 재도입 가능. |
| **Microcharts.Maui** | ✅ — MAUI 표준 대안. SkiaSharp 기반 (안정적), simple API, single-instance 사용엔 충분. |
| Custom GraphicsView/SKCanvasView | ❌ — overengineering. |

### 1.4 Out of scope

- Server throughput chart 시각화 (별도 cycle)
- 다중 metric overlay
- Chart interaction (zoom, pan, tooltip)
- RTT P50/P99 추가 series (P95 first, 향후 별도)
- JsonlPollingAdapter 변경 (이미 ClientObserved 지원)
- MockPollingAdapter ClientRtt 시뮬레이션 정교화 (기본 random walk만)

---

## 2. Scope

### 2.1 In Scope

| 영역 | 작업 |
|---|---|
| `FastPortDashboard.Core.csproj` | Microcharts NuGet 추가? 또는 Maui csproj? → Design에서 결정 (chart view는 MAUI 의존) |
| `FastPortDashboard.Maui.csproj` | `<PackageReference Include="Microcharts.Maui" Version="..." />` 추가 + `UseMicrochartsHandler` 등 builder 설정 |
| `FastPortDashboard.Core/ViewModels/DashboardViewModel.cs` | `ClientRttSeries` (ObservableCollection) 신규 + `ApplyClientSnapshot` method + ApplySnapshot에서 ClientObserved 처리 분기 |
| `FastPortDashboard.Core/Adapters/MockPollingAdapter.cs` | ClientObserved snapshot 시뮬레이션 (random walk RTT) |
| `FastPortDashboard.Maui/MauiProgram.cs` | `.UseMicrocharts()` 또는 동등 builder 호출 |
| `FastPortDashboard.Maui/MainPage.xaml` | placeholder를 `microcharts:ChartView`로 교체 |
| `FastPortDashboardTests/.../DashboardViewModelTests.cs` | T-VM-11/12 신규 (ApplyClientSnapshot 검증) |

### 2.2 Out of Scope

- LibTestTelemetry contract 변경
- Server throughput chart
- LiveCharts2 재도입
- Chart interactions
- iOS/Android TFM
- FastPortSharp.sln 변경

### 2.3 Key Constraint

- **Production 코드 (LibTestTelemetry 등) 변경 0**: RTT contract는 이미 존재.
- **Test 회귀 0**: 기존 18 tests + 신규 ~2 tests.
- **CI workflow 변경 0**: Dashboard sln only.
- **macOS Catalyst 호환**: Microcharts SkiaSharp는 macOS Catalyst 안정 지원.

---

## 3. Requirements

### 3.1 Functional

- **FR-1**: `DashboardViewModel`에 `ClientRttSeries: ObservableCollection<TimedDoublePoint>` 추가. `MaxChartPoints=600` trim 동일 적용.
- **FR-2**: `ApplySnapshot` 호출 시 `snap.ClientObserved`도 처리. ClientObserved가 null이면 client 부분 skip (server-only 호환).
- **FR-3**: `ApplyClientSnapshot(ClientObservedMetricsSnapshot)`이 ClientRttSeries에 `(Timestamp.ToUnixTimeMilliseconds(), RttP95Ms)` 추가.
- **FR-4**: `MockPollingAdapter`가 ClientObserved + ServerObserved를 동시 yield (Combined snapshot).
- **FR-5**: MainPage.xaml의 chart placeholder가 `microcharts:ChartView`로 교체되어 ClientRttSeries 렌더.
- **FR-6**: Mock 모드 Connect 시 chart에 line이 1초 간격으로 갱신.
- **FR-7**: 기존 KPI binding + State + Commands 변경 0.

### 3.2 Non-Functional

- **NFR-1**: Dashboard 빌드 0/0 (`net10.0-maccatalyst` + Windows)
- **NFR-2**: Dashboard test 회귀 0 + 신규 ~2 tests pass (총 ~20)
- **NFR-3**: FastPortSharp.sln 빌드 0/0 회귀
- **NFR-4**: 139 tests 회귀 0
- **NFR-5**: CI workflow 변경 0
- **NFR-6**: 단일 commit
- **NFR-7**: macOS Catalyst Release 실행 → Mock Connect → RTT line chart 갱신 (수동)

### 3.3 Compatibility

- Microcharts.Maui (latest stable, MAUI 10 호환)
- `net10.0-maccatalyst` + `net10.0-windows10.0.19041.0`
- SkiaSharp 의존성 (Microcharts transitive)

---

## 4. Success Criteria

### 4.1 Definition of Done

- [ ] Microcharts.Maui NuGet 추가 (Maui csproj)
- [ ] MauiProgram.cs `.UseMicrocharts()` 또는 builder 통합
- [ ] `ClientRttSeries` 추가 + `ApplyClientSnapshot` 메서드
- [ ] `ApplySnapshot`이 ServerObserved + ClientObserved 둘 다 처리
- [ ] MockPollingAdapter가 Combined snapshot (server + client) yield
- [ ] MainPage.xaml chart placeholder → `microcharts:ChartView` 교체
- [ ] DashboardViewModelTests 신규 ~2 tests
- [ ] Dashboard 빌드 0/0
- [ ] Dashboard test ≥ 20 pass
- [ ] FastPortSharp.sln 회귀 0 + 139 tests
- [ ] CI workflow 무변경
- [ ] macOS Catalyst Mock Connect 후 chart 시각 갱신 (수동)
- [ ] 단일 commit

### 4.2 Quality Criteria

- [ ] 한국어 주석
- [ ] 변경 file ≤ 8 (csproj 2 + ViewModel 1 + Adapter 1 + MauiProgram 1 + XAML 1 + Test 1 + docs)
- [ ] Production 코드 (LibTestTelemetry, LibCommons 등) 변경 0

---

## 5. Risks and Mitigation

| Risk | Probability | Impact | Mitigation |
|---|---|---|---|
| (R-1) Microcharts.Maui MAUI 10 호환성 미검증 | Medium | High | Design phase에서 NuGet 버전 + MAUI 10 release notes 확인. 첫 빌드에서 호환 검증. 안 되면 LiveCharts2 fallback. |
| (R-2) MockPollingAdapter 변경으로 기존 18 test 회귀 | Medium | Medium | ApplySnapshot 분기는 backward-compatible 유지 (ClientObserved null 시 skip). Combined yield는 Adapter 출력 형태 변경이므로 affected tests 확인. |
| (R-3) macOS Catalyst rendering crash (Foundation cycle의 LiveCharts2 history) | Low | High | Microcharts는 SkiaSharp 기반이며 SwiftUI bridging 안 함. 수동 실행 검증 필수. |
| (R-4) ClientRttSeries 600 trim 검증 미흡 | Low | Low | T-VM-9의 패턴 그대로 신규 test로 검증. |
| (R-5) Chart 데이터 binding 갱신이 UI thread 외부에서 발생 | Low | Medium | 기존 ApplySnapshot이 UI thread에서 호출되는 contract 유지. 추가 변경 없음. |

---

## 6. Impact Analysis

### 6.1 영향 받는 파일

| 파일 | 작업 | 예상 라인 |
|---|---|---|
| `FastPortDashboard.Maui.csproj` | edit (+1 PackageReference) | +1 |
| `FastPortDashboard.Maui/MauiProgram.cs` | edit (+UseMicrocharts) | +2 |
| `FastPortDashboard.Maui/MainPage.xaml` | edit (chart placeholder 교체) | ±10 |
| `FastPortDashboard.Core/ViewModels/DashboardViewModel.cs` | edit (ClientRttSeries + ApplyClientSnapshot) | +15 |
| `FastPortDashboard.Core/Adapters/MockPollingAdapter.cs` | edit (Combined snapshot) | ±25 |
| `tests-projects/FastPortDashboardTests/ViewModels/DashboardViewModelTests.cs` | edit (+2 tests) | +30 |

총 ~80 lines, 변경 file 6.

### 6.2 영향 받지 않는 영역

- `FastPortSharp.sln` 및 13개 기존 production 프로젝트
- `LibTestTelemetry/` (contract 변경 0)
- `JsonlPollingAdapter` (이미 Combined snapshot 지원)
- `.github/workflows/`
- 기존 18 test (모두 ServerObserved만 사용, 회귀 0 예상)

### 6.3 CI Impact

CI workflow 변경 0. `build.yml`은 FastPortSharp.sln만 빌드 → Dashboard 미실행.

---

## 7. Architecture Considerations

### 7.1 Decision Confirmed (Plan Checkpoint)

| Decision | Choice | Rationale |
|---|---|---|
| Chart target | **Client RTT (P95Ms 기본)** | 사용자 확정. cycle 명칭과 정합, RTT contract 이미 존재. |
| Chart lib | **Microcharts.Maui** | 사용자 확정. SkiaSharp 안정, MAUI 표준, simple API. |
| Production 변경 0 | Yes (LibTestTelemetry 등) | Contract 이미 RTT 필드 보유 |
| Single commit | Yes | 일관성 |

### 7.2 Open Decisions for Design Phase

- **Microcharts pkg version**: latest stable + MAUI 10 호환 verify (Design에서 NuGet 검색)
- **NuGet 위치**: Maui csproj only vs Core csproj도? — chart view는 MAUI dependency이므로 Maui csproj only가 자연스러움. Core는 chart 무관, series collection만.
- **RTT default metric**: P95Ms vs RttAverageMs vs P99Ms — P95 권장 (latency budget 기준)
- **MockPollingAdapter 시뮬레이션**: ClientObserved를 ObservedMetricsSnapshot.Combined로 yield? 또는 별도 yield? — `Combined` static factory 사용 권장.

---

## 8. Convention Prerequisites

- 한국어 주석
- 단일 commit
- `Plan SC: ` / `Design Ref: ` 코멘트 핵심 결정점에 부착
- macOS Catalyst Release 수동 실행으로 chart visual 1회 검증

---

## 9. Next Steps

1. `/pdca design dashboard-rtt-chart`
   - 3 option:
     - **A**: Microcharts Maui-only (NuGet Maui csproj, ChartView XAML 직접) — Recommended
     - **B**: Microcharts + ViewModel에 IChartEntry collection (data binding) — Core 의존성 ↑
     - **C**: Wrapper service (`IRttChartProvider`) — overengineering
2. `/pdca do dashboard-rtt-chart` (단일 세션, ≤ 15 turn 추정)
3. `analyze` → `report` → `archive`

---

## Version History

| Version | Date | Changes | Author |
|---|---|---|---|
| 0.1 | 2026-05-11 | Initial plan (Client RTT P95Ms, Microcharts.Maui, single commit) | boinred |
