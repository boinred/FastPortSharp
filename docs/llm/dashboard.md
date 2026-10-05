# 대시보드 (MAUI·JSONL 폴링·차트·ViewModel·Echo 클라이언트 UI·Dashboard 솔루션·CI)

서버 텔레메트리 JSONL을 실시간으로 그리는 데스크톱 앱이다. 로직은 MAUI 의존이 없는 `FastPortDashboard.Core`(net10.0)에 있고, `FastPortDashboard.Maui`는 화면만 맡는다. 앱은 탭 두 개다: JSONL Polling(서버·클라이언트 지표 차트)과 Echo Client(직접 접속해 RTT 측정).

**이 문서를 읽는 경우**: 대시보드 KPI·차트 추가, `JsonlPollingAdapter` 동작(offset·truncate·파일 공유 모드), Mock 데이터, `DashboardViewModel`·`EchoClientViewModel` 바인딩, Echo 클라이언트 연결 상태 머신, `FastPortSharp.Dashboard.sln` 빌드, MAUI workload, `dashboard.yml` CI 실패.

**다른 문서로 가는 경우**: JSONL을 만드는 쪽(SmokeServer 텔레메트리, LoadRunner, `ObservedMetricsSnapshot` 계약) → [load-testing.md](load-testing.md). Echo 클라이언트가 붙는 서버와 `PacketIds` proto → [game-server-template.md](game-server-template.md). `BaseMessageConnector`·`BaseSessionServer` 엔진 동작 → [listener-connector.md](listener-connector.md), [session.md](session.md). 메인 솔루션 빌드·필수 체크 → [platform.md](platform.md).

## 핵심 규칙

- **Core에는 MAUI 타입을 넣지 않는다.** ViewModel·Adapter·차트 수학·Echo 클라이언트는 모두 `FastPortDashboard.Core`에 있고, 그래서 `FastPortDashboardTests`가 MAUI 없이 돈다. `IDrawable`·`ICanvas`·`Dispatcher` 같은 MAUI 의존은 `FastPortDashboard.Maui/Views/`에만 둔다.
- Core의 `RootNamespace`는 `FastPortDashboard.Maui`다. 그래서 Core 타입의 namespace도 `FastPortDashboard.Maui.Adapters`, `FastPortDashboard.Maui.ViewModels`, `FastPortDashboard.Maui.EchoClient`다. 프로젝트 이름으로 namespace를 추측하지 않는다.
- **데이터 계약은 `LibTestTelemetry`의 `ObservedMetricsSnapshot`이다.** `JsonlPollingAdapter`는 한 줄씩 `ObservedMetricsJson.SerializerOptions`(camelCase)로 역직렬화한다. 깨진 줄은 건너뛴다. 계약 필드를 바꾸면 SmokeServer·LoadRunner·LoadValidation과 함께 맞춘다([load-testing.md](load-testing.md)).
- `DashboardViewModel.ApplySnapshot`은 `serverObserved`가 있으면 KPI(`CurrentSessions`, `TotalAcceptedSessions`, `TotalSentBytes`, `PendingSendRequests`, `SendBufferBytes`, `LastUpdate`)와 `ThroughputSeries`(`SentBytesPerSecond`)를 갱신한다. `clientObserved`가 있으면 `ClientRttSeries`(P50/P95/P99)를 더한다. 두 시리즈 모두 최대 600점(`MaxChartPoints`)만 유지한다.
- SmokeServer JSONL에는 `serverObserved`만 있다. RTT 차트를 채우려면 LoadRunner JSONL, LoadValidation의 `*.combined.metrics.jsonl`, 또는 Mock을 연다.
- **`JsonlPollingAdapter`는 파일을 `FileShare.ReadWrite | FileShare.Delete`로 연다.** 생산자(`ServerTelemetryExportBackgroundService`)가 쓰기 핸들을 잡고 있어도 Windows에서 `IOException`이 반복되지 않게 하려는 것이다. 읽을 위치(offset)는 yield 전에 확정한다. 한 번의 폴링은 파일을 연 시점의 길이까지만 읽고, `\n`으로 끝난 완성된 줄만 처리한다. offset은 마지막 완성 줄의 끝으로 옮긴다. 생산자가 쓰는 중인 마지막 줄은 다음 폴링에서 다시 읽으므로 줄 누락·중복이 없다(줄바꿈 없이 끝나는 줄은 영원히 전달되지 않으므로 생산자는 `WriteLine`을 쓴다). 줄 앞의 UTF-8 BOM과 `\r`은 제거한다. 파일이 offset보다 짧아지면 처음부터 다시 읽는다. `IOException`은 다음 폴링에서 재시도한다.
- `IPollingAdapter.StreamAsync`가 소스 추상화다. 구현은 `JsonlPollingAdapter`와 `MockPollingAdapter`(seed 기반 random walk, 서버·클라이언트 스냅샷 모두 생성) 두 개다. 새 소스(HTTP 등)는 이 인터페이스로 추가한다.
- **Echo 클라이언트는 게임 서버 템플릿용이다.** Core가 `template-projects/Protos/*.proto`를 `<Protobuf Include>`로 직접 생성해 `PacketIds.EchoRequest`(1001)/`EchoResponse`(1002)를 쓴다. 기본 포트도 템플릿의 `ListenPort`와 같은 7777이다. SmokeServer(6628, `ProtocolId.Tests` = 1)에 붙이면 서버가 packetId 1001을 protocol error로 세고 응답하지 않아 RTT가 쌓이지 않는다. 응답 packetId가 1002가 아니면 클라이언트는 `EC-PROTO-001` 오류를 낸다.
- `EchoClientConnector`의 상태 전이(`TryBeginConnect`, `NotifyConnected`, `NotifyError`, `NotifyDisconnected`)는 소켓 없이 테스트할 수 있게 분리돼 있다. 실제 연결은 `StartConnect`가 `BaseMessageConnector` + `EchoClientSessionFactory`로 한다.
- UI 스레드 처리: `EchoClientViewModel`은 생성자로 받은 `postToUi`(`EchoClientPage`가 `Dispatcher.Dispatch`로 감쌈)로 모든 컬렉션·상태 변경을 넘긴다. KPI는 `System.Threading.Timer`로 1초마다 `EchoClientStats.Snapshot`을 가져온다. `DashboardViewModel`은 dispatcher 없이 `[RelayCommand]` 비동기 흐름 안에서 갱신한다.
- 차트는 SkiaSharp/Microcharts를 쓰지 않는다(macOS 26 crash 때문에 제거). `Microsoft.Maui.Graphics`의 `GraphicsView` + `LineChartDrawable`/`MultiLineChartDrawable`이 그리고, 범위 계산은 Core의 `LineChartMath`가 한다.

