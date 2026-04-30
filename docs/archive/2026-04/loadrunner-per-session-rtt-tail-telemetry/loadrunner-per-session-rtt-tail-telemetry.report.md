# Completion Report: loadrunner-per-session-rtt-tail-telemetry

> Date: 2026-04-30 | Status: Completed | Match Rate: 100%

---

## 1. Summary

`loadrunner-per-session-rtt-tail-telemetry`는 10K load validation에서 global RTT P95/P99가 전체 세션에 퍼진 문제인지, 일부 세션 tail/starvation 문제인지 구분하기 위한 LoadRunner/LoadValidation 계측 feature다.

이번 작업은 의도대로 엔진 send/receive path를 변경하지 않았다. 변경 범위는 LoadRunner telemetry, observed JSON DTO, LoadValidation summary/evaluator, 그리고 관련 테스트로 제한했다.

최종 결과:

- Plan 완료
- Design 완료
- Do 완료
- Check 완료
- Act 완료
- Match rate 100%
- Unit tests 104/104 통과
- Debug/Release build 통과

## 2. Related Documents

- Plan: `docs/01-plan/features/loadrunner-per-session-rtt-tail-telemetry.plan.md`
- Design: `docs/02-design/features/loadrunner-per-session-rtt-tail-telemetry.design.md`
- Do Notes: `docs/02-design/features/loadrunner-per-session-rtt-tail-telemetry.do.md`
- Analysis: `docs/03-analysis/loadrunner-per-session-rtt-tail-telemetry.analysis.md`

## 3. Completed Items

### 3.1 Functional

- [x] `LoadSession.ParseEchoResponse`에서 RTT 기록 시 local `sessionId`를 함께 전달.
- [x] 기존 global RTT Average/P50/P95/P99 계산 유지.
- [x] session ID별 bounded RTT sample collector 추가.
- [x] session당 retained sample cap 256 적용.
- [x] tail 판단 최소 sample 8 적용.
- [x] session P95 distribution의 P50/P95/P99 계산.
- [x] max session P95/P99/max RTT 계산.
- [x] slow session Top 20을 JSONL summary에 포함.
- [x] Markdown summary에는 slow session Top 5 detail line 출력.
- [x] raw per-session RTT sample은 JSONL에 쓰지 않음.
- [x] `ClientObservedMetricsSnapshot.SessionRtt` optional field 추가.
- [x] 과거 JSONL처럼 `sessionRtt`가 없는 payload도 deserialize 가능.
- [x] `minSamplesPerSession` JSON field name을 설계 output contract와 일치시킴.
- [x] LoadValidation stage summary에 session RTT tail aggregate 추가.

### 3.2 Non-Goals Preserved

- [x] `LibNetworks/Sessions/BaseSession.cs` 변경 없음.
- [x] server send queue 변경 없음.
- [x] socket option 변경 없음.
- [x] protocol payload/header format 변경 없음.
- [x] adaptive pacing 정책 변경 없음.
- [x] 10K pass/fail 기준 변경 없음.

### 3.3 Test Coverage

주요 테스트:

- `LoadSession_ParseEchoResponse_RecordsSessionRtt`
- `MetricsCollector_RecordRtt_TracksSessionRttSummary`
- `MetricsCollector_RecordRtt_ExcludesLowSampleSessions`
- `MetricsCollector_RecordRtt_CapsPerSessionSamples`
- `MetricsCollector_RecordRtt_OrdersSlowestSessionsByTieBreakers`
- `MetricsCollector_RecordRtt_IsSafeAcrossConcurrentSessions`
- `ObservedMetricsJson_DeserializesClientPacingFields`
- `ObservedMetricsJson_DeserializesClientWithoutSessionRtt`
- `ClientObservedMetricsSnapshot_MapsLoadRunnerMetrics`
- `JsonMetricsReporter_SerializeSnapshot_WritesObservedClientEnvelope`
- `LoadValidationEvaluator_IncludesMergedServerAndSocketClassifications`
- `LoadValidationSummaryWriter_WritesSummaryFiles`

검증된 계약:

- session RTT summary 생성
- low-sample session 제외
- retained sample cap
- slow session tie-break ordering
- JSON output contract
- old JSON backward compatibility
- LoadRunner snapshot to observed DTO mapping
- LoadValidation evaluator aggregation
- Markdown summary rendering

## 4. Quality Metrics

| Metric | Result |
|--------|--------|
| Match rate | 100% |
| PDCA iteration count | 2 |
| Unit tests | 104 passed / 0 failed |
| Debug build | Passed, 0 warnings |
| Release build | Passed, 0 warnings |
| `git diff --check` | Passed |

Verification commands:

```text
dotnet build FastPortCharp.sln
dotnet test FastPortCharp.sln --no-build
dotnet build FastPortCharp.sln -c Release
git diff --check
```

## 5. Implementation Notes

### 5.1 Runtime Collection

`MetricsCollector` now records RTT in two paths:

- Existing global RTT queue for aggregate P50/P95/P99.
- Per-session bounded collector for tail concentration analysis.

The compatibility method remains:

```csharp
RecordRtt(long clientSendTimestamp, long clientReceiveTimestamp)
```

The new method records session context:

```csharp
RecordRtt(int sessionId, long clientSendTimestamp, long clientReceiveTimestamp)
```

### 5.2 Output Shape

Observed client JSON now includes optional `sessionRtt`.

Important fields:

- `trackedSessionCount`
- `eligibleSessionCount`
- `excludedLowSampleSessionCount`
- `minSamplesPerSession`
- `p50OfSessionP95Ms`
- `p95OfSessionP95Ms`
- `p99OfSessionP95Ms`
- `maxSessionP95Ms`
- `maxSessionP99Ms`
- `maxSessionMaxMs`
- `slowestSessions`

### 5.3 LoadValidation Summary

Markdown summary now includes:

- compact `Session RTT` table column
- low-sample excluded count detail
- slow session Top 5 detail

This is enough to answer the next diagnostic question:

```text
RTT tail이 전체 세션에 넓게 퍼졌는가, 아니면 일부 세션에 집중됐는가?
```

## 6. Lessons Learned

### Keep

- Engine path를 바로 수정하지 않고 telemetry를 먼저 보강한 방향은 적절했다.
- JSONL raw sample을 쓰지 않고 compact summary만 쓰는 방식은 10K artifact 크기 리스크를 줄인다.
- Unit test를 계산 로직뿐 아니라 실제 `ParseEchoResponse` 호출 경로와 JSON envelope까지 확장한 것은 좋은 방어선이다.

### Problem

- 첫 analysis에서 output field name이 설계와 달랐다.
- slow session ordering 구현은 있었지만 tie-break 전용 테스트가 처음에는 없었다.
- 실제 smoke/load validation artifact 검증은 아직 수행하지 않았다.

### Try

- 다음 telemetry feature에서는 public JSON field name을 설계 직후 테스트에 먼저 고정한다.
- summary writer 변경 시 table text뿐 아니라 JSON field 계약도 같이 검증한다.
- 10K 재측정 전 smoke artifact를 한 번 만들어 `sessionRtt` 출력 shape를 눈으로 확인한다.

## 7. Remaining Risk

- 실제 10K load에서 per-session RTT collector overhead는 아직 runtime artifact로 측정하지 않았다.
- session-level socket error/timeout correlation은 이번 feature scope 밖으로 남았다.
- 현재 telemetry는 client-side RTT 기준이다. server receive/send phase decomposition은 별도 feature가 필요하다.

## 8. Next Steps

1. `$pdca archive loadrunner-per-session-rtt-tail-telemetry`
2. smoke 또는 focused load validation 실행으로 실제 `sessionRtt` JSONL/summary 출력 확인
3. 10K 재측정 후 다음을 판단
   - session P95 distribution이 전체적으로 높은지
   - slow session Top N에 tail이 집중되는지
   - low-sample excluded count가 tail 해석을 왜곡하는지
4. 결과에 따라 다음 feature 선택
   - 전체 slowdown이면 throughput/pacing/server 처리량 계측 강화
   - 일부 session tail이면 fairness/starvation/session backlog 분석
