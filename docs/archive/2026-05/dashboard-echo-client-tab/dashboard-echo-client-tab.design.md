---
template: design
version: 1.3
feature: dashboard-echo-client-tab
date: 2026-05-13
author: boinred
project: FastPortSharp
---

# dashboard-echo-client-tab Design Document

> **Summary**: Dashboard에 2-탭 구조(`TabbedPage`)를 도입하고, 신규 Echo Client 탭에서 host/port 입력 → protobuf EchoRequest/EchoResponse 반복 송수신 → 자체 측정 RTT 차트 + KPI를 시각화한다. **Option C (Pragmatic Balance)** 채택.
>
> **Project**: FastPortSharp
> **Version**: 0.1.0
> **Author**: boinred
> **Date**: 2026-05-13
> **Status**: Draft
> **Planning Doc**: [dashboard-echo-client-tab.plan.md](../../01-plan/features/dashboard-echo-client-tab.plan.md)

---

## Context Anchor

> Plan §Context Anchor에서 복사. PRD 부재(이 cycle은 Plan-first).

| Key | Value |
|-----|-------|
| **WHY** | Dashboard가 passive consumer(JSONL tail)에서 active probe(직접 echo)로 진화. SampleClient의 콘솔 진단을 GUI로 격상. |
| **WHO** | FastPort 서버 개발자/QA. 서버 가동 확인, latency 진단, 간단한 부하 sanity check 수행. |
| **RISK** | (a) `FastPortGameServerTemplate` Exe → 직접 ProjectReference 불가 (선행 cycle로 해소: Protos 공유 폴더 + `<Compile Include Link>`). (b) MAUI Catalyst sandbox outbound TCP 허용 여부. (c) `BaseSessionServer` socket lifecycle UI 스레드 안전성. |
| **SUCCESS** | 2-탭 UI + Echo Connect로 EchoResponse 수신 + RTT 차트 그려짐 + send/recv rate KPI 갱신 + JSONL 탭 회귀 0 + tests green. |
| **SCOPE** | 단일 connection echo client. 다중 connection / 부하 mode / message payload generator는 OOS. |

---

## 1. Overview

### 1.1 Design Goals

1. JSONL Polling 탭은 회귀 0 — 기존 `MainPage` 콘텐츠를 `JsonlPollingPage`로 무손실 이전.
2. Echo Client는 `BaseSessionServer` 패턴(SampleClientSession 검증됨)을 그대로 활용하되, **lifecycle/통계/UI binding**을 분리해 단위 테스트 가능한 경계를 만든다.
3. proto/네트워크 라이브러리 재사용은 이미 결정된 경로(`<Protobuf Include="..\template-projects\Protos\*.proto"/>` + PacketIds `<Compile Include Link>`)로 일관 처리.
4. Plan footprint 제약(≤600 라인) 준수.

### 1.2 Design Principles

- **Single Responsibility**: `EchoClientSession`(I/O) / `EchoClientConnector`(lifecycle) / `EchoClientStats`(pure 누적 계산) / `EchoClientViewModel`(UI binding) 4-layer 분리.
- **Pure Core, Adapter Edge**: MAUI 의존(Dispatcher, FilePicker, Storage)은 ViewModel 표면까지만. Core/EchoClient는 plain .NET.
- **Reuse over Reinvent**: `BaseSessionServer`, `BaseMessageConnector`, `LineChartDrawable`, `ArrayPoolCircularBuffers` 모두 재사용.
- **Failure Loud**: 연결 실패/timeout/서버 close는 `EchoClientViewModel.ErrorMessage`로 즉시 노출. silent retry 없음.

### Pipeline References (if applicable)

| Phase | Document | Status |
|-------|----------|--------|
| Phase 1 | Schema Definition | N/A (proto는 기존) |
| Phase 2 | Coding Conventions | ✅ (`// Design Ref: §X` 주석, CommunityToolkit.Mvvm) |
| Phase 3 | Mockup | N/A |
| Phase 4 | API Spec | N/A (REST 아님, TCP/protobuf) |

---

## 2. Architecture Options (v1.7.0)

### 2.0 Architecture Comparison

