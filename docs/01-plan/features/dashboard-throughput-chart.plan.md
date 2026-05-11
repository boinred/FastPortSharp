# dashboard-throughput-chart Plan

> **Project**: FastPortSharp
> **Author**: boinred
> **Date**: 2026-05-11
> **Status**: Draft
> **PRD**: (lightweight, Plan에 통합)

---

## Executive Summary

| 관점 | 내용 |
|---|---|
| **Problem** | RTT chart는 추가됐으나 Server throughput (SentBytesPerSecond) 시각화 없음. ThroughputSeries는 Foundation cycle부터 수집되었으나 ChartView 미연결. |
| **Solution** | RTT chart 아래 Throughput LineChart Frame을 vertical stack으로 추가. 직전 cycle의 code-behind 패턴 그대로 재사용 (`UpdateThroughputChart()`, CollectionChanged 구독). |
| **Function/UX/Effect** | Mock/JSONL 모두에서 RTT와 Throughput 동시 갱신, 사용자가 한 화면에서 latency + bandwidth trend 동시 관찰. |
| **Core Value** | dashboard-rtt-chart의 code-behind reference pattern을 검증 (재사용성 입증) + 게임서버 throughput visibility 확보. |

---

## Context Anchor

| Key | Value |
|-----|-------|
| **WHY** | RTT(latency)만으로는 server 트래픽 상황 불완전. Throughput(bandwidth)은 정상 부하 vs 과부하 판단의 핵심 보조 지표. |
| **WHO** | boinred + 미래 contributor + 게임서버 운영자. |
| **RISK** | (R-1) ThroughputSeries.Count 표시(`최근 sample 수`)와 chart 동시 표시 정리 / (R-2) Multi-chart 동시 갱신 성능 / (R-3) macOS Catalyst 두 ChartView 동시 rendering 호환 |
| **SUCCESS** | Throughput LineChart 렌더 + RTT chart 무영향 + 빌드 0/0 + 20 tests 회귀 0 + 회귀 sln 0 + 수동 시각 확인 |
| **SCOPE** | `FastPortDashboard.Maui` only (MainPage.xaml + MainPage.xaml.cs). Core lib + Production 코드 변경 0. |

---

## 1. Overview

### 1.1 Motivation

직전 cycle 종료 시점 dashboard 상태:
- 6 KPI numeric labels ✅
- Client RTT P95 LineChart (visual) ✅ — dashboard-rtt-chart cycle에서 도입
- ThroughputSeries collection — Foundation cycle부터 채워지나 chart 미연결, `최근 sample 수: N` placeholder만 표시

본 cycle은 직전 cycle의 reference pattern을 적용:
- `MainPage.xaml`: 신규 Frame + `microcharts:ChartView x:Name="ThroughputChartView"` 추가
- `MainPage.xaml.cs`: `_viewModel.ThroughputSeries.CollectionChanged` 구독 + `UpdateThroughputChart()` (RTT chart와 동일 패턴)

### 1.2 ViewModel/Core 변경 0 (Important)

- `ThroughputSeries`는 이미 ViewModel에 존재
- `ApplySnapshot`이 이미 `ThroughputSeries.Add(server.SentBytesPerSecond)` 수행
- → ViewModel/Adapter/Core 변경 불필요

### 1.3 UI 정리

- RTT chart Frame 그대로 (변경 0)
- 그 아래 새 Frame: "Server Throughput (B/s)" + Throughput LineChart
- 기존 placeholder Frame(`ThroughputSeries.Count, StringFormat='최근 sample 수: {0}'`)는 RTT cycle에서 이미 ClientRttSeries Count로 변환됨. 본 cycle은 별도 처리 불요.

### 1.4 Out of scope

- Server throughput chart에 P50/P95 split 표시
- Y축 단위 변환 (B/s vs KB/s vs Mb/s) — Microcharts ValueLabel으로 raw bytes/sec 표시
- LiveCharts2 재도입
- ViewModel/Adapter 변경
- Test 변경 (ThroughputSeries는 기존 T-VM-8/9에서 이미 검증)

---

## 2. Scope

