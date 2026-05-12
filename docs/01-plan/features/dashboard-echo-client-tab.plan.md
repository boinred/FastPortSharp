---
template: plan
version: 1.3
feature: dashboard-echo-client-tab
date: 2026-05-11
author: boinred
project: FastPortSharp
---

# dashboard-echo-client-tab Planning Document

> **Summary**: Dashboard를 2-탭 구조로 분리하고 새 Echo Client 탭에서 직접 FastPort 서버(host/port 입력)에 접속하여 EchoRequest/EchoResponse 왕복 RTT와 송수신 속도를 실시간 측정/시각화한다.
>
> **Project**: FastPortSharp
> **Version**: 0.1.0
> **Author**: boinred
> **Date**: 2026-05-11
> **Status**: Draft

---

## Executive Summary

| Perspective | Content |
|-------------|---------|
| **Problem** | 현재 Dashboard는 외부 JSONL 파일 소비자 only — 서버를 따로 띄우고 metrics export까지 활성화해야만 차트가 채워짐. 실제 echo RTT를 직접 측정하는 손쉬운 진단 수단이 없고, "단순히 한 번 보낼 때 왕복 latency가 얼마인가?" 같은 가장 기본적인 질문을 답할 도구가 없다. |
| **Solution** | 신규 Echo Client 탭에서 host/port 입력 + Connect/Disconnect + Send Interval/Message 옵션을 제공. 내부적으로 `SampleClientSession`과 동일한 protobuf `EchoRequest(1001)` / `EchoResponse(1002)` 프레임을 `LibNetworks.BaseSessionServer`로 송수신하며 `Stopwatch` 기반 RTT 측정. 결과는 새 탭 자체 RTT 차트 + send/recv rate KPI로 시각화. 기존 JSONL Polling 탭은 변경 0. |
| **Function/UX Effect** | (1) Dashboard 단독으로 echo 왕복 RTT 진단 가능, (2) 서버 가동 상태/지연 실시간 감지, (3) MultiLineChartDrawable 재활용으로 일관된 시각, (4) 두 진단 모드(JSONL tail / Echo client)의 명확한 분리. |
| **Core Value** | "FastPort 서버가 살아있는가 / 얼마나 빠른가"를 외부 도구나 콘솔 client 없이 한 클릭(Connect)으로 답할 수 있는 진단 GUI. SampleClient(콘솔)의 GUI 버전. |

---

## Context Anchor

| Key | Value |
|-----|-------|
| **WHY** | Dashboard가 passive consumer(JSONL tail)에서 active probe(직접 echo)로 진화. SampleClient의 콘솔 진단을 GUI로 격상. |
| **WHO** | FastPort 서버 개발자/QA. 서버 가동 확인, latency 진단, 간단한 부하 sanity check 수행. |
| **RISK** | (a) `FastPortGameServerTemplate`이 `Exe` → 직접 ProjectReference 불가, EchoRequest/PacketIds 재사용 경로 설계 필요. (b) MAUI Catalyst sandbox/entitlement이 outbound TCP 허용 여부. (c) `BaseSessionServer` socket 라이프사이클을 MAUI UI 스레드에서 안전하게 시작/종료. |
| **SUCCESS** | 2-탭 UI + Echo Connect로 EchoResponse 수신 + RTT 차트 그려짐 + send/recv rate KPI 갱신 + JSONL 탭 회귀 0 + tests green. |
| **SCOPE** | 단일 connection echo client. 다중 connection / 부하 mode / message payload generator는 OOS. |

---

## 1. Overview

### 1.1 Purpose

Dashboard에 echo client 탭을 추가하여, JSONL 파일 없이도 서버 도달성과 latency를 직접 측정할 수 있게 한다. SampleClient 콘솔 앱이 제공하던 "한 번 EchoRequest 보내고 응답까지 RTT 측정" 기능을 GUI로 격상하고, **반복 전송 + 실시간 차트**로 확장한다.

### 1.2 Background

- `FastPortGameServerTemplate.SampleClient` cycle (이미 archive): 콘솔에서 1회 echo round-trip 검증. 1회로 끝남, GUI 없음.
- Dashboard cycle들: JSONL tail consumer로 발전. 단, 서버 + JSONL export 둘 다 필요.
- 두 도구의 간극을 메우는 위치 — Dashboard 내부 echo client.

### 1.3 Related Documents

- Archive: `docs/archive/2026-05/game-server-template-from-network-engine/` (FastPortGameServerTemplate 모태).
- 직전 dashboard cycles: `dashboard-chart-multi-rtt-overlay-v2`, `dashboard-chart-graphicsview-migration`.
- 참조 코드: `FastPortGameServerTemplate.SampleClient/Sessions/SampleClientSession.cs`, `SampleClientConnector.cs`, `EchoSignal.cs`.