| 차원 | A: Minimal | B: Clean | **C: Pragmatic** ⭐ |
|------|-----------|----------|---------------------|
| Echo session | session 내부 inline loop | Interface + impl + connector + sample store | `EchoClientSession` + `EchoClientConnector` 분리 |
| ViewModel | 단일 (state+kpi+chart inline) | 3개 분리 (Connection/Stats/Chart) | 단일 + Connector/Stats compose |
| Stats | VM inline | 3-class 분리 | `EchoClientStats` pure class |
| 신규 파일 | ~5 | ~12-14 | ~6-7 |
| 단위 테스트 | 어려움 | 광범위 mock | Stats만 pure-test |
| 구현 추정 | 0.5d | 2d | 1d |

### 2.1 Selected: Option C — Pragmatic Balance

**선택 사유**:
- Plan §7.2 잠정 결정과 정확히 일치(`EchoClientSession`/`EchoClientStats` 분리 + Connector lifecycle).
- 검증된 `SampleClientSession` 패턴(BaseSessionServer 상속, RequestSendMessage 사용) 그대로 차용 가능.
- Plan footprint ≤600라인을 안전하게 지키면서도 ViewModel과 도메인 경계는 명확.
- Option B의 추상화 비용(Interface 다층)은 본 cycle Scope("단일 connection echo")에 과함. 다중 connection/load mode가 추가되는 시점에 B로 진화 가능.

### 2.2 Component Diagram

```
┌─────────────────── FastPortDashboard.Maui ───────────────────┐
│  MainPage.xaml (TabbedPage)                                  │
│  ├── Tab 1: JsonlPollingPage (← 기존 MainPage 콘텐츠 이전)  │
│  └── Tab 2: EchoClientPage  (← NEW)                          │
│              ├ Entry: Host / Port / Message / Interval       │
│              ├ Button: Connect / Disconnect                  │
│              ├ Label: State / Error / KPI                    │
│              └ GraphicsView: RTT (LineChartDrawable)         │
│                                                              │
│        binds to (CommunityToolkit.Mvvm)                      │
│              ↓                                               │
└──────────────│─────────────────────────────────────────────────┘
               ↓
┌──────────────│────── FastPortDashboard.Core ─────────────────┐
│  ViewModels/EchoClientViewModel                              │
│      compose Connector + Stats, marshal events to UI thread  │
│              ↓                                               │
│  EchoClient/EchoClientConnector                              │
│      Connect(host,port) → Session 생성 / Disconnect          │
│              ↓                                               │
│  EchoClient/EchoClientSession : BaseSessionServer            │
│      OnConnected → schedule send loop                        │
│      OnReceived(EchoResponse) → record RTT → next send       │
│              ↓                                               │
│  EchoClient/EchoClientStats (pure)                           │
│      RecordRoundTrip(rttMs, bytesSent, bytesRecv)            │
│      Rate(now) → send/recv per second 윈도우                 │
└──────────────────────────────────────────────────────────────┘
```

### 2.3 Dependencies

| Component | Depends On | Reason |
|-----------|-----------|--------|
| `EchoClientSession` | `LibNetworks.BaseSessionServer`, `Sample.proto` generated types, `PacketIds` | I/O + proto 직렬화 |
| `EchoClientConnector` | `LibNetworks.BaseMessageConnector`, `IServerSessionFactory`(internal) | Connect 패턴 (SampleClientConnector 참고) |
| `EchoClientStats` | `System.Diagnostics.Stopwatch` only | pure, MAUI 0 |
| `EchoClientViewModel` | `CommunityToolkit.Mvvm`, `IDispatcher`(MAUI 의존) | UI marshal |

`Sample.proto` 포함은 `FastPortDashboard.Core.csproj`에 다음 한 줄 추가:
```xml
<ItemGroup>
  <Protobuf Include="..\template-projects\Protos\Sample.proto" GrpcServices="None" />
  <Compile Include="..\template-projects\FastPortGameServerTemplate\Protocols\PacketIds.cs">
    <Link>EchoClient\PacketIds.cs</Link>
  </Compile>
</ItemGroup>
```

---

## 3. Data Model

### 3.1 Entity Definition

