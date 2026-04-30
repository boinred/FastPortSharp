# server-telemetry-export-merge-socket-error-classification - Design Document

> Version: 1.0.0 | Date: 2026-04-29 | Status: Draft
> Level: Starter | Plan: docs/01-plan/features/server-telemetry-export-merge-socket-error-classification.plan.md

---

## 1. Overview

이번 기능은 10K 부하 실패를 client-only JSONL이 아니라 client/server combined timeline으로 분석하기 위한 관측 기능이다.

핵심 판단 질문은 다음과 같다.

- client pending request 증가와 server pending send/backpressure 증가가 같은 시간대에 발생하는가?
- disconnect/socket error는 connect, send, receive, protocol parse 중 어느 단계에 집중되는가?
- 실패 원인이 server send path, client socket path, 또는 로컬 OS/socket pressure 중 어디에 가까운가?

성능 튜닝은 이번 범위가 아니다. 이번 범위는 다음 10K 실행에서 병목 후보를 줄일 수 있는 export, merge, classification 데이터 계약을 만드는 것이다.

## 2. Architecture

### 2.1 Ownership

| Area | Owner module | Responsibility |
|------|--------------|----------------|
| server telemetry counters | `LibNetworks/Telemetry` | protocol-neutral send/receive/session counters 유지 |
| server JSONL export | `FastPortSmokeServer` | `IServerTelemetryExporter` snapshot을 주기적으로 파일에 기록 |
| client socket classification | `FastPortLoadRunner` | load session phase별 socket/protocol error 집계 |
| client/server metrics reader | `FastPortLoadValidation` | client-only, server-only, combined envelope 읽기 |
| timestamp merge | `FastPortLoadValidation` | client sample 기준 nearest server sample merge |
| validation summary | `FastPortLoadValidation` | merged backlog fields와 classified socket errors 출력 |

### 2.2 Boundary Rules

- `LibNetworks`에는 smoke/echo protocol 지식이나 load validation workflow를 넣지 않는다.
- `FastPortSmokeServer`는 server process 내부 telemetry export만 담당한다.
- `FastPortLoadValidation`은 server process lifecycle을 필수로 관리하지 않는다. 이번 단계에서는 optional `--server-metrics` path를 받는다.
- 기존 `ObservedMetricsSnapshot` envelope는 유지한다.
- 기존 client-only JSONL은 계속 유효해야 한다.

## 3. Data Contracts

### 3.1 Existing Envelope

기존 envelope를 그대로 사용한다.

```csharp
public sealed record ObservedMetricsSnapshot(
    DateTimeOffset Timestamp,
    ClientObservedMetricsSnapshot? ClientObserved,
    ServerObservedMetricsSnapshot? ServerObserved);
```

Server export는 다음 형태의 JSONL을 쓴다.

```json
{"timestamp":"...","clientObserved":null,"serverObserved":{...}}
```

Combined output은 다음 형태를 쓴다.

```json
{"timestamp":"...","clientObserved":{...},"serverObserved":{...}}
```

### 3.2 Client Socket Classification

`ClientObservedMetricsSnapshot`에 optional dictionary fields를 추가한다. 기본값은 `null` 또는 empty dictionary로 두어 기존 JSONL 역직렬화와 summary 생성을 깨지 않는다.

```csharp
IReadOnlyDictionary<string, long>? SocketErrorCountsByPhase = null;
IReadOnlyDictionary<string, long>? SocketErrorCountsByType = null;
IReadOnlyDictionary<string, long>? SocketErrorCountsByCode = null;
IReadOnlyDictionary<string, long>? SocketErrorCountsByClass = null;
```

`SocketErrorCountsByClass` key format은 다음처럼 단순 문자열로 둔다.

```text
{phase}|{exceptionType}|{socketErrorCode}
```

예시:

```text
connect|SocketException|ConnectionRefused
send|IOException|SocketError.Unknown
receive|SocketException|ConnectionReset
protocol|InvalidDataException|none
```

enum DTO 대신 dictionary를 선택한다. 이유는 플랫폼별 `SocketError` 값과 exception wrapping 차이가 있어도 schema migration 없이 counters를 보존하기 위해서다.