### 2.1 In Scope

| 영역 | 작업 |
|---|---|
| `FastPortDashboard.Maui/MainPage.xaml` | RTT Frame 아래 Throughput Frame 추가 (Frame 안에 Label + ChartView) |
| `FastPortDashboard.Maui/MainPage.xaml.cs` | `ThroughputSeries.CollectionChanged` 구독 + `UpdateThroughputChart()` (RTT 패턴 재사용) |

### 2.2 Out of Scope

- `FastPortDashboard.Core` (ViewModel + Adapter): 변경 0
- `tests-projects/FastPortDashboardTests`: 변경 0 (기존 20 tests 회귀 0 검증만)
- Production 코드 (LibTestTelemetry 등): 변경 0
- `FastPortSharp.sln`: 변경 0
- CI workflow: 변경 0

### 2.3 Key Constraint

- **Core lib + ViewModel 변경 0**: Throughput은 이미 데이터 layer 완비
- **Test 회귀 0**: 20 tests 그대로 통과
- **UI 패턴 일관성**: RTT chart와 동일 Frame 구조 + 동일 code-behind pattern
- **macOS Catalyst 호환**: Microcharts ChartView × 2 동시 사용 호환 확인 (수동 검증)

---

## 3. Requirements

### 3.1 Functional

- **FR-1**: MainPage.xaml에 RTT Frame 다음 위치에 새 Frame 추가:
  - 제목 Label: "Server Throughput (B/s)"
  - 부제 Label: `ThroughputSeries.Count` (최근 sample 수)
  - `<microcharts:ChartView x:Name="ThroughputChartView" HeightRequest="160" />`
- **FR-2**: MainPage.xaml.cs constructor에서:
  - `_viewModel.ThroughputSeries.CollectionChanged += (_, _) => UpdateThroughputChart();`
  - `UpdateThroughputChart();` 호출
- **FR-3**: `UpdateThroughputChart()` 메서드:
  - `ThroughputSeries` → `ChartEntry[]` 변환 (color: green 계열 `#4CAF50`)
  - `ThroughputChartView.Chart = new LineChart { Entries = entries, LineMode = Straight, LineSize = 2, PointMode = None, BackgroundColor = Transparent }`
- **FR-4**: RTT chart 동작 변경 0 (기존 `UpdateRttChart()` 유지)
- **FR-5**: Mock Connect 시 두 chart 동시 갱신

### 3.2 Non-Functional

- **NFR-1**: `dotnet build FastPortSharp.Dashboard.sln -c Release` 0/0
- **NFR-2**: `dotnet test FastPortSharp.Dashboard.sln -c Release --no-build` 20/0/0 (회귀 0)
- **NFR-3**: `dotnet build FastPortSharp.sln -c Release` 회귀 0/0
- **NFR-4**: `dotnet test FastPortSharp.sln -c Release --no-build` 139/0/0
- **NFR-5**: CI workflow 변경 0
- **NFR-6**: 단일 commit
- **NFR-7**: macOS Catalyst Release Mock Connect 시 두 chart 동시 갱신 (수동 1회)

### 3.3 Compatibility

- Microcharts.Maui 1.0.1 (직전 cycle 도입)
- `net10.0-maccatalyst` + `net10.0-windows10.0.19041.0`
- 기존 RTT chart UX 보존

---

## 4. Success Criteria

### 4.1 Definition of Done

- [ ] MainPage.xaml 신규 Throughput Frame (LineChart 포함)
- [ ] MainPage.xaml.cs `UpdateThroughputChart()` + CollectionChanged subscribe
- [ ] RTT chart 변경 0 (기존 code path 유지)
- [ ] Core lib + ViewModel + Adapter 변경 0
- [ ] Dashboard 빌드 0/0
- [ ] Dashboard test 20/0/0 (회귀 0)
- [ ] FastPortSharp.sln 회귀 0 (build + 139 tests)
- [ ] CI workflow 무변경
- [ ] macOS Catalyst Release 두 chart 동시 갱신 (수동)
- [ ] 단일 commit

### 4.2 Quality Criteria

