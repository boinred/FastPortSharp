# 부하 테스트 (스모크 서버·텔레메트리·JSONL·부하 생성기·단계별 검증·임계값·클라우드 부하 검증·idle 세션 정리)

엔진의 실제 TCP 경로를 계측하고 검증하는 도구 묶음이다. 스모크 서버가 서버 지표를 JSONL로 내보내고, 부하 생성기(LoadRunner)가 클라이언트 지표를 JSONL로 쓰며, 검증 하네스(LoadValidation)가 둘을 합쳐 단계별로 통과·실패를 판정한다.

**이 문서를 읽는 경우**: `FastPortTestSmokeServer` 실행·설정, 서버 텔레메트리 지표 추가·변경, `ObservedMetricsSnapshot` JSONL 형식, idle 세션 정리(`SessionIdleTracker`), `FastPortTestLoadRunner` CLI 옵션·pacing, `FastPortTestLoadValidation` profile·stage·임계값, 서버·러너 지표 병합, Azure/OCI 클라우드 부하 검증 스크립트, 벤치마크 리포트·runbook 위치.

**다른 문서로 가는 경우**: 엔진 관측 hook(`OnNetwork*`)과 `NetworkDisconnectReason` 정의 → [session.md](session.md). accept 경로 hook(`OnAcceptSucceeded` 등)과 `StartAccept` backlog 인자 → [listener-connector.md](listener-connector.md). `TimerQueue`·`IMonotonicTimeSource` 자체 → [packet-buffers.md](packet-buffers.md). JSONL을 화면에 그리는 쪽 → [dashboard.md](dashboard.md). 빌드·CI 전반 → [platform.md](platform.md).

## 핵심 규칙

- **엔진은 텔레메트리를 모른다.** `LibNetworks`는 `protected virtual OnNetwork*`·`OnAccept*` hook만 제공한다. `FastPortTestSmokeServer`와 `FastPortTestSmokeClientSession`이 override해 `IServerTelemetry`로 넘긴다. 엔진에 `LibTestTelemetry` 참조를 넣지 않는다.
- **JSONL 한 줄 = `ObservedMetricsSnapshot` 하나다.** 직렬화는 항상 `ObservedMetricsJson.SerializerOptions`(camelCase)를 쓴다. 서버 줄은 `serverObserved`만, 러너 줄은 `clientObserved`만 채운다(`FromServer`/`FromClient`). 병합 결과(`*.combined.metrics.jsonl`)는 둘 다 채운다(`Combined`).
- **JSONL 계약은 네 곳이 공유한다.** 생산: SmokeServer(`ServerTelemetryExportBackgroundService`), LoadRunner(`JsonMetricsReporter`). 소비: LoadValidation(`JsonlObservedMetricsReader`), Dashboard(`JsonlPollingAdapter`). 필드 이름을 바꾸거나 지우면 네 곳과 테스트를 같이 고친다. 새 필드는 기본값이 있는 선택 인자로 끝에 붙여 옛 JSONL도 읽히게 한다.
- 서버 지표는 3단계로 흐른다: `ServerTelemetryCollector`(누적 카운터) → `ServerTelemetrySnapshot`(시점 값) → `ServerObservedMetricsSnapshot.FromTelemetry`(직전 스냅샷과의 차이로 초당 값 계산). 첫 스냅샷의 `*PerSecond`는 0이다.
- **LoadRunner는 `LibNetworks`를 쓰지 않는다.** `LoadSession`이 `TcpClient`/`NetworkStream`으로 직접 연결하고 와이어 헤더를 손으로 쓴다(`BinaryPrimitives`). 엔진 버그가 측정 도구에 섞이지 않게 하려는 분리다. csproj에 `LibCommons` 참조는 있지만 코드에서 쓰지 않는다.
- SmokeServer echo는 `Protocols`의 `ProtocolId.Tests` + `EchoRequest`/`EchoResponse`(`tests.proto`)를 쓴다. 템플릿 proto(`PacketIds` 1001/1002)와 다르다.
- 끊김 사유 문자열은 `FastPortTestSmokeClientSession.ToTelemetryReason`이 `NetworkDisconnectReason`을 kebab-case(`remote-closed`, `idle-timeout`, `receive-buffer-overflow` 등)로 바꾼다. enum에 값을 추가하면 여기도 추가한다. 빠지면 `unknown`으로 집계된다.
- **idle 정리는 애플리케이션 정책이다.** 엔진이 아니라 SmokeServer의 `SessionIdleTracker`가 `ITimerQueue.SchedulePeriodic`으로 `ScanExpired`를 돌린다. `LastReceivedTimestamp`가 `IdleTimeout`을 넘으면 `RequestDisconnect(NetworkDisconnectReason.IdleTimeout)`을 호출하고 `RecordIdleTimeoutDisconnect`로 센다. 세션은 `OnAccepted`에서 `Register`, `OnDisconnected`에서 `Unregister`한다.
- LoadRunner heartbeat(`--heartbeat-interval`, 기본 30s)는 서버 idle timeout(기본 120s)보다 짧아야 저속 시나리오에서 세션이 정리되지 않는다.
- 판정 임계값은 코드 상수다: `LoadValidationThresholds.Default`(최소 peak 세션 비율 0.95, 최대 소켓 오류율 0.01, 최대 disconnect 비율 0.05, 최종 disconnect 0, `receive|IOException|TimedOut` 0건, 최소 JSON 샘플 3). profile과 stage 목록은 `LoadValidationProfiles`에 하드코딩돼 있다(`smoke`, `staged`).
- 서버·러너 병합은 **타임스탬프 최근접 매칭**이다(`ObservedMetricsMerger`, 기본 허용치 1500ms). 서버와 러너가 다른 머신이면 시계 차이가 매칭 실패 원인이 된다.