---

## 2. Scope

### 2.1 In Scope

- [ ] MainPage 2-탭 구조 (`TabbedPage` 또는 `SegmentedControl`/`Toggle` — Design에서 결정).
- [ ] Tab 1: 기존 JSONL Polling — UI/로직 변경 0.
- [ ] Tab 2: Echo Client — host/port 입력, Message, Send Interval(ms), Connect/Disconnect, 상태 표시.
- [ ] EchoClient TCP session: `LibNetworks.BaseSessionServer` 기반, protobuf `EchoRequest`/`EchoResponse` 프레임, Send Interval마다 반복 송신.
- [ ] RTT 측정: `Stopwatch.GetTimestamp()` 페어, 매 echo round-trip마다 sample 추가.
- [ ] Tab 2 자체 RTT 차트 (기존 `MultiLineChartDrawable` single-series 재사용 또는 `LineChartDrawable` 재사용).
- [ ] Send/Recv KPI: send count, recv count, send rate (msg/s), recv rate (msg/s), total bytes sent/received, last RTT.
- [ ] `EchoRequest`/`EchoResponse` proto 타입 재사용 경로 확립 (FastPortGameServerTemplate Exe라서 직접 ref 불가 — Design 결정 사항).
- [ ] macOS Catalyst Debug + Windows 빌드 success.
- [ ] 신규 echo client 로직에 대한 단위 테스트 (RTT 계산 + KPI 누적 등 순수 함수 부분).

### 2.2 Out of Scope

- 다중 동시 connection (1개 connection만 지원).
- 부하 테스트 모드 (FastPortTestLoadRunner와 역할 분리).
- 사용자 정의 message payload generator / 가변 페이로드 크기.
- TLS / 인증.
- macOS Catalyst Release 빌드 검증 (memory note 따라 Debug only).
- 서버 자동 발견 (mDNS 등).

---

## 3. Requirements

### 3.1 Functional Requirements

| ID | Requirement | Priority | Status |
|----|-------------|----------|--------|
| FR-01 | 메인 화면이 2-탭(JSONL Polling / Echo Client)으로 분리되어 사용자가 전환 가능. | High | Pending |
| FR-02 | Echo Client 탭에 Host(string), Port(int), Message(string), Send Interval(ms) 입력 위젯과 Connect/Disconnect 버튼 표시. | High | Pending |
| FR-03 | Connect 시 host:port로 TCP 접속, EchoRequest 송신 → EchoResponse 수신 round-trip을 Send Interval마다 반복. | High | Pending |
| FR-04 | 각 round-trip의 RTT(ms)를 측정하여 차트에 추가. 차트는 최근 N(=600) sample window. | High | Pending |
| FR-05 | Send count / Recv count / Send rate / Recv rate / Total bytes sent / received / Last RTT / Avg RTT KPI 표시 (1초 단위 갱신). | High | Pending |
| FR-06 | Disconnect 시 socket close + 통계 freeze, 재Connect 시 통계 reset. | High | Pending |
| FR-07 | 연결 실패 / timeout / 서버 close 등 오류를 사용자에게 텍스트 라벨로 표시. | High | Pending |
| FR-08 | JSONL Polling 탭은 직전 cycle 동작 그대로 (회귀 0). | High | Pending |
| FR-09 | 32+신규 tests green. | High | Pending |

### 3.2 Non-Functional Requirements

| Category | Criteria | Measurement Method |
|----------|----------|-------------------|
| Stability | macOS Catalyst Debug에서 Connect/Disconnect 10회 반복 시 leak/crash 0 | 수동 + IPS 확인 |
| Performance | Send Interval ≥ 10ms 정상 동작 (100msg/s 안정) | 수동 KPI 관찰 |
| Compatibility | net10.0-maccatalyst + net10.0-windows10 모두 build success | `dotnet build` |
| Footprint | 신규 코드 ≤ 600 라인 | `wc -l` |

---

## 4. Success Criteria

### 4.1 Definition of Done

- [ ] FR-01~FR-09 모두 충족.
- [ ] EchoRequest/EchoResponse proto 재사용 경로 확립 (Design 단계 §2~§3 명시 + Do 단계 구현).
- [ ] macOS Catalyst Debug 실행 evidence (사용자 manual confirm) — Tab 2 Connect 시 EchoResponse 수신, RTT 차트 + KPI 갱신.
- [ ] JSONL 탭 회귀 검증 — 기존 `MainPage.xaml` Tab 1 동작 그대로.
- [ ] `dotnet test` ≥ 37 + 신규 echo client unit tests 통과.
- [ ] Design Ref 주석 신규/수정 코드에 부착.

### 4.2 Quality Criteria