| Type | Fields | Notes |
|------|--------|-------|
| `EchoClientOptions` (record) | `Host:string`, `Port:int`, `Message:string`, `SendIntervalMs:int` | ViewModel ↔ Connector 전달 |
| `EchoClientState` (enum) | `Disconnected`, `Connecting`, `Connected`, `Error` | UI 라벨/버튼 enable 제어 |
| `RttSample` (record struct) | `TimestampUtc:DateTime`, `RttMs:double` | RTT 차트 series |
| `EchoStatsSnapshot` (record) | `SendCount`, `RecvCount`, `SendRatePerSec`, `RecvRatePerSec`, `TotalBytesSent`, `TotalBytesRecv`, `LastRttMs`, `AvgRttMs` | 1초 주기 KPI 갱신 단위 |

### 3.2 Entity Relationships

```
EchoClientConnector ──owns──> EchoClientSession (0..1)
EchoClientSession ──appends──> EchoClientStats (pure store)
EchoClientStats ──snapshots──> EchoStatsSnapshot (per 1s timer)
EchoClientViewModel ──holds──> EchoClientOptions, EchoClientState, latest EchoStatsSnapshot, RttSample window(N=600)
```

### 3.3 Database Schema

N/A (네트워크 in-memory only).

---

## 4. API Specification

N/A — REST API 없음. 와이어 contract는 `Sample.proto`의 `EchoRequest(1001)` / `EchoResponse(1002)` packet id pair (이미 정의됨, 본 cycle은 변경 0).

---

## 5. UI/UX Design

### 5.1 Screen Layout

```
┌─ MainPage (TabbedPage) ──────────────────────────────────────┐
│  [ JSONL Polling ] [ Echo Client ]                           │
├──────────────────────────────────────────────────────────────┤
│ (Echo Client tab selected)                                   │
│                                                              │
│  Host:    [ 127.0.0.1                            ]           │
│  Port:    [ 7777     ]                                       │
│  Message: [ hello                                ]           │
│  Interval:[ 100  ] ms                                        │
│  State:    Disconnected     [ Connect ]  [ Disconnect ]      │
│  Error:                                                      │
│                                                              │
│  ┌──────────────────────────────────────────────────────┐    │
│  │  RTT (ms)                                            │    │
│  │  ╱╲     ╱╲                                           │    │
│  │ ╱  ╲   ╱  ╲___╱╲___                                  │    │
│  └──────────────────────────────────────────────────────┘    │
│                                                              │
│  Send: 1532  | Recv: 1531  | Send/s: 9.9  | Recv/s: 9.9      │
│  TxBytes: 158K | RxBytes: 159K | LastRTT: 1.3 | AvgRTT: 1.6  │
└──────────────────────────────────────────────────────────────┘
```

### 5.2 User Flow

```
1. 사용자가 Echo Client 탭 선택
2. Host/Port/Message/Interval 입력 (default: 127.0.0.1 / 7777 / "hello" / 100)
3. [Connect] 클릭
   → State: Connecting → Connected (성공) 또는 Error (실패)
   → 즉시 EchoRequest 송신 시작, Interval 마다 반복
4. RTT 차트 + KPI 1초마다 갱신
5. [Disconnect] 클릭 → socket close → 통계 freeze (값 유지)
6. 재 [Connect] → 통계 reset → 새 series 시작
7. 탭 전환(Tab 1 ↔ Tab 2): echo session 유지(Disconnect 명시 전까지)
```

### 5.3 Component List

| Component | Type | Binding |
|-----------|------|---------|
| Host/Port/Message/Interval `Entry` | MAUI | `EchoClientViewModel.{Host,Port,Message,SendIntervalMs}` (TwoWay) |
| State `Label` | MAUI | `EchoClientViewModel.StateText` (OneWay) |
| Error `Label` | MAUI | `EchoClientViewModel.ErrorMessage` (OneWay) |
| Connect/Disconnect `Button` | MAUI | `EchoClientViewModel.ConnectCommand`, `DisconnectCommand` |
| RTT `GraphicsView` | MAUI | `EchoClientViewModel.RttSeries` → `LineChartDrawable.Values` |
| KPI `Label` ×8 | MAUI | `EchoClientViewModel.Snapshot.*` (OneWay) |

### 5.4 Page UI Checklist

#### Echo Client Page
- [ ] Host/Port/Message/Interval 모두 default 값으로 prefill.
- [ ] Connect 클릭 시 입력 필드 비활성화, Disconnect 활성화.
- [ ] Disconnect 후 입력 필드 재활성화, 통계 freeze.
- [ ] Error 시 메시지 라벨에 표시, Connect 버튼 재활성화.
- [ ] RTT 차트 1초마다 invalidate, 최근 600 sample window.
- [ ] KPI 1초마다 갱신 (snapshot 단위).

