# loadrunner-per-session-rtt-tail-telemetry - Plan Document

> Version: 1.0.0 | Date: 2026-04-30 | Status: Completed
> Level: Starter

---

## 1. Overview

### 1.1 Purpose

`loadrunner-per-session-rtt-tail-telemetry`는 10K load validation에서 관측되는 높은 RTT P95/P99가 전체 세션에 고르게 퍼진 문제인지, 일부 세션의 starvation/tail 문제인지 구분하기 위한 LoadRunner 계측 feature다.

이번 feature는 네트워크 엔진을 고치지 않는다. `LibNetworks` send/receive path를 변경하지 않고, 테스트/검증 도구에서 세션별 RTT tail을 볼 수 있게 만든다.

목적은 다음 최적화 대상을 정확히 고르는 것이다.

- 전체 세션이 비슷하게 느리면 global throughput / pacing / server 처리량 문제로 본다.
- 일부 세션만 극단적으로 느리면 fairness, starvation, 특정 session backlog, receive timeout tail 문제로 본다.
- 세션별 RTT와 pending request / pacing window / socket classification을 함께 보면 LoadRunner pacing 문제와 server send tail 문제를 분리할 수 있다.

### 1.2 Background

직전 `basesession-send-channel-queue-lock-reduction` 결과는 구조적 안정성 개선과 남은 성능 문제를 동시에 보여줬다.

Latest candidate: `artifacts/load-validation/s5-send-channel-queue-batch-pool-adaptive/summary.md`

| Metric | Latest Result |
|--------|--------------:|
| Peak sessions | `9,975 / 10,000` |
| Final disconnects | `2` |
| Max TPS | `7,901.40` |
| Max pending request count | `38,246` |
| Max pending send requests | `1,282` |
| Server send backpressure events | `0` |
| `send\|IOException\|NoBufferSpaceAvailable` | `0` |
| `receive\|IOException\|TimedOut` | `1,266` |
| Socket error rate | `0.12%` |
| RTT P95 | `17,796.60ms` |
| RTT P99 | `27,398.15ms` |
| Max scheduler drift | `19.66ms` |

해석:

- 처음 10K 실패 baseline 대비 send backlog, disconnect, NoBuffer, server backpressure는 크게 개선됐다.
- 그러나 최신 adaptive reference 대비 TPS와 RTT tail은 아직 부족하다.
- 현재 RTT P95/P99는 전체 RTT sample 통합 분포라서, 어떤 세션들이 tail을 만들었는지 알 수 없다.

### 1.3 PM Framing

이번 feature의 판단 질문은 네 가지다.

1. RTT tail은 대부분의 세션에서 발생하는가, 일부 세션에서 집중되는가?
2. 느린 세션은 pending request가 많이 쌓이는 세션인가?
3. 느린 세션은 adaptive pacing window가 낮게 고정되거나 자주 감소하는 세션인가?
4. receive timeout이 발생한 세션과 high-RTT 세션이 겹치는가?

이 답을 얻기 전에는 엔진 send path를 추가로 튜닝해도 원인이 빗나갈 수 있다.

## 2. Goals

### 2.1 Primary Goals

- [x] 이 feature의 범위를 LoadRunner / LoadValidation 계측으로 제한한다.
- [ ] LoadRunner가 RTT sample을 세션 ID와 함께 기록할 수 있게 한다.
- [ ] 전체 RTT P50/P95/P99는 기존처럼 유지한다.
- [ ] 세션별 RTT summary를 계산한다.
- [ ] 세션별 RTT P95 분포를 summary에 추가한다.
- [ ] 가장 느린 세션 Top N을 JSONL 또는 summary artifact에 남긴다.
- [ ] 샘플 수가 너무 적은 세션을 tail 판단에서 제외할 수 있게 한다.
- [ ] 세션별 RTT 지표를 pending request, pacing window, socket error/timeout과 연결할 수 있는 최소 필드를 정의한다.
- [ ] focused 10K 재실행 없이도 unit/summary tests로 schema와 계산을 검증한다.

### 2.2 Non-Goals

- `LibNetworks/Sessions/BaseSession.cs`를 변경하지 않는다.
- 서버 send queue, receive parser, socket option, protocol format을 변경하지 않는다.
- RTT 수치를 낮추는 최적화는 이번 feature의 목표가 아니다.
- adaptive pacing 정책을 변경하지 않는다.
- 10K 성능 pass/fail 기준을 새로 정하지 않는다.
- 모든 RTT raw sample을 무제한 저장하지 않는다.

## 3. Scope

### 3.1 In Scope

