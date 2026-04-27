# fastport-loadrunner - Design Document

> Version: 1.0.0 | Date: 2026-04-27 | Status: Completed
> Level: Starter | Plan: fastport-loadrunner.plan.md

---

## 1. Overview

`FastPortLoadRunner`는 `FastPortServer`에 실제 TCP 세션을 생성하고 payload를 송수신하며 서버의 처리량과 안정성을 측정하는 콘솔 기반 부하 테스트 도구다.

기존 `FastPortBenchmark`의 micro benchmark 역할은 제거한다. 이 기능의 중심은 실제 네트워크 경로를 통과하는 테스트다.

## 2. Design Goals

- CLI 옵션만으로 부하 시나리오를 재현할 수 있어야 한다.
- 1,000 세션부터 10,000 세션까지 ramp-up 방식으로 확장 가능해야 한다.
- 4K~16K 랜덤 payload와 8K 고정 payload를 지원해야 한다.
- 서버/클라이언트 양쪽에서 필요한 telemetry를 수집할 수 있는 경계를 제공해야 한다.
- 향후 MAUI dashboard가 같은 telemetry를 읽을 수 있도록 출력 모델을 단순하게 유지해야 한다.
- 게임 서버 템플릿화에 방해되지 않도록 부하 테스트 코드는 엔진 코드와 분리되어야 한다.

## 3. Architecture

### 3.1 Project Boundary

| Project | Responsibility |
|---------|----------------|
| `FastPortLoadRunner` | 부하 테스트 실행, 세션 생성, payload 송신, 클라이언트 측 telemetry 수집 |
| `LibNetworks` | TCP 연결, session, packet send/receive engine |
| `LibCommons` | packet, buffer, latency/metric primitive |
| `Protocols` | 테스트용 protobuf message |
| `FastPortServer` | 부하 테스트 대상 서버 |

`FastPortLoadRunner`는 서버 로직을 포함하지 않는다. 서버는 기존 `FastPortServer`를 실행하고, LoadRunner는 독립 프로세스로 서버에 연결한다.

### 3.2 Runtime Flow

```text
CLI args
  -> LoadRunnerOptions
  -> LoadScenario
  -> LoadRunner
  -> SessionRampUp
  -> LoadSession[]
  -> PayloadGenerator
  -> Socket send/receive
  -> MetricsCollector
  -> Console/JSON output
```

### 3.3 Component Design

| Component | Type | Responsibility |
|-----------|------|----------------|
| `LoadRunnerOptions` | record | CLI 옵션 파싱 결과 |
| `LoadScenario` | record | 실행 가능한 테스트 시나리오 모델 |
| `PayloadProfile` | record struct | fixed/random payload 정책 |
| `PayloadGenerator` | class | payload byte 생성 |
| `LoadRunner` | class | 전체 테스트 lifecycle 제어 |
| `SessionRampUp` | class | 목표 세션 수까지 일정 속도로 세션 생성 |
| `LoadSession` | class | 단일 TCP 세션 연결, 송신, 수신, 종료 |
| `MetricsCollector` | class | thread-safe metric 집계 |
| `MetricsSnapshot` | record | 특정 시점의 metric 출력 모델 |
| `ConsoleMetricsReporter` | class | 주기적 콘솔 출력 |
| `JsonMetricsReporter` | class | dashboard 연동용 JSON lines 출력 |

## 4. CLI Contract

### 4.1 Supported Options

| Option | Required | Default | Description |
|--------|----------|---------|-------------|
| `--host <host>` | No | `127.0.0.1` | Target server host |
| `--port <port>` | No | `6628` | Target server port |
| `--sessions <count>` | No | `1` | Concurrent session count |
| `--payload fixed:<bytes>` | No | `fixed:8192` | Fixed payload size |
| `--payload random:<min>-<max>` | No | N/A | Random payload size range |
| `--rate <count>` | No | `1` | Packets per second per session |
| `--ramp-up <duration>` | No | `10s` | Time to reach target sessions |
| `--duration <duration>` | No | `1m` | Test duration after start |
| `--metrics-interval <duration>` | No | `1s` | Reporting interval |
| `--output <path>` | No | none | Optional JSONL metric output path |

### 4.2 Example Commands

```bash
dotnet run -c Release --project FastPortLoadRunner -- \
  --sessions 10000 \
  --payload random:4096-16384 \
  --duration 5m \
  --ramp-up 60s
```

```bash
dotnet run -c Release --project FastPortLoadRunner -- \
  --sessions 10000 \
  --payload fixed:8192 \
  --rate 20 \
  --metrics-interval 1s
```

## 5. Data Model

### 5.1 LoadScenario

```csharp
internal sealed record LoadScenario(
    string Host,
    int Port,
    int Sessions,
    PayloadProfile Payload,
    int SendRatePerSession,
    TimeSpan RampUp,
    TimeSpan Duration,
    TimeSpan MetricsInterval,
    string? OutputPath);
```

### 5.2 PayloadProfile

```csharp
internal readonly record struct PayloadProfile(
    PayloadMode Mode,
    int MinBytes,
    int MaxBytes);
```

Rules:

- `fixed:N` uses `MinBytes == MaxBytes == N`.
- `random:min-max` uses inclusive min/max range.
- payload size means message payload bytes before FastPort packet header.

### 5.3 MetricsSnapshot

