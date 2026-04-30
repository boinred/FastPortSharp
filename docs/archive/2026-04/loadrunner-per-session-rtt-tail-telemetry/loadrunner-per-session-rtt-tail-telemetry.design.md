# loadrunner-per-session-rtt-tail-telemetry - Design Document

> Version: 1.0.0 | Date: 2026-04-30 | Status: Completed
> Level: Starter | Plan: docs/01-plan/features/loadrunner-per-session-rtt-tail-telemetry.plan.md

---

## 1. Overview

`loadrunner-per-session-rtt-tail-telemetry`는 LoadRunner가 이미 계산하는 global RTT P50/P95/P99에 더해, session-level RTT tail summary를 추가하는 feature다.

이번 설계는 엔진단을 건드리지 않는다.

Out of engine scope:

- `LibNetworks/Sessions/BaseSession.cs`
- server send queue
- socket option
- protocol payload/header format

In scope:

- `FastPortLoadRunner`에서 RTT sample을 session ID와 함께 기록
- `MetricsSnapshot` / `ClientObservedMetricsSnapshot`에 compact session RTT summary 추가
- `FastPortLoadValidation`에서 stage summary와 Markdown에 session RTT tail 정보 표시
- 기존 JSONL / summary reader와 backward compatibility 유지

핵심 질문은 다음과 같다.

```text
RTT tail이 전체 세션에 넓게 퍼진 문제인가?
아니면 일부 세션만 극단적으로 느린 문제인가?
```

## 2. Existing Flow

현재 RTT 기록 흐름:

```text
LoadSession.CreateEchoRequestPacket
  -> RequestId = sessionId << 32 | requestId
  -> ClientSendTs = Stopwatch.GetTimestamp()

LoadSession.ParseEchoResponse
  -> EchoResponse.Header.ClientSendTs 읽기
  -> clientReceiveTs = Stopwatch.GetTimestamp()
  -> _pacer.OnResponse(rttMs)
  -> metricsCollector.RecordRtt(clientSendTs, clientReceiveTs)

MetricsCollector.RecordRtt
  -> global _rttSamplesMs.Enqueue(elapsedMs)

MetricsCollector.CreateSnapshot
  -> global RTT Average/P50/P95/P99 계산

MetricsSnapshot.ToClientObservedMetricsSnapshot
  -> JSONL clientObserved field로 출력

FastPortLoadValidation
  -> JSONL 읽기
  -> stage MaxRttP95Ms / MaxRttP99Ms 계산
  -> summary.md table에 RTT P95/P99 출력
```

현재 문제:

- `RecordRtt`가 session ID를 받지 않는다.
- RTT sample은 global queue에만 들어간다.
- slow session Top N을 계산할 수 없다.
- global RTT P95/P99가 일부 session starvation 때문인지 전체 slowdown인지 판단할 수 없다.

## 3. Proposed Architecture

### 3.1 High-Level Flow

새 RTT 기록 흐름:

```text
LoadSession.ParseEchoResponse
  -> sessionId는 LoadSession 생성자 값 사용
  -> clientSendTs / clientReceiveTs로 rttMs 계산
  -> _pacer.OnResponse(rttMs)
  -> metricsCollector.RecordRtt(sessionId, clientSendTs, clientReceiveTs)

MetricsCollector.RecordRtt
  -> 기존 global RTT queue에 기록
  -> session ID별 bounded sample collector에 기록

MetricsCollector.CreateSnapshot
  -> global RTT summary 계산
  -> session-level RTT summary 계산
  -> MetricsSnapshot에 포함

ObservedMetricsExtensions
  -> MetricsSnapshot.SessionRtt -> ClientObservedMetricsSnapshot.SessionRtt

FastPortLoadValidation
  -> ClientObservedMetricsSnapshot.SessionRtt 읽기
  -> stage-level max/last session RTT summary 선택
  -> summary.json / summary.md 출력
```

### 3.2 Design Choice

첫 구현은 per-session bounded sample 방식으로 한다.

선택 이유:

- 기존 global RTT percentile 구현과 비슷해서 구현/검증이 단순하다.
- 세션별 slow Top N 계산이 쉽다.
- 10K 세션에서 per-session cap을 작게 두면 메모리 상한을 예측할 수 있다.
- histogram은 더 효율적일 수 있지만 bucket 설계가 필요하고 진단 정확도를 낮출 수 있다.

기본 상수 후보:

```csharp
private const int MaxRttSamples = 100_000;
private const int MaxSessionRttSamplesPerSession = 256;
private const int MinSessionRttSamplesForTail = 8;
private const int MaxSlowSessionRttEntries = 20;
```

## 4. Data Model