## 파일·타입

| 파일 | 타입·심볼 | 내용 |
|---|---|---|
| `tests-projects/FastPortTestSmokeServer/Program.cs` | top-level | Generic Host. 설정 섹션 파싱, `TimerQueue`·`SessionIdleTracker`·`ServerTelemetryCollector`·`ServerTelemetryExporter` 등록 |
| `tests-projects/FastPortTestSmokeServer/FastPortTestSmokeServerOptions.cs` | `FastPortTestSmokeServerConfiguration`, `FastPortTestSmokeServerOptions`, `FastPortTestSmokeServerTelemetryOptions` | 섹션 `FastPortTestSmokeServer`(없으면 구 이름 `FastPortSmokeServer`). 기본 포트 6628, `DefaultListenBacklog` 4096, `DefaultOutstandingAccepts` 1 |
| `tests-projects/FastPortTestSmokeServer/FastPortTestSmokeServer.cs` | `FastPortTestSmokeServer : BaseMessageListener` | accept hook → `RecordAccept`, `accept-session-create`·`accept-task-start` duration |
| `tests-projects/FastPortTestSmokeServer/FastPortTestSmokeServerBackgroundService.cs` | `FastPortTestSmokeServerBackgroundService` | `StartAccept` 호출, 종료 시 `RequestShutdown` |
| `tests-projects/FastPortTestSmokeServer/ServerTelemetryExportBackgroundService.cs` | `ServerTelemetryExportBackgroundService` | `Telemetry:Output`이 있을 때만 주기적으로 JSONL append. `FileShare.ReadWrite` + `WriteThrough` |
| `tests-projects/FastPortTestSmokeServer/Sessions/FastPortTestSmokeClientSession.cs` | `FastPortTestSmokeClientSession : BaseSessionClient, IIdleTrackedSession` | echo 응답, `OnNetwork*` override, accept 경로 지연(`accept-first-receive` 등), `ToTelemetryReason` |
| `tests-projects/FastPortTestSmokeServer/Sessions/FastPortTestSmokeClientSessionFactory.cs` | `FastPortTestSmokeClientSessionFactory : IClientSessionFactory` | 수신·송신 `ArrayPoolCircularBuffers(8 * 1024)` |
| `tests-projects/FastPortTestSmokeServer/Sessions/SessionIdleTracker.cs` | `SessionIdleTracker`, `IIdleTrackedSession`, `SessionIdleTrackerOptions` | idle 정리. 0 이하 설정은 `Normalized*`가 기본값으로 보정 |
| `tests-projects/LibTestTelemetry/ServerTelemetry.cs` | `IServerTelemetry`, `ServerTelemetryCollector`, `ServerTelemetrySnapshot` | 서버 카운터 누적, 소켓 오류 phase/type/code/class 분류, operation duration 요약 |
| `tests-projects/LibTestTelemetry/ObservedMetrics.cs` | `ObservedMetricsSnapshot`, `ServerObservedMetricsSnapshot`, `ClientObservedMetricsSnapshot`, `SessionRttSummarySnapshot`, `IServerTelemetryExporter`, `ServerTelemetryExporter`, `ObservedMetricsJson` | JSONL 계약과 직렬화 옵션 |
| `tests-projects/FastPortTestLoadRunner/LoadRunnerOptions.cs` | `LoadRunnerOptions`, `LoadScenario`, `LoadPacingOptions`, `LoadPacingPolicy`, `PayloadProfile`, `DurationParser` | CLI 파싱·기본값·`PrintUsage` |
| `tests-projects/FastPortTestLoadRunner/LoadRunner.cs`, `LoadSession.cs` | `LoadRunner`, `LoadSession`, `PayloadGenerator` | ramp-up 간격으로 세션 시작, 세션별 송수신·RTT·heartbeat |
| `tests-projects/FastPortTestLoadRunner/OutstandingRequestPacer.cs` | `OutstandingRequestPacer` | fixed/adaptive window pacing |
| `tests-projects/FastPortTestLoadRunner/Metrics.cs` | `MetricsCollector`, `MetricsSnapshot`, `ConsoleMetricsReporter`, `JsonMetricsReporter` | 클라이언트 지표 수집·출력 |
| `tests-projects/FastPortTestLoadRunner/ObservedMetricsExtensions.cs` | `ToClientObservedMetricsSnapshot` | `MetricsSnapshot` → JSONL 계약 매핑 |
| `tests-projects/FastPortTestLoadRunner/ConnectEventReporter.cs` | `JsonConnectEventReporter`, `ConnectSessionEvent` | 세션별 connect 결과 JSONL |
| `tests-projects/FastPortTestLoadValidation/LoadValidationOptions.cs` | `LoadValidationOptions` | CLI 파싱. 기본 출력 `artifacts/load-validation/{timestamp}-{profile}` |
| `tests-projects/FastPortTestLoadValidation/LoadValidationProfile.cs` | `LoadValidationProfiles` | `smoke`(smoke-fixed-10, smoke-random-25), `staged`(s1-fixed-1k ~ s5-random-10k) |
| `tests-projects/FastPortTestLoadValidation/LoadValidationStage.cs` | `LoadValidationStage`, `LoadValidationThresholds`, `LoadValidationStageSummary`, `LoadValidationRunSummary` | stage 정의·임계값·결과 모델 |
| `tests-projects/FastPortTestLoadValidation/LoadRunnerCommandBuilder.cs`, `ProcessRunner.cs` | `LoadRunnerCommandBuilder`, `LoadRunnerCommand`, `ProcessRunner` | `dotnet run --project <runner> -- ...` 명령 생성·실행, stdout/stderr 로그 저장 |
| `tests-projects/FastPortTestLoadValidation/JsonlObservedMetricsReader.cs`, `ObservedMetricsMerger.cs` | `JsonlObservedMetricsReader`, `ObservedMetricsMerger` | JSONL 읽기, 서버·클라이언트 샘플 병합 |
| `tests-projects/FastPortTestLoadValidation/LoadValidationEvaluator.cs`, `LoadValidationSummaryWriter.cs` | `LoadValidationEvaluator`, `LoadValidationSummaryWriter` | 판정, `manifest.json`·`summary.json`·`summary.md` 작성 |
| `scripts/cloud/` | `server-start.sh`, `runner-smoke.sh`, `runner-10k.sh`, `runner-connectivity.sh`, `ssh-readiness.sh`, `os-readiness.sh`, `write-manifest.sh`, `collect-artifacts.sh`, `azure-*.sh`, `oci-*.sh`, `free-tier-guard.sh` | 서버 VM·러너 분리 검증 보조. 환경 변수 `FASTPORT_*`로 설정 |
| `scripts/load-validation/decompose-summary.sh` | — | `summary.json`을 `jq`로 분해해 출력 |