## 파일·타입

| 파일 | 타입·심볼 | 내용 |
|---|---|---|
| `FastPortDashboard.Core/FastPortDashboard.Core.csproj` | — | net10.0, `CommunityToolkit.Mvvm`, `Google.Protobuf`·`Grpc.Tools`, 템플릿 proto 생성, `LibTestTelemetry`·`LibCommons`·`LibNetworks` 참조 |
| `FastPortDashboard.Core/Adapters/IPollingAdapter.cs` | `IPollingAdapter` | `IAsyncEnumerable<ObservedMetricsSnapshot> StreamAsync` |
| `FastPortDashboard.Core/Adapters/JsonlPollingAdapter.cs` | `JsonlPollingAdapter` | JSONL tail 폴링(기본 1초). offset·truncate 처리 |
| `FastPortDashboard.Core/Adapters/MockPollingAdapter.cs` | `MockPollingAdapter` | 가짜 서버·클라이언트 스냅샷 |
| `FastPortDashboard.Core/Charts/LineChartMath.cs` | `LineChartMath` | `ComputeRange`, `ComputeRangeMulti`, `ComputeStepX`. 빈 값·동일 값 보정 |
| `FastPortDashboard.Core/ViewModels/DashboardViewModel.cs` | `DashboardViewModel` | KPI `[ObservableProperty]`, `ThroughputSeries`, `ClientRttSeries`, `ConnectCommand`/`DisconnectCommand`, `PumpAsync`·`ApplySnapshot`(테스트 진입점) |
| `FastPortDashboard.Core/ViewModels/PollingState.cs`, `TimedDoublePoint.cs`, `TimedRttPoint.cs` | `PollingState`, `TimedDoublePoint`, `TimedRttPoint` | 상태 enum(Idle/Polling/Disconnected/Error)과 차트 점 |
| `FastPortDashboard.Core/ViewModels/EchoClientViewModel.cs` | `EchoClientViewModel` | `Host`·`Port`·`Message`·`SendIntervalMs` 입력, `RttSeries`(최대 600), `Snapshot` KPI |
| `FastPortDashboard.Core/EchoClient/EchoClientConnector.cs` | `EchoClientConnector` | 연결 상태 머신, `StartConnect`, `RequestDisconnect`, `StateChanged` |
| `FastPortDashboard.Core/EchoClient/EchoClientSession.cs`, `EchoClientSessionFactory.cs` | `EchoClientSession : BaseSessionServer`, `EchoClientSessionFactory : IServerSessionFactory` | 연결 후 `SendIntervalMs` 간격 Echo 송신, 응답으로 RTT 계산 |
| `FastPortDashboard.Core/EchoClient/EchoClientStats.cs`, `EchoClientModels.cs` | `EchoClientStats`, `EchoClientOptions`, `EchoClientState`, `RttSample`, `EchoStatsSnapshot` | 송수신 카운터·1초 창 rate·평균 RTT |
| `FastPortDashboard.Maui/AppShell.xaml` | `AppShell` | `TabBar`: `JsonlPollingPage`, `EchoClientPage` |
| `FastPortDashboard.Maui/Views/JsonlPollingPage.xaml(.cs)` | `JsonlPollingPage` | `DashboardViewModel` 바인딩, 파일 선택(`FilePicker`), RTT·throughput 차트 |
| `FastPortDashboard.Maui/Views/EchoClientPage.xaml(.cs)` | `EchoClientPage` | `EchoClientViewModel` 생성, dispatcher 연결, RTT 차트 |
| `FastPortDashboard.Maui/Views/LineChartDrawable.cs`, `MultiLineChartDrawable.cs`, `LineChartSeries.cs` | `LineChartDrawable`, `MultiLineChartDrawable`, `LineChartSeries` | `IDrawable` 라인 차트 |
| `FastPortDashboard.Maui/MainPage.xaml(.cs)` | `MainPage` | 라우팅에서 쓰지 않는 호환용 잔존 페이지 |
| `FastPortDashboard.Maui/FastPortDashboard.Maui.csproj` | — | TFM `net10.0-maccatalyst`(Windows 호스트면 `net10.0-windows10.0.19041.0` 추가), `Microsoft.Maui.Controls` 10.0.20, Catalyst AOT 끔 |
| `FastPortSharp.Dashboard.sln` | — | Maui, Core, `FastPortDashboardTests`, `LibTestTelemetry`만 포함 |
| `.github/workflows/dashboard.yml` | job `dashboard (${{ matrix.os }})` | macOS·Windows에서 restore → build → test |