### 4.1 Session RTT Runtime Collector

`FastPortLoadRunner/Metrics.cs` 내부에 private helper type을 추가한다.

```csharp
private sealed class SessionRttSamples
{
    private readonly object _gate = new();
    private readonly Queue<double> _samples = new();
    private readonly int _capacity;
    private long _totalSampleCount;

    public SessionRttSamples(int capacity);
    public void Add(double rttMs);
    public SessionRttSnapshot CreateSnapshot(int sessionId);
}
```

설계 의도:

- session별 collector라 lock contention은 같은 session의 receive loop로 제한된다.
- `Queue<double>`는 capacity를 넘으면 오래된 sample을 제거한다.
- `_totalSampleCount`는 총 관측 수를 유지하고, `SampleCount`는 현재 retained sample 수를 의미한다.
- Snapshot 시 복사 후 정렬한다.

### 4.2 SessionRttSnapshot

LoadRunner 내부 계산 결과:

```csharp
internal sealed record SessionRttSnapshot(
    int SessionId,
    int SampleCount,
    long TotalSampleCount,
    double RttAverageMs,
    double RttP50Ms,
    double RttP95Ms,
    double RttP99Ms,
    double RttMaxMs);
```

`SessionRttSnapshot`은 raw sample을 포함하지 않는다.

### 4.3 SessionRttSummary

JSONL에 포함할 compact summary:

```csharp
public sealed record SessionRttSummarySnapshot(
    int TrackedSessionCount,
    int EligibleSessionCount,
    int ExcludedLowSampleSessionCount,
    int MinSamplesPerSession,
    double P50OfSessionP95Ms,
    double P95OfSessionP95Ms,
    double P99OfSessionP95Ms,
    double MaxSessionP95Ms,
    double MaxSessionP99Ms,
    double MaxSessionMaxMs,
    IReadOnlyList<SlowSessionRttSnapshot> SlowestSessions);
```

### 4.4 SlowSessionRttSnapshot

Slow Top N entry:

```csharp
public sealed record SlowSessionRttSnapshot(
    int SessionId,
    int SampleCount,
    long TotalSampleCount,
    double RttAverageMs,
    double RttP50Ms,
    double RttP95Ms,
    double RttP99Ms,
    double RttMaxMs);
```

`SlowSessionRttSnapshot`는 `LibNetworks.Telemetry`에 둔다. 이유는 `ClientObservedMetricsSnapshot`이 `LibNetworks.Telemetry` public record이고 JSON deserialize 대상이기 때문이다.

### 4.5 MetricsSnapshot Extension

`FastPortLoadRunner/Metrics.cs`의 `MetricsSnapshot`에 optional field를 추가한다.

```csharp
SessionRttSummarySnapshot? SessionRtt = null
```

### 4.6 ClientObservedMetricsSnapshot Extension

`LibNetworks/Telemetry/ObservedMetrics.cs`의 `ClientObservedMetricsSnapshot` 마지막 optional parameter로 추가한다.

```csharp
SessionRttSummarySnapshot? SessionRtt = null
```

Backward compatibility:

- 새 field는 optional nullable이다.
- 과거 JSONL에는 `sessionRtt`가 없으므로 deserialize 시 `null`이어야 한다.
- 기존 positional 생성 호출은 마지막 optional parameter 덕분에 유지된다.

## 5. Algorithms

### 5.1 RecordRtt

기존 method는 compatibility helper로 남긴다.

```csharp
public void RecordRtt(long clientSendTimestamp, long clientReceiveTimestamp)
{
    RecordRtt(sessionId: 0, clientSendTimestamp, clientReceiveTimestamp);
}
```

새 method:

```csharp
public void RecordRtt(int sessionId, long clientSendTimestamp, long clientReceiveTimestamp)
```

동작:

1. elapsed ticks가 0 이하이면 return.
2. elapsedMs 계산.
3. 기존 global `_rttSamplesMs`에 enqueue.
4. `sessionId > 0`이면 `_sessionRttSamples.GetOrAdd(sessionId, ...)`에 추가.
5. global queue cap 유지.

### 5.2 Session Summary Calculation

```text
allSessionSnapshots = session collectors snapshot
eligible = snapshots where SampleCount >= MinSessionRttSamplesForTail
excluded = tracked - eligible
sessionP95Values = eligible.Select(RttP95Ms)

P50OfSessionP95 = percentile(sessionP95Values, 50)
P95OfSessionP95 = percentile(sessionP95Values, 95)
P99OfSessionP95 = percentile(sessionP95Values, 99)
MaxSessionP95 = max eligible RttP95Ms
MaxSessionP99 = max eligible RttP99Ms
MaxSessionMax = max eligible RttMaxMs
SlowestSessions = eligible ordered by RttP95 desc, RttP99 desc, RttMax desc, SessionId asc take 20
```