- `FastPortLoadRunner/LoadSession.cs`
  - echoed `RequestId`에서 session ID를 복원하거나, local `sessionId` context와 함께 RTT를 기록한다.
- `FastPortLoadRunner/Metrics.cs`
  - 세션별 RTT sample aggregation 또는 bounded sample reservoir 추가.
  - 세션별 summary snapshot 추가.
- `FastPortLoadRunner/ObservedMetricsExtensions.cs`
  - observed JSONL에 세션별 RTT tail summary field 추가.
- `LibNetworks/Telemetry/ObservedMetrics.cs`
  - LoadValidation이 읽을 DTO에 새 metric field 추가.
- `FastPortLoadValidation/JsonlObservedMetricsReader.cs`
  - 새 observed metric field deserialize 지원.
- `FastPortLoadValidation/LoadValidationEvaluator.cs`
  - stage summary에 세션별 RTT tail max/percentile 반영.
- `FastPortLoadValidation/LoadValidationSummaryWriter.cs`
  - Markdown summary에 핵심 세션별 RTT 지표 추가.
- Tests
  - 세션별 RTT percentile 계산.
  - minimum sample threshold.
  - top slow sessions ordering.
  - observed JSONL deserialize.
  - validation summary rendering.

### 3.2 Out of Scope

- 엔진 send/receive path 변경.
- server-side per-session latency decomposition.
- server receive timestamp / server send timestamp 기반 phase latency 분해.
- external metrics backend integration.
- UI dashboard.
- binary trace format.
- production telemetry API 변경.

## 4. Success Criteria

### 4.1 Functional Criteria

- [ ] LoadRunner snapshot에 기존 global RTT metrics가 계속 포함된다.
- [ ] LoadRunner snapshot에 세션별 RTT tail summary가 추가된다.
- [ ] 세션별 RTT P95/P99 또는 max RTT를 기준으로 slow session Top N을 볼 수 있다.
- [ ] summary에서 최소 sample 수 미만 세션을 제외한 값과 제외 수를 확인할 수 있다.
- [ ] JSONL schema가 기존 reader와 호환된다.
- [ ] LoadValidation summary에 세션별 tail 지표가 표시된다.
- [ ] 새 metric이 없던 과거 JSONL도 deserialize 가능해야 한다.

### 4.2 Diagnostic Criteria

다음 10K 재측정 후 아래 판단 중 하나를 명확히 할 수 있어야 한다.

| Question | Required Signal |
|----------|-----------------|
| 전체 세션이 느린가? | session RTT P95 distribution의 P50/P95/P99 |
| 일부 세션만 느린가? | slow session Top N과 전체 세션 중 tail concentration |
| 샘플 부족 세션 때문에 왜곡됐는가? | excluded low-sample session count |
| receive timeout 세션이 tail과 겹치는가? | session-level timeout/error count or top-session correlation field |
| pacing이 tail을 만드는가? | session-level pacing window/wait summary or follow-up hook |

### 4.3 Performance Criteria

- 10K validation JSONL 크기가 과도하게 증가하지 않아야 한다.
- per-session telemetry 때문에 LoadRunner send/receive hot path가 눈에 띄게 느려지면 안 된다.
- bounded sample strategy 또는 compact aggregation을 사용한다.
- `dotnet test FastPortCharp.sln --no-build`가 통과해야 한다.

## 5. Candidate Design Directions

### 5.1 Per-Session Bounded RTT Samples

각 session ID별로 작은 bounded queue/ring buffer를 유지하고 snapshot 시 percentile을 계산한다.

장점:

- 구현이 단순하다.
- 기존 global RTT percentile 계산 방식과 유사하다.
- slow session Top N 계산이 쉽다.

위험:

- 10K sessions * samples per session 만큼 메모리가 필요하다.
- snapshot마다 많은 배열 정렬이 발생할 수 있다.

### 5.2 Per-Session Histogram

각 session ID별로 fixed bucket histogram을 유지한다.

장점:

- 메모리 사용량 예측이 쉽다.
- snapshot 비용이 sample sort보다 낮을 수 있다.

위험:

- bucket 설계가 필요하다.
- 정확한 percentile이 아니라 근사값이 된다.
- 기존 code style보다 복잡하다.

### 5.3 Hybrid Summary

초기 구현은 bounded sample per session으로 시작하고, 문제가 확인되면 histogram으로 전환한다.

권장 방향:

- 첫 구현은 bounded sample으로 한다.
- session count가 10K이고 per-session sample cap을 작게 잡으면 충분히 관리 가능하다.
- JSONL에는 raw per-session samples를 쓰지 않고 summary만 쓴다.

## 6. Proposed Output Metrics

### 6.1 Snapshot Fields