#### JSONL Polling Page (회귀 검증 only)
- [ ] 기존 `MainPage` 모든 동작 보존 (Browse → tail → 차트 → KPI).

---

## 6. Error Handling

### 6.1 Error Code Definition

| Code | Cause | UI Message | Recovery |
|------|-------|------------|----------|
| `EC-CONNECT-001` | DNS/host resolve 실패 | "Cannot resolve host: {host}" | 사용자 host 수정 후 재시도 |
| `EC-CONNECT-002` | TCP connect refused/timeout | "Connection refused or timed out: {host}:{port}" | 서버 가동 확인 후 재시도 |
| `EC-RUNTIME-001` | server가 connection close | "Server closed the connection" | Disconnect 상태, 재 Connect로 복구 |
| `EC-PROTO-001` | EchoResponse 파싱 실패 | "Unexpected response packet" | Disconnect 후 서버/proto 버전 확인 |

### 6.2 Error Response Format

ViewModel level:
```csharp
[ObservableProperty] string? errorMessage;
[ObservableProperty] EchoClientState state;
// On error: state = Error, errorMessage = "{code}: {detail}"
```

---

## 7. Security Considerations

- **No TLS** (OOS, Plan §2.2). 로컬/사내망 진단용. Production target endpoint는 별도 cycle.
- **MAUI Catalyst sandbox**: outbound TCP는 default 허용. 차단 시 `Platforms/MacCatalyst/Entitlements.plist`에 `com.apple.security.network.client` 추가(Risk §M3).
- **No 사용자 인증/세션 토큰**: echo는 stateless. server측에서 client identity 식별 안 함.
- **Resource leak**: socket/CancellationTokenSource는 Disconnect 시 `Dispose`. 탭 전환 시 유지(사용자 명시 Disconnect까지) — `App.OnSleep`에서도 유지(MAUI는 백그라운드에서 disposed 보장 안 함, 의도적).

---

## 8. Test Plan (v2.3.0)

### 8.1 Test Scope

본 cycle은 **MAUI UI 자동 테스트가 어려운 환경**(macOS Catalyst Debug + Windows). L1(API/contract)은 protobuf wire-format으로 대체, L2/L3는 manual evidence + Stats pure unit test로 대체.

### 8.2 L1: Wire-format / Unit Test Scenarios

| ID | Scenario | Verification |
|----|----------|--------------|
| L1-01 | `EchoClientStats.RecordRoundTrip` 누적 정확성 | xUnit, send/recv count + bytes 정확 |
| L1-02 | `EchoClientStats.Snapshot(now)` 1초 윈도우 rate 계산 | xUnit, 모의 timestamp injection으로 rate 검증 |
| L1-03 | `EchoClientStats.AvgRttMs` 누적 평균 | xUnit, EMA 또는 단순 평균(둘 다 허용) |
| L1-04 | `EchoClientConnector` state transition | xUnit, Disconnected → Connecting → Connected → Disconnected 순서 |
| L1-05 | EchoRequest 직렬화 wire-format | byte 비교(SampleClientSession 생성 payload와 동일) |

### 8.3 L2: UI Action Test Scenarios

| ID | Scenario | Verification |
|----|----------|--------------|
| L2-01 | Connect 클릭 → State=Connected | Manual evidence (사용자 screenshot/log) |
| L2-02 | Connect → 차트에 sample 1개 이상 그려짐 | Manual evidence |
| L2-03 | Disconnect → 통계 freeze, 입력 재활성화 | Manual evidence |
| L2-04 | 잘못된 host → Error 라벨 표시 | Manual evidence |

### 8.4 L3: E2E Scenario Test Scenarios

| ID | Scenario | Verification |
|----|----------|--------------|
| L3-01 | FastPortGameServerTemplate 가동 → Dashboard echo 탭 Connect → RTT 차트 + KPI 10초 이상 정상 | Manual, screenshot + 로그 |
| L3-02 | JSONL 탭 회귀 (직전 cycle 동작 100% 보존) | Manual, screenshot 비교 |
| L3-03 | Connect/Disconnect 10회 반복 → leak/crash 0 | Manual, IPS log 없음 확인 |

### 8.5 Seed Data Requirements

