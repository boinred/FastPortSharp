# 10k-load-bottleneck-telemetry - Design Document

> Version: 1.0.0 | Date: 2026-04-28 | Status: Draft
> Level: Starter | Plan: docs/01-plan/features/10k-load-bottleneck-telemetry.plan.md

---

## 1. Overview

`10k-load-bottleneck-telemetry`는 `s5-random-10k` 부하에서 관측된 두 가지 현상을 원인 지표로 분해하기 위한 설계이다.

- target 10,000 sessions 중 peak current sessions가 9,767에 그침
- RTT P95/P99가 8,738.94 ms / 10,137.04 ms까지 상승

이번 단계의 핵심은 성능 최적화를 바로 넣는 것이 아니라, 병목 위치를 판단할 수 있는 telemetry contract를 먼저 확장하는 것이다. `FastPortServer`/`LibNetworks`는 바로 사용할 수 있는 기본 네트워크 엔진이어야 하므로, 엔진에는 protocol-neutral counter만 추가하고 smoke/load 전용 해석은 `FastPortSmokeServer`, `FastPortLoadRunner`, `FastPortLoadValidation`에 둔다.

## 2. Current Architecture

### 2.1 Runtime Flow

현재 관련 흐름은 다음과 같다.

1. `BaseSession`이 receive/send socket event를 처리한다.
2. `ServerTelemetryCollector`가 accept, disconnect, packet, byte, error 누적값을 기록한다.
3. `ServerObservedMetricsSnapshot`이 server snapshot을 observed contract로 변환한다.
4. `FastPortLoadRunner`의 `MetricsCollector`가 client 관측 값을 JSONL로 출력한다.
5. `FastPortLoadValidation`이 JSONL을 읽어 stage summary와 pass/fail을 만든다.

### 2.2 Existing Telemetry

현재 server telemetry:

- accepted sessions
- disconnected sessions
- connected sessions
- received packets / bytes
- sent packets / bytes
- accept errors
- socket errors
- parse errors
- protocol errors
- socket error rate

현재 client telemetry:

- target/current sessions
- total sent/received packets
- total sent/received bytes
- send/receive bytes per second
- sent/received packets per second
- TPS
- RTT average/P50/P95/P99
- connect count
- disconnect count
- socket error count/rate

### 2.3 Missing Signals

10K 병목 판단에 부족한 지표는 다음이다.

- server send path에 밀린 작업이 있는지
- send buffer backlog가 증가하는지
- socket send completion이 request보다 늦어지는지
- client request in-flight가 누적되는지
- client scheduling/timer drift가 RTT를 왜곡하는지
- connect attempt 대비 success/failure가 어떤지

## 3. Design Goals

- `LibNetworks`에는 게임 protocol이나 smoke protocol을 모르는 범용 counter만 둔다.
- hot path 추가 비용은 `Interlocked` 기반 counter와 단순 max update로 제한한다.
- JSONL contract는 기존 필드를 유지하면서 optional-friendly 방식으로 확장한다.
- `FastPortLoadValidation`은 기존 pass/fail threshold를 유지하고, 병목 지표를 summary에 추가한다.
- MAUI dashboard에서 그대로 읽을 수 있는 명확한 metric 이름을 사용한다.

## 4. Telemetry Contract

### 4.1 Server Metrics

`ServerTelemetrySnapshot`과 `ServerObservedMetricsSnapshot`에 다음 필드를 추가한다.

| Field | Type | Source | Meaning |
|-------|------|--------|---------|
| `TotalSendRequests` | `long` | `BaseSession.RequestSendBuffers` | application이 socket send를 요청한 packet count |
| `PendingSendRequests` | `long` | request minus completion | 아직 send completion이 확인되지 않은 send request count |
| `MaxPendingSendRequests` | `long` | collector max | snapshot 기간까지 관측된 최대 pending send request |
| `SendBackpressureEvents` | `long` | pending threshold 초과 시 | send path가 밀린 이벤트 count |
| `SendBackpressureRate` | `double` | observed delta | 초당 backpressure event count |
| `SendBufferBytes` | `long` | `m_SendBuffers.CanReadSize` sample | 현재 session send buffer backlog bytes 합산 또는 근사값 |
| `MaxSendBufferBytes` | `long` | collector max | 관측된 최대 send buffer backlog bytes |

첫 구현에서는 모든 session의 buffer depth를 정확히 합산하기보다 `BaseSession` send path에서 관측 가능한 per-session `CanReadSize`를 sample로 기록하고 max를 먼저 제공한다. 전체 합산은 session registry가 필요하므로 이번 단계의 non-goal로 둔다.

### 4.2 Client Metrics

`MetricsSnapshot`과 `ClientObservedMetricsSnapshot`에 다음 필드를 추가한다.