### 3.3 Summary Additions

`LoadValidationStageSummary`에 optional/default fields를 추가한다.

```csharp
string? ServerMetricsPath = null;
string? CombinedMetricsPath = null;
int ServerJsonSamples = 0;
int MergedSamples = 0;
int UnmatchedClientSamples = 0;
double MaxMergeSkewMs = 0;
long MaxPendingSendRequests = 0;
long MaxSendBackpressureEvents = 0;
long MaxSendBufferBytes = 0;
double MaxSendRequestsPerSecond = 0;
double MaxSendCompletionsPerSecond = 0;
IReadOnlyDictionary<string, long>? SocketErrorCountsByPhase = null;
IReadOnlyDictionary<string, long>? SocketErrorCountsByClass = null;
```

Markdown summary에는 핵심 fields만 표에 추가한다.

- max pending request
- max pending send
- max server send buffer bytes
- server backpressure events
- merge matched/unmatched
- top socket error classes는 표 아래 bullet로 표시한다.

JSON summary에는 전체 counters를 보존한다.

## 4. Server Telemetry Export

### 4.1 Options

`FastPortSmokeServer`에 telemetry export options를 추가한다.

```csharp
public sealed class FastPortSmokeServerTelemetryOptions
{
    public string? Output { get; init; }
    public int IntervalSeconds { get; init; } = 1;
}
```

Configuration key는 `Telemetry` section을 사용한다.

```bash
./FastPortSmokeServer/bin/Release/net10.0/FastPortSmokeServer \
  --Telemetry:Output artifacts/load-validation/s5-server-merged/server.metrics.jsonl \
  --Telemetry:IntervalSeconds 1
```

`Output`이 비어 있으면 export service는 no-op로 동작하거나 등록되지 않는다.

### 4.2 Hosted Service

새 hosted service를 추가한다.

```text
FastPortSmokeServer/ServerTelemetryExportBackgroundService.cs
```

동작:

1. `Telemetry:Output`이 없으면 즉시 대기 no-op 상태로 둔다.
2. output directory를 생성한다.
3. `FileMode.Create`, `FileAccess.Write`, `FileShare.Read`로 JSONL writer를 연다.
4. interval마다 `IServerTelemetryExporter.CreateObservedSnapshot(previous)`를 호출한다.
5. JSONL 한 줄을 쓰고 flush한다.
6. cancellation 시 마지막 flush 후 종료한다.

Rate fields는 이전 server snapshot과 현재 snapshot의 timestamp 차이로 계산한다. 첫 sample의 per-second fields는 0이다.

### 4.3 Hot Path Constraint

Export service는 interval 기반 snapshot만 수행한다. session send/receive hot path에는 새 file I/O를 넣지 않는다.

## 5. Metrics Reader And Merge

### 5.1 Reader

`JsonlObservedMetricsReader`를 client-only reader에서 envelope reader로 확장한다.

```csharp
internal sealed record JsonlObservedMetricsReadResult(
    IReadOnlyList<ObservedMetricsSnapshot> Samples,
    IReadOnlyList<ClientObservedMetricsSnapshot> ClientSamples,
    IReadOnlyList<ServerObservedMetricsSnapshot> ServerSamples,
    IReadOnlyList<string> Errors);
```

유지할 API:

- `ReadClientSamplesAsync(path, cancellationToken)`은 기존 test와 caller compatibility를 위해 유지한다.

추가할 API:

- `ReadObservedSamplesAsync(path, cancellationToken)`
- `ReadServerSamplesAsync(path, cancellationToken)`

Reader rules:

- empty line은 skip한다.
- JSON parse error는 errors에 누적하고 계속 읽는다.
- client-only 파일에서 server sample이 없어도 error로 보지 않는다.
- server-only 파일에서 client sample이 없어도 error로 보지 않는다.
- 명시적으로 client file을 읽을 때 `clientObserved`가 없는 line은 기존처럼 error로 둔다.

### 5.2 Merge Algorithm

새 class를 추가한다.

```text
FastPortLoadValidation/ObservedMetricsMerger.cs
```

입력:

- client samples sorted by timestamp
- server samples sorted by timestamp
- tolerance, default `1500ms`