- 로컬에서 `FastPortGameServerTemplate` 가동 (default port 7777).
- 별도 seed DB 없음 (네트워크 echo only).

---

## 9. Clean Architecture

```
FastPortDashboard.Core/
├── EchoClient/                              (NEW)
│   ├── EchoClientOptions.cs                 record (host, port, message, intervalMs)
│   ├── EchoClientState.cs                   enum
│   ├── EchoStatsSnapshot.cs                 record
│   ├── RttSample.cs                         record struct
│   ├── EchoClientStats.cs                   pure 누적/rate/snapshot
│   ├── EchoClientSession.cs                 BaseSessionServer 상속
│   ├── EchoClientSessionFactory.cs          IServerSessionFactory 구현
│   └── EchoClientConnector.cs               BaseMessageConnector 상속 (SampleClientConnector 패턴)
├── ViewModels/
│   ├── DashboardViewModel.cs                (UNCHANGED)
│   └── EchoClientViewModel.cs               (NEW, compose + UI marshal)
└── FastPortDashboard.Core.csproj            (MODIFIED: <Protobuf Include> + <Compile Include Link>)

FastPortDashboard.Maui/
├── MainPage.xaml(.cs)                       (MODIFIED → TabbedPage shell)
├── Views/
│   ├── JsonlPollingPage.xaml(.cs)           (NEW, 기존 MainPage 콘텐츠 이전)
│   ├── EchoClientPage.xaml(.cs)             (NEW, form + chart + KPI)
│   ├── LineChartDrawable.cs                 (UNCHANGED, RTT용 재사용)
│   └── MultiLineChartDrawable.cs            (UNCHANGED, JSONL 탭에서만 사용)
└── MauiProgram.cs                            (MODIFIED: EchoClient DI 등록)

tests-projects/FastPortDashboardTests/EchoClient/   (NEW)
├── EchoClientStatsTests.cs                  L1-01/02/03
└── EchoClientConnectorTests.cs              L1-04 (state machine)
```

---

## 10. Migration Strategy

| Step | Action | Risk |
|------|--------|------|
| 1 | `MainPage.xaml.cs`의 RTT/Throughput drawable wiring + FilePicker 코드를 `Views/JsonlPollingPage.xaml(.cs)`로 **그대로** 이전 | Low — 코드 이동만, 의미 변화 0 |
| 2 | `MainPage.xaml`을 `TabbedPage`로 재구성, child 2개: `JsonlPollingPage`, `EchoClientPage` | Low |
| 3 | `FastPortDashboard.Core.csproj`에 `<Protobuf Include>` + `<Compile Include Link>` 추가 → 빌드 검증(EchoRequest/EchoResponse/PacketIds 컴파일 가능) | Medium — proto 빌드 실패 가능, 격리해서 먼저 검증 |
| 4 | `EchoClientStats` 신규 + 단위 테스트 추가 | Low |
| 5 | `EchoClientSession`/`Factory`/`Connector` 신규 (SampleClient 코드 참고) | Medium — `BaseSessionServer` 상속, OnReceived 정상 동작 검증 필요 |
| 6 | `EchoClientViewModel` + DI 등록 + `EchoClientPage.xaml(.cs)` 신규 | Low |
| 7 | Manual evidence (macOS Catalyst Debug): Connect → RTT 차트 + KPI 정상, JSONL 탭 회귀 0 | High validation point |
| 8 | `dotnet test` 그린 확인 (기존 32 + 신규 ~5) | Low |

**Rollback**: 각 module이 isolated commit. 문제 발생 시 module 단위 revert. JSONL 탭은 step 1 이전 상태(`MainPage` 원본) 즉시 복구 가능 — `git revert` 1회.

---

## 11. Implementation Guide

### 11.1 Recommended Implementation Order

1. **Module 0 — Plan footprint 검증**: 신규 코드 추정 라인 수 확인 (≤600 라인 목표).
2. **Module 1 — Stats pure layer** (테스트 가능 코어).
3. **Module 2 — Session/Factory/Connector** (네트워크 코어, SampleClient 패턴 차용).
4. **Module 3 — ViewModel** (UI 의존 layer 표면).
5. **Module 4 — Page UI** (Echo Client 페이지 XAML/code-behind).
6. **Module 5 — Tab integration** (MainPage TabbedPage + JsonlPollingPage 이전).
7. **Module 6 — Manual evidence + 회귀 검증**.