후보 field:

- `sessionRttTrackedSessionCount`
- `sessionRttEligibleSessionCount`
- `sessionRttExcludedLowSampleSessionCount`
- `sessionRttMinSamplesPerSession`
- `sessionRttP95OfSessionP95Ms`
- `sessionRttP99OfSessionP95Ms`
- `sessionRttMaxSessionP95Ms`
- `sessionRttMaxSessionP99Ms`
- `sessionRttMaxSessionMaxMs`
- `slowestSessionRtt[]`

### 6.2 Slow Session Entry

후보 shape:

```json
{
  "sessionId": 1234,
  "sampleCount": 281,
  "rttP50Ms": 120.5,
  "rttP95Ms": 17890.1,
  "rttP99Ms": 27398.2,
  "rttMaxMs": 30120.4
}
```

Top N 기본값은 20개로 충분하다. 추후 필요하면 CLI option으로 확장한다.

## 7. Measurement Plan

### 7.1 Required Validation

1. Unit tests for per-session percentile calculation.
2. Unit tests for minimum sample filtering.
3. Unit tests for slow session Top N ordering.
4. Observed JSONL serialization/deserialization test.
5. LoadValidation summary rendering test.
6. Smoke run to verify JSONL and Markdown output.

### 7.2 Optional Validation

focused 10K 재실행은 design/do 이후 선택한다. 이 feature는 측정 장치 추가가 목적이므로, unit/smoke로 schema와 계산을 먼저 검증하고 10K는 다음 분석 feature에서 실행해도 된다.

## 8. Schedule

| Phase | Target Date | Status |
|-------|------------|--------|
| Plan | 2026-04-30 | Completed |
| Design | 2026-04-30 | Pending |
| Implementation | TBD | Pending |
| Analyze | TBD | Pending |
| Report | TBD | Pending |

## 9. Risks & Mitigations

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| Per-session samples increase memory too much | Medium | Medium | Use small per-session cap and write only summary to JSONL |
| Snapshot percentile calculation is too expensive | Medium | Medium | Limit tracked sessions and samples; move to histogram if needed |
| JSONL becomes too large | Medium | Medium | Output aggregate fields plus Top N only |
| Low-sample sessions distort tail ranking | Medium | High | Add minimum sample threshold and excluded count |
| New metrics break old JSONL parsing | High | Low | Keep new fields optional and default to zero/null |
| 계측 feature가 엔진 수정으로 번짐 | Medium | Medium | Explicitly keep `LibNetworks` out of scope |
| Pacing/window correlation expands scope too far | Medium | Medium | Add minimal hooks only; detailed correlation can be follow-up |

## 10. Architecture Considerations

- RTT sample source is still client-side send timestamp and client-side receive timestamp.
- `RequestId` already encodes session ID as high 32 bits, but `LoadSession` also owns `sessionId`; design should choose one consistent source.
- JSONL should preserve backward compatibility with previous `ObservedMetrics`.
- Summary should answer diagnostic questions without requiring raw per-session dump parsing.
- Do not add locks on the send/receive hot path unless measurement shows it is harmless.
- Use existing `MetricsCollector` / `MetricsSnapshot` pattern instead of adding a new telemetry subsystem.

## 11. Open Questions For Design

1. Should session ID be taken from local `LoadSession.sessionId` or decoded from echoed `RequestId`?
2. What is the initial per-session sample cap: 128, 256, or 512?
3. What minimum sample count should make a session eligible for tail ranking?
4. Should slow session Top N be sorted by P95, P99, or max RTT?
5. Should receive timeout counts be tracked per session in the same feature?
6. Should session-level pacing window stats be included now or deferred?

## 12. References

- `FastPortLoadRunner/LoadSession.cs`
- `FastPortLoadRunner/Metrics.cs`
- `FastPortLoadRunner/ObservedMetricsExtensions.cs`
- `LibNetworks/Telemetry/ObservedMetrics.cs`
- `FastPortLoadValidation/JsonlObservedMetricsReader.cs`
- `FastPortLoadValidation/LoadValidationEvaluator.cs`
- `FastPortLoadValidation/LoadValidationSummaryWriter.cs`
- `docs/load-validation-benchmark-results.md`
- `docs/archive/2026-04/basesession-send-channel-queue-lock-reduction/basesession-send-channel-queue-lock-reduction.report.md`
- `docs/archive/2026-04/adaptive-client-send-pacing-and-rtt-stability/adaptive-client-send-pacing-and-rtt-stability.report.md`

## 13. Next Phase

Recommended next command:

```bash
$pdca design loadrunner-per-session-rtt-tail-telemetry
```