- [ ] 한국어 주석
- [ ] 변경 파일 ≤ 4 (MainPage.xaml + MainPage.xaml.cs + 2 docs)
- [ ] RTT chart와 동일 패턴 (color만 다름)

---

## 5. Risks and Mitigation

| Risk | Probability | Impact | Mitigation |
|---|---|---|---|
| (R-1) ThroughputSeries.Count placeholder label과 chart 중복 표시 | Low | Low | "최근 sample 수: {0}" 부제는 chart 보조 정보로 유지 (사용자에게 sample 수가 명시되어 trend 판단 용이) |
| (R-2) Multi-chart 동시 갱신 성능 (UpdateChart 두 번 호출) | Low | Low | 각 chart는 독립 collection, CollectionChanged event도 독립. 영향 0. |
| (R-3) macOS Catalyst 두 ChartView 동시 SkiaSharp 렌더링 호환 | Low | Medium | Microcharts.Maui는 multi-instance 지원 표준 패턴. 수동 검증 1회. |
| (R-4) UpdateChart 호출 시 ChartView 재생성 비용 | Low | Low | 직전 cycle에서 RTT chart 동일 패턴 정상 동작 검증. 동일 패턴 재사용. |

---

## 6. Impact Analysis

### 6.1 영향 받는 파일

| 파일 | 작업 | 예상 라인 |
|---|---|---|
| `FastPortDashboard.Maui/MainPage.xaml` | edit (+1 Frame) | +10 |
| `FastPortDashboard.Maui/MainPage.xaml.cs` | edit (+UpdateThroughputChart + subscribe) | +20 |

총 2개 파일, ~30 lines.

### 6.2 영향 받지 않는 영역

- `FastPortDashboard.Core` (전체)
- `tests-projects/FastPortDashboardTests` (전체)
- `LibTestTelemetry`
- 13개 production 프로젝트
- `.github/workflows/`
- ViewModel/Adapter/State 로직

### 6.3 CI Impact

CI workflow 변경 0. `build.yml`은 FastPortSharp.sln만 빌드 → 본 cycle 미영향.

---

## 7. Architecture Considerations

### 7.1 Decision Confirmed (Plan Checkpoint)

| Decision | Choice | Rationale |
|---|---|---|
| Chart layout | **RTT 아래에 수직 스택** | 사용자 확정. 두 chart 동시 관찰 가능, 단순한 layout. |
| Chart lib | Microcharts.Maui (RTT cycle 동일) | 일관성 |
| Core/ViewModel 변경 | **0** | ThroughputSeries 이미 존재 |
| Test 변경 | 0 | 기존 T-VM-8/9 (ThroughputSeries) 그대로 검증 |
| Single commit | Yes | 일관성 |

### 7.2 Open Decisions for Design Phase

- **Chart color**: green 계열 `#4CAF50` (Material Design) — RTT 파란색과 시각적 구분 (확정)
- **HeightRequest**: 160 (RTT와 동일) vs 120 (compact)
- **부제 Label**: `ThroughputSeries.Count` 표시 유지 vs 제거

---

## 8. Convention Prerequisites

- 한국어 주석
- 단일 commit
- RTT chart와 동일 code-behind 패턴 (UpdateRttChart → UpdateThroughputChart 미러)

---

## 9. Next Steps

1. `/pdca design dashboard-throughput-chart`
   - 3 option:
     - **A**: Code-behind 패턴 재사용 (UpdateThroughputChart 별도 method, RTT와 mirror) — Recommended
     - **B**: 단일 generic `UpdateChart(IEnumerable<TimedDoublePoint> series, ChartView view, SKColor color)` 헬퍼로 통합
     - **C**: Chart Manager 클래스 (overengineering)
2. `/pdca do dashboard-throughput-chart` (단일 세션, ≤ 10 turn 추정)
3. `analyze` → `report` → `archive`

---

## Version History

| Version | Date | Changes | Author |
|---|---|---|---|
| 0.1 | 2026-05-11 | Initial plan (Maui-only UI 변경, ViewModel 변경 0, RTT chart 패턴 재사용) | boinred |