문서(`docs/`): `staged-load-validation-test-guide.md`(smoke·staged 실행과 결과 읽기), `loadrunner-os-limits.md`(10K 세션 OS 한도), `cloud-server-runner-split-load-validation-runbook.md`(OCI), `azure-server-runner-split-load-validation-runbook.md`(Azure), `baselistener-optimization-benchmark.md`(accept 경로 벤치마크 리포트). LoadRunner 옵션 표는 `tests-projects/FastPortTestLoadRunner/README.md`.

## 작업별 시작점

| 하려는 작업 | 고칠 곳 | 같이 확인할 것 |
|---|---|---|
| 새 서버 텔레메트리 지표 추가 | `IServerTelemetry` 메서드 → `ServerTelemetryCollector` → `ServerTelemetrySnapshot` → `ServerObservedMetricsSnapshot`(끝에 기본값 인자) + `FromTelemetry` | 값을 넣는 hook override(`FastPortTestSmokeClientSession` 또는 `FastPortTestSmokeServer`), `ServerTelemetryTests`·`ObservedMetricsTests`, 판정에 쓰면 `LoadValidationEvaluator`, 화면에 쓰면 [dashboard.md](dashboard.md) |
| 엔진 hook 추가 후 계측 연결 | `LibNetworks`에 hook 추가([session.md](session.md)) → SmokeServer 세션 override | 엔진 수정은 scaffold golden 갱신 대상([platform.md](platform.md)) |
| 새 클라이언트 지표 추가 | `MetricsCollector`·`MetricsSnapshot` → `ClientObservedMetricsSnapshot` → `ToClientObservedMetricsSnapshot` | `ObservedMetricsTests.ClientObservedMetricsSnapshot_MapsLoadRunnerMetrics`, `FastPortTestLoadRunnerTests` |
| LoadRunner 옵션 추가 | `LoadRunnerOptions.TryParse`의 `switch`·`PrintUsage`, `LoadScenario` | LoadValidation이 넘겨야 하면 `LoadValidationOptions` + `LoadRunnerCommandBuilder.Build`, README 옵션 표, 두 Tests 파일 |
| 검증 임계값 바꾸기 | `LoadValidationThresholds.Default` 또는 stage별 `Thresholds` 인자 | 판정 로직 `LoadValidationEvaluator.Evaluate`, `staged-load-validation-test-guide.md`의 Pass/Fail 기준 |
| stage·profile 추가 | `LoadValidationProfiles`(`CreateSmokeProfile`/`CreateStagedProfile`, `IsKnownProfile`) | `LoadValidationProfiles_StagedProfile_HasExpectedStages`, cloud 스크립트의 `--stage` 값 |
| 끊김 사유 집계가 `unknown` | `FastPortTestSmokeClientSession.ToTelemetryReason` | `NetworkDisconnectReason` 값 목록 |
| idle 정리 동작 변경 | `SessionIdleTracker`, `SessionIdleTrackerOptions`, `Program.cs`의 `SessionIdleCleanup` 파싱 | `SessionIdleTrackerTests`, LoadRunner heartbeat 간격 |
| JSONL이 비어 있음 | `Telemetry:Output` 설정 여부, `ServerTelemetryExportBackgroundService` | 리더 쪽 `FileShare` 모드 |
| 병합 매칭 0건 | `--merge-tolerance-ms`, `ObservedMetricsMerger` | 서버·러너 시계 동기화 |