## 작업별 시작점

| 하려는 작업 | 고칠 곳 | 같이 확인할 것 |
|---|---|---|
| 대시보드 KPI 추가 | `DashboardViewModel`에 `[ObservableProperty]` + `ApplySnapshot` 대입 → `JsonlPollingPage.xaml` 바인딩 | 지표가 계약에 없으면 먼저 [load-testing.md](load-testing.md)의 지표 추가, `MockPollingAdapter`, `DashboardViewModelTests` |
| 대시보드 차트 추가 | `DashboardViewModel`에 `ObservableCollection` 시리즈 + 600점 trim → `JsonlPollingPage.xaml`에 `GraphicsView` → `.xaml.cs`에서 drawable 연결·`CollectionChanged` 구독 | 여러 선이면 `MultiLineChartDrawable`/`LineChartSeries`, 범위 수학은 `LineChartMath` + `LineChartMathTests` |
| JSONL 읽기 문제(누락·중복·IOException) | `JsonlPollingAdapter.ReadNewSnapshotsAsync` | 생산자의 `FileShare`·flush(`ServerTelemetryExportBackgroundService`), `JsonlPollingAdapterTests` |
| 새 데이터 소스 | `IPollingAdapter` 구현 → `DashboardViewModel.StartAsync`의 선택 분기 | `UseMock`/`FilePath` 입력 UI |
| Echo 클라이언트 프로토콜 변경 | `EchoClientSession`(송신·`OnReceived`), `template-projects/Protos/` | 템플릿·SampleClient 영향([game-server-template.md](game-server-template.md)), scaffold golden 갱신 |
| Echo 연결 상태·오류 표시 | `EchoClientConnector`, `EchoClientViewModel.OnConnectorStateChanged` | `EchoClientConnectorTests` |
| Echo KPI 계산 | `EchoClientStats.Snapshot` | `EchoClientStatsTests` |
| MAUI CI 실패 | `.github/workflows/dashboard.yml` | workload 버전·Xcode, restore의 `-p:Configuration=Release` |

## 실행·테스트