- [ ] 빌드 warning 신규 0건.
- [ ] socket/cts 등 IDisposable 자원은 Disconnect/탭 전환 시 정상 정리 (leak 검증은 manual + IDisposable 패턴 명시).
- [ ] RTT 계산, send/recv counter 누적은 순수 함수로 분리해 단위 테스트 가능.

---

## 5. Risks and Mitigation

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| `FastPortGameServerTemplate` Exe 라서 `EchoRequest/PacketIds` 직접 참조 불가 | High | Resolved-by-precursor | **선행 cycle들 처리 확정 (2026-05-12)**: `template-contracts-extraction` (Contracts lib 도입) + `template-contracts-scaffold-fix` + 최종 `protos-shared-folder-revert-contracts`로 단순화 완료. 현재 상태: `template-projects/Protos/Sample.proto` (단순 폴더). Dashboard는 자체 `<Protobuf Include="..\template-projects\Protos\*.proto"/>` 한 줄로 EchoRequest/EchoResponse 자체 어셈블리 생성. PacketIds는 `<Compile Include Link>` 로 Template에서 공유 (Exe 참조 없음). |
| MAUI Catalyst sandbox가 outbound TCP 차단 | High | Low | macOS sandbox는 GUI 앱 outbound 기본 허용. entitlement 변경 없이 검증. 차단 시 `com.apple.security.network.client` 추가. |
| UI 스레드에서 socket I/O 시 freeze | Medium | Medium | `BaseSessionServer`는 async 패턴 사용 — UI 스레드 외부에서 작업. KPI/차트 update만 dispatcher로 marshal. |
| Send Interval 너무 짧을 때 echo response가 누적되어 RTT 측정이 잘못됨 (request/response 매칭 실패) | Medium | Medium | 1 connection × in-flight 1 echo가 기본. 다음 echo는 직전 response 도착 후 + interval. 단순한 ping-pong 패턴. (또는 sequence number로 매칭 — Design 결정) |
| `BaseSessionServer`가 client-side 사용에 적합한지 (`BaseSessionServer`는 "remote is server" 의미, SampleClient 패턴 동일) | Low | Low | SampleClient가 동일 패턴으로 동작 검증됨. |

---

## 6. Impact Analysis

### 6.1 Changed Resources

| Resource | Type | Change |
|----------|------|--------|
| `FastPortDashboard.Maui/MainPage.xaml` + `.xaml.cs` | XAML/C# | 2-탭 컨테이너로 재구성 (또는 새 `AppShell` 라우팅) |
| `FastPortDashboard.Maui/Views/JsonlPollingPage.xaml(.cs)` (신규 또는 추출) | XAML/C# | 기존 MainPage 콘텐츠 이동 |
| `FastPortDashboard.Maui/Views/EchoClientPage.xaml(.cs)` (신규) | XAML/C# | host/port/Message/Interval + Connect 버튼 + 차트 + KPI |
| `FastPortDashboard.Core/ViewModels/EchoClientViewModel.cs` (신규) | C# | Echo client state machine + KPI 누적 + RTT series |
| `FastPortDashboard.Core/EchoClient/EchoClientSession.cs` (신규) | C# | `BaseSessionServer` 상속, EchoRequest 송신 + EchoResponse 수신 |
| `FastPortDashboard.Core/EchoClient/EchoClientStats.cs` (신규) | C# | send/recv count, rate 계산 (1초 윈도우) — 순수 함수 |
| `FastPortGameServerTemplate.Contracts` (신규 lib, Design 결정 시) | C# net10.0 | EchoRequest/EchoResponse/PacketIds protobuf 격리 |
| `FastPortDashboard.Core.csproj` | csproj | `LibCommons`, `LibNetworks`, `FastPortGameServerTemplate.Contracts` (또는 link) 추가 |
| `FastPortDashboard.Maui.csproj` | csproj | (Core가 들고 있으면 추가 없음) |
| `tests-projects/FastPortDashboardTests/EchoClient/EchoClientStatsTests.cs` (신규) | C# | rate 계산 단위 테스트 |

### 6.2 Current Consumers

| Resource | Usage | Impact |
|----------|-------|--------|
| `MainPage.xaml.cs` | RTT/Throughput drawable wiring | Tab 1로 이동, wiring 보존 |
| `DashboardViewModel` | 기존 JSONL ViewModel | 변경 0 (Tab 1 binding 그대로) |
| `LineChartDrawable` | Throughput | Tab 1 단일 사용, 변경 0 |
| `MultiLineChartDrawable` | RTT P50/P95/P99 | Tab 1 단일 사용, 변경 0 |
| Echo 시각화는 새 `LineChartDrawable` 인스턴스 재사용 가능 | 단일 series RTT chart | new dependency, 코드 변경 0 |

### 6.3 Verification