## 실행·테스트

```bash
# 스모크 서버 (JSONL 출력은 Telemetry:Output을 줘야 켜진다)
dotnet run -c Release --project tests-projects/FastPortTestSmokeServer -- \
  --Telemetry:Output=artifacts/load-validation/local/server.metrics.jsonl --Telemetry:IntervalSeconds=1

# 부하 생성기 단독
dotnet run -c Release --project tests-projects/FastPortTestLoadRunner -- \
  --host 127.0.0.1 --port 6628 --sessions 1000 --payload random:4096-16384 \
  --ramp-up 60s --duration 3m --output client.metrics.jsonl

# 단계별 검증 (실행 없이 LoadRunner 명령만 보려면 --dry-run 추가)
dotnet run -c Release --project tests-projects/FastPortTestLoadValidation -- \
  --profile staged --stage s1-fixed-1k --runner-project tests-projects/FastPortTestLoadRunner \
  --server-metrics artifacts/load-validation/local/server.metrics.jsonl --output artifacts/load-validation/s1-local
```

- SmokeServer 설정(`appsettings.json`): `FastPortTestSmokeServer:Host/Port/ListenBacklog/OutstandingAccepts`, `SessionIdleCleanup:Enabled/IdleTimeoutSeconds/ScanIntervalSeconds`. `Telemetry:Output/IntervalSeconds`는 appsettings에 없고 명령줄이나 환경 변수(`Telemetry__Output`)로 준다.
- LoadRunner 옵션 전체: `--host --port --sessions --payload(fixed:<n>|random:<min>-<max>) --rate --ramp-up --duration --metrics-interval --output --connect-events-output --heartbeat-interval(none 가능) --max-pending-requests-per-session(구 옵션, fixed-window로 매핑) --pacing-policy(none|fixed-window|adaptive-window) --pacing-fixed-window --pacing-min-window --pacing-initial-window --pacing-max-window --pacing-rtt-target-ms --pacing-rtt-high-ms --pacing-increase-every`. 종료 코드 0 정상, 1 오류, 130 취소.
- LoadValidation 옵션 전체: `--profile(smoke|staged) --host --port --output --stage --runner-project --configuration --server-metrics --merge-tolerance-ms --dry-run --continue-on-failure --runner-no-build` + LoadRunner와 같은 pacing 옵션. 종료 코드 0 통과, 2 판정 실패, 1 인자 오류.
- 출력 디렉터리: `manifest.json`, `summary.json`, `summary.md`, stage별 `{id}.metrics.jsonl`, `{id}.connect-events.jsonl`, `{id}.stdout.log`, `{id}.stderr.log`, `--server-metrics` 사용 시 `{id}.combined.metrics.jsonl`. `artifacts/load-validation/`은 `.gitignore` 대상이다.
- 테스트(`tests-projects/FastPortTests/`, `dotnet test FastPortSharp.sln -c Release`): `FastPortTestSmokeServerTests`(실제 loopback echo + 텔레메트리), `SessionIdleTrackerTests`, `ServerTelemetryTests`, `ObservedMetricsTests`, `FastPortTestLoadRunnerTests`, `FastPortTestLoadValidationTests`. LoadRunner·LoadValidation은 `InternalsVisibleTo("FastPortTests")`로 internal 타입을 연다.