| Field | Type | Source | Meaning |
|-------|------|--------|---------|
| `ConnectAttemptCount` | `long` | session connect 시작 시 | client가 connect를 시도한 총 횟수 |
| `ConnectFailureCount` | `long` | connect exception/fail | connect 실패 누적 count |
| `PendingRequestCount` | `long` | sent minus received | response를 아직 받지 못한 request count |
| `MaxPendingRequestCount` | `long` | collector max | 관측된 최대 pending request count |
| `ActiveSessionRatio` | `double` | current / target | target 대비 active session ratio |
| `SchedulerDriftAverageMs` | `double` | reporter loop delay | metrics interval 기준 평균 scheduling drift |
| `SchedulerDriftMaxMs` | `double` | reporter loop delay | 관측된 최대 scheduling drift |

`PendingRequestCount`는 현재 echo-style smoke protocol 기준으로 유효하다. 장기적으로는 protocol이 request/response 구조가 아닐 수 있으므로 이름은 client-observed 분석 지표로 유지하고, `LibNetworks`에는 넣지 않는다.

### 4.3 JSONL Shape

기존 JSONL 구조는 유지한다.

```json
{
  "timestamp": "2026-04-28T00:00:00+09:00",
  "clientObserved": {
    "targetSessions": 10000,
    "currentSessions": 9767,
    "pendingRequestCount": 1234,
    "maxPendingRequestCount": 4567,
    "schedulerDriftMaxMs": 42.5
  },
  "serverObserved": {
    "currentSessions": 9767,
    "pendingSendRequests": 321,
    "maxPendingSendRequests": 654,
    "sendBackpressureEvents": 12
  }
}
```

`FastPortLoadRunner`는 우선 client-only JSONL을 계속 출력한다. `FastPortSmokeServer`의 server telemetry export와 병합이 필요한 경우에는 후속 단계에서 별도 merge runner 또는 dashboard collector가 `ObservedMetricsSnapshot.Combined`를 사용한다.

## 5. Component Design

### 5.1 `LibNetworks.Telemetry`

대상 파일:

- `LibNetworks/Telemetry/ServerTelemetry.cs`
- `LibNetworks/Telemetry/ObservedMetrics.cs`

변경 방향:

- `IServerTelemetry`에 send request/backpressure 관련 record method 추가
- `ServerTelemetryCollector`는 `Interlocked`만 사용해 누적값과 max값 관리
- `NullServerTelemetry`는 no-op 구현 유지
- `ServerTelemetrySnapshot`에 원시 counter 추가
- `ServerObservedMetricsSnapshot.FromTelemetry`에서 rate 계산 추가

예상 method:

```csharp
void RecordSendRequested(int bytes, int queuedBytes);
void RecordSendCompleted();
void RecordSendBackpressure();
```

`RecordSendCompleted`는 byte 수와 packet 수는 기존 `RecordSent(bytes)`가 담당하므로 pending count 감소에만 집중한다. 다만 실제 코드 단순화를 위해 `RecordSent(bytes)` 내부에서 completion을 함께 처리하는 방식도 허용한다.

### 5.2 `LibNetworks.Sessions.BaseSession`

대상 파일:

- `LibNetworks/Sessions/BaseSession.cs`

변경 방향:

- `RequestSendBuffers`에서 send request와 enqueue 후 queue depth를 기록한다.
- `OnSocketEventsSentCompleted`에서 send completion을 기록한다.
- `DoWorkSendBuffers`에서 send buffer depth sample을 기록한다.
- backpressure threshold는 우선 conservative constant로 둔다.

초기 threshold 후보:

- pending send requests > 2,000
- per-session send buffer bytes > 1 MiB

threshold는 fail 기준이 아니라 병목 signal count 용도다.

### 5.3 `FastPortLoadRunner`

대상 파일:

- `FastPortLoadRunner/Metrics.cs`
- `FastPortLoadRunner/LoadSession.cs`
- `FastPortLoadRunner/ObservedMetricsExtensions.cs`

변경 방향:

- connect attempt/failure를 명시적으로 기록한다.
- sent packet 증가 시 pending request를 증가시킨다.
- received packet 증가 시 pending request를 감소시킨다.
- max pending request를 유지한다.
- metrics reporter loop에서 expected wake time과 actual wake time 차이를 drift로 기록한다.
- JSON serialization은 기존 `ObservedMetricsJson` 경로를 유지한다.

### 5.4 `FastPortLoadValidation`

대상 파일:

- `FastPortLoadValidation/JsonlObservedMetricsReader.cs`
- `FastPortLoadValidation/LoadValidationEvaluator.cs`
- `FastPortLoadValidation/LoadValidationStageSummary.cs`
- `FastPortLoadValidation/LoadValidationSummaryWriter.cs`

변경 방향:

- JSONL reader는 새 필드가 없는 이전 JSONL도 읽을 수 있어야 한다.
- stage summary에 bottleneck max fields를 추가한다.
- Markdown summary table은 너무 넓어지지 않도록 주요 병목 지표만 표시한다.
- 상세 JSON에는 모든 bottleneck field를 포함한다.

Markdown summary 추가 후보:

- `Max Pending Req`
- `Max Scheduler Drift`
- `RTT P95`
- `RTT P99`

## 6. Data Model