```bash
# MAUI 없이 Core 테스트 (Linux 포함 어디서나)
dotnet test tests-projects/FastPortDashboardTests -c Release

# 전체 대시보드 솔루션 (macOS/Windows + MAUI workload)
dotnet workload install maui --version 10.0.401
dotnet restore FastPortSharp.Dashboard.sln -p:Configuration=Release
dotnet build FastPortSharp.Dashboard.sln -c Release --no-restore
dotnet test FastPortSharp.Dashboard.sln -c Release --no-build

# macOS Catalyst 실행
dotnet build FastPortDashboard.Maui/FastPortDashboard.Maui.csproj -c Release -f net10.0-maccatalyst -t:Run
```

- 실데이터 확인: SmokeServer를 `--Telemetry:Output=<경로>`로 띄운 뒤([load-testing.md](load-testing.md)) JSONL Polling 탭에서 Mock을 끄고 그 파일을 고른다. Echo 탭은 게임 서버 템플릿을 띄우고 7777에 붙는다.
- 테스트(`tests-projects/FastPortDashboardTests/`): `Adapters/JsonlPollingAdapterTests.cs`(offset 유지, truncate, 깨진 줄, 쓰는 중인 마지막 줄, 동시 쓰기), `Adapters/MockPollingAdapterTests.cs`, `Charts/LineChartMathTests.cs`, `EchoClient/EchoClientConnectorTests.cs`, `EchoClient/EchoClientStatsTests.cs`, `ViewModels/DashboardViewModelTests.cs`, `E2E/MockE2ETests.cs`(Mock → ViewModel 전체 흐름). `EchoClientViewModel` 전용 테스트는 없다.
- 시간 의존 테스트는 고정 시간 창(예: 250ms 안에 N개)으로 단언하지 않는다. CI macOS runner는 느려서 흔들린다. 받은 샘플 수(`MockE2ETests`의 `TakeAdapter`, `CancelAfterAdapter`)나 소비 순서(`JsonlPollingAdapterTests`의 enumerator 직접 진행)로 진행하고, 시간은 넉넉한 안전 상한으로만 쓴다.
- CI `dashboard.yml`: `main`·`builds/release`의 push/PR 중 `FastPortDashboard.Maui/**`, `FastPortDashboard.Core/**`, `tests-projects/FastPortDashboardTests/**`, `tests-projects/LibTestTelemetry/**`, `LibCommons/**`, `LibNetworks/**`, `template-projects/Protos/**`, `FastPortSharp.Dashboard.sln`, 워크플로 자신이 바뀔 때와 `workflow_dispatch`로 돈다. job 이름은 `dashboard (macos-latest)`, `dashboard (windows-latest)`이다. Linux는 MAUI TFM을 빌드할 수 없어 matrix에서 뺐다.

## 주의

- 솔루션 파일 이름은 `FastPortSharp.Dashboard.sln`이다. `FastPortSharp.sln`에는 대시보드 프로젝트가 없어서 `build.yml`과 `dotnet test FastPortSharp.sln`은 대시보드 테스트를 돌리지 않는다.
- `FastPortSharp.Dashboard.sln`에 `LibCommons`/`LibNetworks`가 없다. 솔루션에 없는 ProjectReference는 솔루션 구성을 물려받지 않아 `-c Release`로 빌드해도 Debug 구성으로 빌드된다.
- MAUI는 Release에서만 maccatalyst RuntimeIdentifiers(x64/arm64)를 추가한다. `--no-restore` Release 빌드 전 restore에도 `-p:Configuration=Release`를 줘야 NETSDK1047이 나지 않는다.
- MAUI workload는 `--version 10.0.401`로 고정한다. 최신 workload set은 runner의 Xcode보다 높은 MacCatalyst SDK를 요구해 빌드가 깨질 수 있다. 올릴 때는 `Microsoft.Maui.Controls` 버전과 runner Xcode를 함께 확인한다.
- macOS Catalyst Release는 AOT에서 시작 시 SIGABRT가 나서 `RunAOTCompilation=false`, `MtouchInterpreter=all`로 둔다.
- `Platforms/` 아래 Android·iOS·Tizen 폴더가 남아 있지만 TFM에 없어 빌드되지 않는다.
- `FastPortDashboard.Maui/README.md`는 일부 오래됐다(ViewModel 위치를 Maui로 적음, RTT 차트 미포함이라고 적음, SmokeServer가 기본으로 JSONL을 쓴다고 적음). 코드가 기준이다. SmokeServer는 `Telemetry:Output`을 줘야 JSONL을 쓴다.
- 코드 주석의 `Design Ref: §...`는 저장소에 없는 과거 설계 문서 참조다. 용어는 [glossary.md](glossary.md), 전체 지도는 [README.md](README.md), 샘플 서버·클라이언트는 [sample-apps.md](sample-apps.md).