## 주의

- `--runner-project` 기본값 `FastPortTestLoadRunner`는 현재 작업 디렉터리 기준 상대 경로다. 프로젝트는 `tests-projects/` 아래에 있으므로 저장소 루트에서 실행하면 `--runner-project tests-projects/FastPortTestLoadRunner`를 넘긴다. `scripts/cloud/runner-smoke.sh`·`runner-10k.sh`는 이 옵션을 넘기지 않는다.
- `tests-projects/FastPortTestLoadRunner/README.md` 예시의 `--project FastPortTestLoadRunner`도 같은 이유로 루트에서는 `tests-projects/FastPortTestLoadRunner`로 바꿔 실행한다. README 옵션 표에는 pacing 옵션이 빠져 있으니 `LoadRunnerOptions.PrintUsage`를 기준으로 본다.
- 10K 세션은 OS 파일 디스크립터·ephemeral port 한도에 걸린다. 실행 전에 `docs/loadrunner-os-limits.md`를 확인한다. full staged 검증은 기본 `dotnet test`에 넣지 않는다.
- SmokeServer의 `LatencyStats`는 static 단일 인스턴스이고 콘솔 출력이 켜져 있다(`EnableConsoleOutput = true`).
- 클라우드 결과에서 러너 CPU나 로컬 네트워크가 먼저 포화되면 순수 서버 벤치마크로 해석하지 않는다(cloud runbook의 Result Interpretation).
- `cloud-server-runner-split-load-validation-runbook.md`가 언급하는 `docs/load-validation-benchmark-results.md`는 저장소에 없다.
- 용어(세션 Client/Server 명명 등)는 [glossary.md](glossary.md), 전체 지도는 [README.md](README.md).