- [ ] `MainPage`가 2-탭 구조로 바뀌어도 Tab 1 (JSONL) UI/binding 모두 직전 cycle과 동일하게 보임.
- [ ] EchoRequest proto 재사용 결정한 경로가 `FastPortGameServerTemplate.SampleClient`의 send 동작과 동일한 wire format 생성하는지 (수동 wireshark 또는 byte 비교 OOS — SampleClient를 가동해 서버에 보낸 후 동일 echo 수신되면 통과).
- [ ] Disconnect 후 즉시 재Connect 가능.

---

## 7. Architecture Considerations

### 7.1 Project Level Selection

| Level | Selected |
|-------|:--------:|
| Starter | ☐ |
| **Dynamic** | ☑ |
| Enterprise | ☐ |

### 7.2 Key Architectural Decisions (Plan 시점 가설, Design에서 확정)

| Decision | Options | Tentative | Rationale |
|----------|---------|-----------|-----------|
| Proto 타입 재사용 | 신규 Contracts lib / Compile Include link / Dashboard 재정의 | 신규 `FastPortGameServerTemplate.Contracts` (Design 검토) | 후속 cycle (다른 client) 확장에 가장 깔끔. 단 별도 cycle scope 침범 위험 → Design에서 trade-off. |
| 탭 UI 패턴 | `TabbedPage` (MAUI 기본 하단 탭) / `Shell` Flyout / In-page `Picker` 토글 | `TabbedPage` | 가장 자연스러운 desktop tab metaphor + 적은 코드 |
| Echo session 클래스 | `BaseSessionServer` 상속 신규 / SampleClient session 재사용 | 신규 `EchoClientSession : BaseSessionServer` | SampleClient는 1회 echo + signal로 종료 패턴, 본 cycle은 반복 echo이므로 다른 클래스 |
| 차트 컴포넌트 | 새 drawable / `LineChartDrawable` 재사용 | 기존 `LineChartDrawable` 재사용 | single-series, 직전 cycle에서 verified |
| KPI 계산 위치 | session 내부 / 별도 stats class | 별도 `EchoClientStats` (Core, 순수 함수) | 단위 테스트 가능, MAUI 의존 0 |

### 7.3 Clean Architecture

```
FastPortDashboard.Maui/
├── MainPage.xaml(.cs)              (MODIFIED → TabbedPage)
└── Views/
    ├── JsonlPollingPage.xaml(.cs)  (NEW, 기존 MainPage 콘텐츠 이전)
    └── EchoClientPage.xaml(.cs)    (NEW)

FastPortDashboard.Core/
├── ViewModels/
│   ├── DashboardViewModel.cs       (UNCHANGED)
│   └── EchoClientViewModel.cs      (NEW)
└── EchoClient/
    ├── EchoClientSession.cs        (NEW, BaseSessionServer 상속)
    └── EchoClientStats.cs          (NEW, pure helpers)

FastPortGameServerTemplate.Contracts/   (NEW, Design 결정 시)
└── Protocols/                            (proto + generated types)
```

---

## 8. Convention Prerequisites

### 8.1 Existing Conventions

- [x] `// Design Ref: §X` 주석 패턴
- [x] 순수 함수는 Core, MAUI 의존은 Maui 분리
- [x] CommunityToolkit.Mvvm source generator (`[ObservableProperty]`, `[RelayCommand]`)

### 8.2 New Conventions to Verify

| Category | To Define | Priority |
|----------|-----------|:--------:|
| Echo client session lifecycle | Connect → Connected → SendLoop → Disconnect 상태 전이 명시 | High |
| 통계 reset 시점 | 매 Connect마다 reset / 사용자 명시 reset 버튼 | Medium → Connect 시 reset (간단) |
| 탭 전환 시 echo session 정리 | Tab 전환 시 자동 Disconnect / 백그라운드 유지 | High → 일단 유지 (사용자가 Disconnect까지 명시) |

### 8.3 Environment Variables

해당 없음.

---

## 9. Next Steps

1. [ ] `/pdca design dashboard-echo-client-tab` — proto 재사용 경로 + 탭 UI 패턴 확정 (Option A/B/C).
2. [ ] (Design 결정 시) `FastPortGameServerTemplate.Contracts` 분리는 본 cycle scope 인지 별도 cycle인지 결정.
3. [ ] `/pdca do …` — 점진적 구현 (module-1 stats, module-2 session, module-3 view, module-4 tabbed integration).
4. [ ] `/pdca analyze` — JSONL 회귀 + tests green + grep 검증.
5. [ ] Manual: macOS Catalyst Debug → host/port 입력 → Connect → 차트 + KPI 동작 확인.
6. [ ] `/pdca report` + archive.

---

## Version History

| Version | Date | Changes | Author |
|---------|------|---------|--------|
| 0.1 | 2026-05-11 | Initial draft (2-탭 + Echo Client GUI 계획) | boinred |