### 11.2 Key Implementation Notes

- **Send loop 구동**: `EchoClientSession` 내부에서 `PeriodicTimer(TimeSpan.FromMilliseconds(intervalMs))` + `CancellationToken`. 매 tick에 `RequestSendMessage`. 단, **in-flight 1 echo 보장** — 다음 tick은 이전 response 도착 후에만 dispatch (Plan §5 Risk: "Send Interval 너무 짧을 때 echo response 누적").
- **RTT 측정**: `Stopwatch.GetTimestamp()` 페어. `OnConnected`/`OnReceived`에서 `m_SendTimestamp` 갱신 + `EchoClientStats.RecordRoundTrip` 호출.
- **UI 스레드 marshal**: `EchoClientSession`은 `Action<RttSample>` 콜백을 받음. `EchoClientViewModel`이 콜백을 `IDispatcher.Dispatch(() => ...)`로 wrap해서 전달.
- **DI 등록** (`MauiProgram.cs`):
  ```csharp
  builder.Services.AddSingleton<EchoClientStats>();
  builder.Services.AddTransient<EchoClientSessionFactory>();
  builder.Services.AddSingleton<EchoClientConnector>();
  builder.Services.AddSingleton<EchoClientViewModel>();
  builder.Services.AddSingleton<EchoClientPage>();
  ```
- **`// Design Ref` 주석**: 모든 신규 파일 top에 `// Design Ref: §9 — EchoClient Clean Architecture`. 핵심 로직(send loop, RTT 측정)에는 `// Plan SC: FR-04 RTT 측정 / FR-05 KPI 갱신`.

### 11.3 Session Guide (Module Map)

> `/pdca do dashboard-echo-client-tab --scope module-N` 으로 점진적 구현.

| Module Key | Module | Files | Est. Lines | Depends On |
|-----------|--------|-------|------------|-----------|
| `module-1` | Stats pure layer | `EchoClientStats.cs`, `EchoStatsSnapshot.cs`, `RttSample.cs`, `EchoClientOptions.cs`, `EchoClientState.cs`, `EchoClientStatsTests.cs` | ~150 | — |
| `module-2` | Session + Factory + Connector | `EchoClientSession.cs`, `EchoClientSessionFactory.cs`, `EchoClientConnector.cs`, `EchoClientConnectorTests.cs` | ~180 | module-1, proto 빌드 |
| `module-3` | ViewModel | `EchoClientViewModel.cs` | ~120 | module-1, module-2 |
| `module-4` | Echo Client Page | `EchoClientPage.xaml`, `EchoClientPage.xaml.cs` | ~110 | module-3 |
| `module-5` | Tab integration | `MainPage.xaml(.cs)` rewrite (TabbedPage), `JsonlPollingPage.xaml(.cs)` (move), `MauiProgram.cs` DI | ~80 (net 증가) | module-4 |
| `module-6` | Manual evidence | (no code) | 0 | module-5 |

**Recommended Session Plan**:
- **Session 1**: module-1 (pure, 가장 안전, 테스트 함께)
- **Session 2**: module-2 (네트워크, proto 빌드 검증 포함)
- **Session 3**: module-3 + module-4 (UI 의존 layer 한 번에)
- **Session 4**: module-5 + module-6 (integration + 회귀 검증)

총 추정: 4 세션, 약 1일 작업.

---

## 12. Open Questions (Deferred to Do Phase)

| ID | Question | Resolution Plan |
|----|----------|-----------------|
| OQ-1 | `AvgRttMs`를 EMA로 할지 단순 누적 평균으로 할지 | Do 단계 module-1 구현 시 결정. 단순 평균이 기본(테스트 용이). |
| OQ-2 | Connect 실패 후 자동 재시도 여부 | OOS 유지 — 사용자가 명시적으로 다시 Connect 클릭. |
| OQ-3 | macOS Catalyst entitlement이 outbound TCP 차단하는지 | Module-7 manual evidence 단계에서 확인. 차단 시 `Entitlements.plist` 추가 commit. |

---

## Version History

| Version | Date | Changes | Author |
|---------|------|---------|--------|
| 0.1 | 2026-05-13 | Initial draft (Option C — Pragmatic Balance, 6 module session plan) | boinred |