If no eligible sessions exist:

- counts are still reported;
- percentile/max fields are zero;
- `SlowestSessions` is empty.

### 5.3 Percentile Calculation

Reuse existing linear interpolation percentile behavior.

Design constraint:

- keep a single helper in `MetricsCollector` or extract to a private static method reused by global and session RTT calculations.
- avoid changing numeric behavior of existing global RTT.

## 6. Output Contract

### 6.1 JSONL Shape

Client observed JSONL gains one optional nested object:

```json
{
  "clientObserved": {
    "rttP95Ms": 17796.6,
    "rttP99Ms": 27398.1,
    "sessionRtt": {
      "trackedSessionCount": 9975,
      "eligibleSessionCount": 9900,
      "excludedLowSampleSessionCount": 75,
      "minSamplesPerSession": 8,
      "p50OfSessionP95Ms": 4500.0,
      "p95OfSessionP95Ms": 18000.0,
      "p99OfSessionP95Ms": 26000.0,
      "maxSessionP95Ms": 30100.0,
      "maxSessionP99Ms": 42000.0,
      "maxSessionMaxMs": 50000.0,
      "slowestSessions": [
        {
          "sessionId": 1234,
          "sampleCount": 256,
          "totalSampleCount": 281,
          "rttAverageMs": 8200.0,
          "rttP50Ms": 3000.0,
          "rttP95Ms": 30100.0,
          "rttP99Ms": 42000.0,
          "rttMaxMs": 50000.0
        }
      ]
    }
  }
}
```

Raw session samples are never written.

### 6.2 LoadValidationStageSummary Fields

Add these optional fields:

```csharp
int SessionRttTrackedSessionCount = 0;
int SessionRttEligibleSessionCount = 0;
int SessionRttExcludedLowSampleSessionCount = 0;
double MaxSessionRttP95OfP95Ms = 0;
double MaxSessionRttP99OfP95Ms = 0;
double MaxSessionRttMaxSessionP95Ms = 0;
double MaxSessionRttMaxSessionP99Ms = 0;
double MaxSessionRttMaxSessionMaxMs = 0;
IReadOnlyList<SlowSessionRttSnapshot>? SlowestSessions = null;
```

Naming uses `Max...` because `LoadValidationEvaluator` summarizes across multiple JSONL snapshots and keeps the worst observed stage value.

### 6.3 Markdown Summary

The existing table is already wide. Do not add many columns to the main table.

Add compact main table column:

```text
Session RTT
```

Format:

```text
eligible=9900/9975, p95-of-p95=18000.00ms, max-p95=30100.00ms
```

Then add detail lines after each stage:

```text
- s5-random-10k: session RTT excluded low-sample sessions = 75
- s5-random-10k: slow session 1234 p95=30100.00ms p99=42000.00ms max=50000.00ms samples=256/281
```

Limit detail lines to top 5 in Markdown even if JSON carries top 20.

## 7. Error / Timeout Correlation

This design does not fully implement per-session socket error classification. Current `RecordSocketError` does not receive session ID and exceptions are recorded at phase level.

For this feature:

- session RTT Top N is implemented now;
- global socket classification remains as-is;
- per-session timeout/error correlation is left as a follow-up unless it can be added without broad scope.

Reasoning:

The immediate diagnostic gap is session-level RTT distribution. Per-session error correlation is useful, but it requires changing error recording calls and can be added after RTT tail concentration is visible.

## 8. Files To Change

### 8.1 FastPortLoadRunner

| File | Change |
|------|--------|
| `FastPortLoadRunner/LoadSession.cs` | call `metricsCollector.RecordRtt(sessionId, clientSendTs, clientReceiveTs)` |
| `FastPortLoadRunner/Metrics.cs` | add session RTT collectors, summary records, snapshot fields, percentile helpers |
| `FastPortLoadRunner/ObservedMetricsExtensions.cs` | map `MetricsSnapshot.SessionRtt` into `ClientObservedMetricsSnapshot.SessionRtt` |

### 8.2 LibNetworks.Telemetry

| File | Change |
|------|--------|
| `LibNetworks/Telemetry/ObservedMetrics.cs` | add `SessionRttSummarySnapshot`, `SlowSessionRttSnapshot`, optional `ClientObservedMetricsSnapshot.SessionRtt` |

### 8.3 FastPortLoadValidation