출력:

```csharp
internal sealed record ObservedMetricsMergeResult(
    IReadOnlyList<ObservedMetricsSnapshot> CombinedSamples,
    int MatchedSamples,
    int UnmatchedClientSamples,
    double MaxSkewMs);
```

Algorithm:

1. client sample을 기준 timeline으로 사용한다.
2. server sample pointer를 timestamp 순서로 이동한다.
3. 각 client sample마다 absolute timestamp difference가 가장 작은 server sample을 찾는다.
4. diff가 tolerance 이내면 `ObservedMetricsSnapshot.Combined(client, server)`를 만든다.
5. tolerance 밖이면 `ObservedMetricsSnapshot.Combined(client, null)`을 만든다.
6. unmatched client count와 max skew를 기록한다.

Server-only sample을 기준으로 combined row를 만들지는 않는다. validation summary의 핵심은 client failure 시점에 server state가 무엇이었는지 보는 것이기 때문이다.

### 5.3 Combined Artifact

`--server-metrics`가 제공되면 stage별 combined JSONL을 쓴다.

```text
{output}/{stageId}.combined.metrics.jsonl
```

이 파일은 기존 client JSONL을 대체하지 않는다. 기존 `{stageId}.metrics.jsonl`은 그대로 유지한다.

## 6. Load Validation CLI

`LoadValidationOptions`에 optional server metrics path와 merge tolerance를 추가한다.

```bash
./FastPortLoadValidation/bin/Release/net10.0/FastPortLoadValidation \
  --profile staged \
  --stage s5-random-10k \
  --output artifacts/load-validation/s5-server-merged \
  --server-metrics artifacts/load-validation/s5-server-merged/server.metrics.jsonl \
  --merge-tolerance-ms 1500
```

Defaults:

- `--server-metrics`: null
- `--merge-tolerance-ms`: 1500

Behavior:

- server metrics path가 없으면 현재 client-only behavior와 동일하다.
- path가 있는데 file이 없으면 stage summary failure에 error를 추가한다.
- server samples가 있어도 matching된 sample이 없으면 summary failure에 warning 수준 message를 failures로 추가한다. 이 경우 validation exit code는 기존 failure model에 따라 failed가 된다.

## 7. Socket Error Classification

### 7.1 Phases

분류 phase는 다음 값으로 제한한다.

| Phase | Meaning |
|-------|---------|
| `connect` | `TcpClient.ConnectAsync` 이전/도중 실패 |
| `send` | request packet 생성 후 `WriteAsync`/`FlushAsync` 도중 실패 |
| `receive` | `ReadAsync` 또는 response body read 도중 실패 |
| `protocol` | packet size, protocol id, protobuf parse 등 protocol-level invalid response |
| `unknown` | 기존 compatibility path 또는 phase 판별 불가 |

Cancellation은 error로 집계하지 않는다.

### 7.2 Metrics Collector API

`MetricsCollector`에 overload를 추가한다.

```csharp
public void RecordSocketError(string phase, Exception? exception = null);
public void RecordProtocolError(string reason);
```

기존 `RecordSocketError()`는 유지하고 `unknown` phase로 연결한다.

Internal state는 `ConcurrentDictionary<string, long>` 또는 lock-protected `Dictionary<string, long>`을 사용한다. Snapshot 생성 시 dictionary copy를 만든다.

### 7.3 LoadSession Placement

`LoadSession`은 phase별로 다음 위치에서 분류한다.

- connect: `ConnectAsync` 실패 catch
- send: `SendLoopAsync` 내부 `WriteAsync`/`FlushAsync` exception
- receive: `ReadExactAsync` 내부 `ReadAsync` exception
- protocol: invalid packet size, unexpected protocol id, protobuf parse exception

정상 EOF 또는 cancellation은 socket error로 세지 않는다. session disconnect count와 socket error count를 분리해야 10K 결과에서 정상 종료와 fault를 구분할 수 있다.

## 8. Implementation Order