### 6.1 `ServerTelemetrySnapshot`

추가 property:

```csharp
long SendRequests,
long PendingSendRequests,
long MaxPendingSendRequests,
long SendBackpressureEvents,
long MaxSendBufferBytes
```

### 6.2 `ServerObservedMetricsSnapshot`

추가 property:

```csharp
long TotalSendRequests,
long PendingSendRequests,
long MaxPendingSendRequests,
double SendRequestsPerSecond,
double SendBackpressureEventsPerSecond,
long SendBackpressureEventCount,
long MaxSendBufferBytes
```

### 6.3 `ClientObservedMetricsSnapshot`

추가 property:

```csharp
long ConnectAttemptCount,
long ConnectFailureCount,
long PendingRequestCount,
long MaxPendingRequestCount,
double ActiveSessionRatio,
double SchedulerDriftAverageMs,
double SchedulerDriftMaxMs
```

### 6.4 `LoadValidationStageSummary`

추가 property:

```csharp
long MaxPendingRequestCount,
double MaxSchedulerDriftMs,
double MaxActiveSessionRatio
```

server JSONL까지 병합하는 단계에서는 다음 property를 추가한다.

```csharp
long MaxPendingSendRequests,
long MaxSendBackpressureEvents,
long MaxSendBufferBytes
```

## 7. Implementation Order

1. `ServerTelemetry` contract 확장
2. `BaseSession` send request/completion/backpressure instrumentation 추가
3. `MetricsCollector` client bottleneck counter 추가
4. `LoadSession` connect/pending request instrumentation 추가
5. `ClientObservedMetricsSnapshot`와 extension mapping 확장
6. `FastPortLoadValidation` summary model/evaluator/writer 확장
7. 기존 telemetry serialization test와 load validation test 보강
8. `s5-random-10k` logging-off 재측정 수행
9. baseline과 재측정 결과를 `summary.md` 또는 별도 report로 비교

## 8. Test Plan

### 8.1 Unit Tests

- `ServerTelemetryCollector`:
  - send request 증가
  - send completion 후 pending 감소
  - max pending 유지
  - reset 시 추가 counter 초기화
- `ObservedMetricsJson`:
  - 새 client/server field camelCase serialization 확인
  - 기존 필드와 함께 deserialize 가능 확인
- `MetricsCollector`:
  - sent/received packet에 따른 pending request 증감
  - max pending request 유지
  - active session ratio 계산
- `LoadValidationEvaluator`:
  - 새 bottleneck max fields가 stage summary에 반영되는지 확인

### 8.2 Integration Tests

기존 regression:

```bash
dotnet build FastPortCharp.sln
dotnet test FastPortCharp.sln --no-build
```

smoke validation:

```bash
./FastPortLoadValidation/bin/Release/net10.0/FastPortLoadValidation \
  --profile smoke \
  --output artifacts/load-validation/smoke-bottleneck-telemetry
```

10K focused validation:

```bash
./FastPortLoadValidation/bin/Release/net10.0/FastPortLoadValidation \
  --profile staged \
  --stage s5-random-10k \
  --output artifacts/load-validation/s5-logging-off
```

### 8.3 Acceptance Criteria

- build/test가 통과한다.
- 기존 smoke/staged validation command가 깨지지 않는다.
- `summary.json`에 client bottleneck fields가 포함된다.
- `summary.md`에서 10K stage의 max pending request와 max scheduler drift를 확인할 수 있다.
- server-side bottleneck counter는 `LibNetworks`의 protocol-neutral API로 제공된다.
- 10K 재측정 결과가 baseline의 peak ratio, disconnect count, RTT P95/P99와 비교된다.

## 9. Risks

| Risk | Impact | Mitigation |
|------|--------|------------|
| telemetry 추가가 hot path를 더 느리게 함 | 10K 결과 왜곡 | allocation 없는 counter와 max update만 사용 |
| send buffer 전체 합산이 불가능함 | server backlog 해석 제한 | 첫 단계는 max sample을 제공하고, 필요 시 session registry 설계 |
| client pending request가 protocol에 종속됨 | 엔진 contract 오염 위험 | client-observed field로만 유지 |
| JSONL field 추가로 이전 artifact reader가 깨짐 | 분석 회귀 | 새 필드는 DTO 추가만 하고 기존 필드는 유지 |
| logging-off 재측정이 로컬 머신 상태에 민감함 | 비교 신뢰도 저하 | baseline command, ulimit, profile, stage id를 report에 같이 기록 |

## 10. Non-Goals

- MAUI dashboard 구현
- production metric exporter 구현
- distributed load runner 구현
- OS/kernel tuning 자동화
- RTT fail threshold 최종 확정
- game protocol template 구조화

## 11. Follow-up

이번 design 이후 `$pdca do 10k-load-bottleneck-telemetry` 단계에서 위 implementation order대로 코드를 변경한다. 구현 후 `$pdca analyze 10k-load-bottleneck-telemetry`에서 설계 대비 반영 여부를 확인하고, 10K focused validation 결과를 report 단계에 포함한다.