| File | Change |
|------|--------|
| `FastPortLoadValidation/LoadValidationStage.cs` | add stage summary session RTT fields |
| `FastPortLoadValidation/LoadValidationEvaluator.cs` | aggregate worst session RTT summary across client samples |
| `FastPortLoadValidation/LoadValidationSummaryWriter.cs` | add compact session RTT output and slow-session detail lines |

### 8.4 Tests

| File | Change |
|------|--------|
| `LibCommonTest/FastPortLoadRunnerTests.cs` | session RTT sample, min-sample filter, Top N ordering |
| `LibCommonTest/ObservedMetricsTests.cs` | JSON serialization/deserialization and mapping |
| `LibCommonTest/FastPortLoadValidationTests.cs` | evaluator summary and Markdown rendering |

## 9. Implementation Order

1. Add telemetry DTOs in `LibNetworks/Telemetry/ObservedMetrics.cs`.
2. Extend `MetricsSnapshot` and `ObservedMetricsExtensions` mapping.
3. Add `MetricsCollector.RecordRtt(int sessionId, ...)` and session sample collectors.
4. Update `LoadSession.ParseEchoResponse` to pass local `sessionId`.
5. Add LoadRunner unit tests for session RTT calculations.
6. Extend `LoadValidationStageSummary`.
7. Extend `LoadValidationEvaluator` to choose worst stage session RTT values.
8. Extend `LoadValidationSummaryWriter` Markdown output.
9. Add ObservedMetrics and LoadValidation tests.
10. Run build/test.
11. Run smoke validation only if needed to verify real JSONL/summary output.

## 10. Test Plan

### 10.1 Unit Tests

Add or update tests:

- `MetricsCollector_RecordRtt_TracksSessionRttSummary`
  - record RTT samples for session 1 and 2;
  - assert tracked/eligible counts;
  - assert per-session P95 distribution.
- `MetricsCollector_RecordRtt_ExcludesLowSampleSessions`
  - session below threshold excluded;
  - excluded count increments.
- `MetricsCollector_RecordRtt_OrdersSlowestSessions`
  - sort by P95 desc, then P99 desc, then max desc, then session ID asc.
- `ObservedMetricsJson_DeserializesClientSessionRttFields`
  - verify camelCase JSON and optional field round-trip.
- `ClientObservedMetricsSnapshot_MapsSessionRtt`
  - verify `MetricsSnapshot.ToClientObservedMetricsSnapshot`.
- `LoadValidationEvaluator_IncludesSessionRttTailSummary`
  - verify stage summary fields.
- `LoadValidationSummaryWriter_WritesSessionRttDetails`
  - verify main table and slow session detail lines.
- `JsonlObservedMetricsReader_ReadsOlderClientObservedSamples`
  - JSON without `sessionRtt` still reads successfully.

### 10.2 Command Verification

Required:

```bash
dotnet build FastPortCharp.sln
dotnet test FastPortCharp.sln --no-build
```

Optional after implementation:

```bash
dotnet build FastPortCharp.sln -c Release
```

Optional smoke:

```bash
./FastPortLoadValidation/bin/Release/net10.0/FastPortLoadValidation \
  --profile smoke \
  --stage smoke-random-25 \
  --output artifacts/load-validation/per-session-rtt-smoke \
  --server-metrics artifacts/load-validation/per-session-rtt-smoke/server.metrics.jsonl
```

## 11. Performance Constraints

- Do not write raw per-session RTT samples to JSONL.
- Keep slow session list to top 20 in JSON.
- Keep Markdown slow session detail to top 5.
- Use per-session sample cap of 256 unless implementation measurement suggests otherwise.
- Keep global `_rttSamplesMs` behavior unchanged.
- Avoid global lock around all session RTT writes.

## 12. Decisions

| Decision | Choice | Reason |
|----------|--------|--------|
| Session ID source | local `LoadSession.sessionId` | already available and avoids decoding request ID in receive path |
| Runtime storage | per-session bounded samples | simpler than histogram for first diagnostic implementation |
| Raw output | no raw samples | prevents JSONL growth |
| Summary output | aggregate fields + Top N | enough to identify tail concentration |
| Min sample threshold | 8 | avoids ranking sessions with only one or two responses |
| Top N | 20 JSON / 5 Markdown | useful detail without noisy summaries |
| Engine changes | none | feature is diagnostic tooling only |

## 13. Open Questions

1. Should sample cap and min sample threshold become CLI options later?
2. Should slow session Top N sort by P95 or max RTT for the final report? Current design chooses P95 first.
3. Should per-session receive timeout/error counts be added in the next feature?
4. Should session RTT summary be included in console output, or only JSONL/Markdown?

## 14. Next Phase

Recommended next command:

```bash
$pdca do loadrunner-per-session-rtt-tail-telemetry
```