1. Add server telemetry export options and hosted service in `FastPortSmokeServer`.
2. Add socket error classification counters to `FastPortLoadRunner/Metrics.cs`.
3. Update `FastPortLoadRunner/LoadSession.cs` to record phase-aware errors.
4. Extend `ClientObservedMetricsSnapshot` and `ObservedMetricsExtensions`.
5. Extend `JsonlObservedMetricsReader` to read client/server/envelope samples.
6. Add `ObservedMetricsMerger`.
7. Add `--server-metrics` and `--merge-tolerance-ms` to `LoadValidationOptions`.
8. Update `Program.cs` evaluation flow to read server metrics once and merge per stage.
9. Extend `LoadValidationStageSummary` and `LoadValidationSummaryWriter`.
10. Add focused unit tests and run build/test.
11. Run a reduced smoke validation with server export and merge enabled.

## 9. Test Plan

### 9.1 Unit Tests

Add or extend tests under `LibCommonTest`.

- `ServerTelemetryExportBackgroundService` writes server-only observed JSONL and flushes on cancellation.
- Server export disabled path does not create a file.
- `JsonlObservedMetricsReader` reads old client-only JSONL.
- `JsonlObservedMetricsReader` reads server-only JSONL.
- `ObservedMetricsMerger` matches nearest server sample within tolerance.
- `ObservedMetricsMerger` records unmatched client samples outside tolerance.
- `LoadValidationEvaluator` includes server max fields when merged samples exist.
- `LoadValidationEvaluator` preserves current client-only summary behavior.
- `MetricsCollector` aggregates socket errors by phase/type/code.
- `LoadSession` protocol invalid response records `protocol` classification.

### 9.2 Command Verification

Baseline verification:

```bash
dotnet build FastPortCharp.sln
dotnet test FastPortCharp.sln --no-build
```

Reduced export/merge verification:

```bash
./FastPortSmokeServer/bin/Release/net10.0/FastPortSmokeServer \
  --Telemetry:Output artifacts/load-validation/server-merge-smoke/server.metrics.jsonl \
  --Telemetry:IntervalSeconds 1
```

```bash
./FastPortLoadValidation/bin/Release/net10.0/FastPortLoadValidation \
  --profile smoke \
  --output artifacts/load-validation/server-merge-smoke \
  --server-metrics artifacts/load-validation/server-merge-smoke/server.metrics.jsonl
```

Target 10K verification:

```bash
./FastPortLoadValidation/bin/Release/net10.0/FastPortLoadValidation \
  --profile staged \
  --stage s5-random-10k \
  --output artifacts/load-validation/s5-server-merged \
  --server-metrics artifacts/load-validation/s5-server-merged/server.metrics.jsonl
```

## 10. Acceptance Criteria

- `FastPortSmokeServer` can write server observed JSONL without changing `LibNetworks` protocol behavior.
- Server JSONL includes pending send, max pending send, send backpressure, send buffer bytes, and max send buffer bytes.
- `FastPortLoadValidation` runs with no `--server-metrics` exactly as before.
- `FastPortLoadValidation` with `--server-metrics` writes `{stageId}.combined.metrics.jsonl`.
- Summary JSON includes server backlog fields and socket error classification counters.
- Summary Markdown shows enough merged fields to compare client pending request and server pending send pressure.
- Client socket errors are split by phase and class.
- `dotnet build FastPortCharp.sln` passes.
- `dotnet test FastPortCharp.sln --no-build` passes.
- Reduced smoke export/merge run produces `server.metrics.jsonl`, client metrics JSONL, combined JSONL, and summary files.

## 11. Risks And Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| server export perturbs high-load run | high | interval writer only, no hot path file I/O, flush once per sample |
| timestamp merge is misleading | medium | record matched/unmatched count and max skew in summary |
| socket classification overfits one OS | medium | preserve phase, exception type, and socket error code as raw counters |
| summary becomes too wide | low | keep Markdown compact, put detailed counters in JSON |
| server metrics covers multiple stages | medium | merge per client stage timestamp range; keep server path and merge stats in each stage summary |

## 12. Deferred Work

- Managed server lifecycle inside `FastPortLoadValidation`.
- Prometheus/OpenTelemetry export.
- MAUI or web dashboard for combined timeline.
- Automated OS/socket tuning recommendations.
- Distributed load generation.