```csharp
internal sealed record MetricsSnapshot(
    DateTimeOffset Timestamp,
    int TargetSessions,
    int ConnectedSessions,
    long TotalSentPackets,
    long TotalReceivedPackets,
    long TotalSentBytes,
    long TotalReceivedBytes,
    double SentPacketsPerSecond,
    double ReceivedPacketsPerSecond,
    double SentBytesPerSecond,
    double ReceivedBytesPerSecond,
    double Tps,
    double RttAverageMs,
    double RttP50Ms,
    double RttP95Ms,
    double RttP99Ms,
    long AcceptCount,
    long DisconnectCount,
    long SocketErrorCount,
    double SocketErrorRate);
```

## 6. Packet and Protocol Design

### 6.1 Initial Protocol

초기 구현은 기존 `FastPortServer`의 echo request/response 흐름을 활용한다.

- LoadRunner sends `EchoRequest`.
- Server responds with `EchoResponse`.
- Request header contains `request_id` and client send timestamp.
- Response header preserves client send timestamp and adds server receive/send timestamps.
- LoadRunner records receive timestamp and calculates RTT.

### 6.2 Payload Size

- `fixed:8192`: every request carries 8K payload.
- `random:4096-16384`: each request picks a random size between 4K and 16K.
- 16K payload must validate packet fragmentation/reassembly because current receive socket buffer is 8K.

## 7. Telemetry Design

### 7.1 Metrics

| Metric | Source | Notes |
|--------|--------|-------|
| Connected sessions | LoadRunner session lifecycle | Current successful TCP sessions |
| CCU | Same as connected sessions | Dashboard term |
| TPS | Response count/sec | Echo response based |
| RTT | Client send/receive timestamp | p50/p95/p99 required |
| Send bytes/sec | LoadSession send path | Payload plus packet overhead |
| Recv bytes/sec | LoadSession receive path | Response packet bytes |
| Send packets/sec | LoadSession send path | Per interval |
| Recv packets/sec | LoadSession receive path | Per interval |
| Accept/disconnect count | Server telemetry later | Client can estimate connect/disconnect |
| Socket error count/rate | LoadSession exception handling | Per interval and cumulative |

### 7.2 Output Format

Console output should be human-readable and compact:

```text
time=12:00:01 sessions=10000 tps=182000 rtt_avg=12.3ms p95=28.1ms send=1.2GB/s recv=1.1GB/s errors=0.01%
```

JSONL output should be one snapshot per line:

```json
{"timestamp":"2026-04-27T12:00:01+09:00","connectedSessions":10000,"tps":182000,"rttP95Ms":28.1}
```

## 8. Concurrency Design

### 8.1 Session Lifecycle

Each `LoadSession` owns:

- TCP socket or future `BaseConnector`-based adapter
- send loop
- receive loop
- per-session cancellation token
- request id sequence

### 8.2 Ramp-Up

`SessionRampUp` spreads connection attempts across `RampUp`.

Example:

- `--sessions 10000 --ramp-up 60s`
- about 167 sessions/sec
- avoid creating 10,000 sockets at once

### 8.3 Backpressure

The first implementation should keep a simple fixed send rate per session. Later versions can add:

- max in-flight requests per session
- adaptive rate control
- error threshold stop condition

## 9. Implementation Order

1. Keep current CLI parsing and help output.
2. Add `LoadScenario` conversion from `LoadRunnerOptions`.
3. Add `PayloadGenerator` with deterministic random support.
4. Add `MetricsCollector` and `MetricsSnapshot`.
5. Implement basic `LoadRunner` lifecycle.
6. Implement TCP `LoadSession` connect/send/receive loops.
7. Wire echo protobuf request/response.
8. Add console reporter.
9. Add JSONL reporter.
10. Add integration smoke test with small session count.
11. Document local OS limits for high session counts.

## 10. Test Plan

### 10.1 Unit Tests

- CLI parser accepts valid defaults.
- CLI parser rejects invalid ports, sessions, rates, durations.
- `PayloadProfile` parses `fixed:8192`.
- `PayloadProfile` parses `random:4096-16384`.
- `PayloadGenerator` returns sizes inside range.
- `MetricsCollector` calculates interval rates correctly.

### 10.2 Integration Tests

- Start `FastPortServer`.
- Run LoadRunner with `--sessions 10 --payload fixed:1024 --duration 10s`.
- Validate all sessions connect and send/receive packets.
- Run `--payload random:4096-16384` to validate large packet path.

### 10.3 Manual Load Tests

- 1,000 sessions local baseline.
- 3,000 sessions ramp-up.
- 5,000 sessions ramp-up.
- 10,000 sessions with OS limit notes.

## 11. Risks

| Risk | Mitigation |
|------|------------|
| OS file descriptor/ephemeral port limits block 10,000 sessions | Document required system settings and support multi-process load |
| LoadRunner itself becomes CPU bottleneck | Track client CPU/memory and support multiple runner processes |
| Telemetry overhead skews results | Use interval aggregation and avoid per-packet logging |
| Packet fragmentation bugs hide in current code | Add large payload integration tests before 10,000 session test |
| Server send loop has pending async-send risk | Stabilize session send loop before high load |

## 12. Next Phase

Do phase should start with packet/buffer/session stabilization before implementing high-scale load generation. The LoadRunner can then grow against a stable network core.
