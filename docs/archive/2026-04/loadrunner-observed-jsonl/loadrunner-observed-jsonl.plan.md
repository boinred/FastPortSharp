# loadrunner-observed-jsonl - Plan Document

> Version: 1.0.0 | Date: 2026-04-28 | Status: Completed
> Level: Starter

---

## 1. Overview

### 1.1 Purpose
FastPortLoadRunner의 JSONL metric output을 `ObservedMetricsSnapshot` contract에 맞춘다. LoadRunner가 내부 집계용으로 쓰는 `MetricsSnapshot`은 유지하되, 파일/파이프라인으로 내보내는 JSONL은 dashboard, smoke server, future telemetry exporter가 공유할 수 있는 observed metric envelope를 사용하도록 계획한다.

### 1.2 Background
`telemetry-export-metric-contract` 단계에서 server/client observed metric DTO와 JSON helper가 추가되었다. 현재 남은 차이는 `FastPortLoadRunner`의 `JsonMetricsReporter`가 여전히 내부 `MetricsSnapshot`을 그대로 직렬화한다는 점이다. 이 상태에서는 UI dashboard나 외부 분석기가 server/client 공통 metric contract를 기준으로 JSONL stream을 읽기 어렵다.

## 2. Goals

### 2.1 Primary Goals
- [x] LoadRunner JSONL output의 목표 contract를 `ObservedMetricsSnapshot` 기반으로 정한다.
- [x] 기존 console reporter와 runtime aggregation model은 유지하는 방향을 명확히 한다.
- [x] JSONL contract 검증 테스트 범위를 정의한다.
- [x] 후속 MAUI/dashboard가 재사용할 수 있는 field naming 기준을 유지한다.

### 2.2 Non-Goals
- MAUI dashboard 구현은 포함하지 않는다.
- HTTP/WebSocket telemetry export endpoint는 포함하지 않는다.
- FastPortServer core engine에 load test 전용 protocol이나 smoke telemetry를 넣지 않는다.
- LoadRunner 부하 생성 알고리즘, payload randomization, session scheduler를 변경하지 않는다.

## 3. Scope

### 3.1 In Scope
- `FastPortLoadRunner/Metrics.cs`의 `JsonMetricsReporter` 출력 형태 검토 및 변경 계획.
- `FastPortLoadRunner/ObservedMetricsExtensions.cs`의 client observed adapter 재사용.
- JSONL 한 줄을 `ObservedMetricsSnapshot.FromClient(snapshot.ToClientObservedMetricsSnapshot())` 형태로 내보내는 방향 검토.
- camelCase JSON naming 유지.
- JSONL contract test 추가 계획.
- 기존 ambiguous/internal JSON root field가 외부 contract로 새로 고정되지 않도록 방지.

### 3.2 Out of Scope
- `MetricsSnapshot` 내부 property rename.
- console text output 변경.
- server observed snapshot과 client observed snapshot을 같은 process에서 결합하는 기능.
- legacy JSONL compatibility switch 구현. 필요 시 design 단계에서 `legacy|observed` option을 별도 판단한다.

## 4. Proposed Direction

LoadRunner는 내부적으로 `MetricsSnapshot`을 계속 생산한다. JSONL reporter는 snapshot을 client observed DTO로 변환한 뒤 `ObservedMetricsSnapshot` envelope로 감싸서 serialize한다.

Expected JSONL shape:

```json
{"timestamp":"2026-04-28T00:00:00Z","clientObserved":{"targetSessions":10000,"currentSessions":10000,"totalSentPackets":123,"totalReceivedPackets":123},"serverObserved":null}
```

이 형태를 사용하면 future dashboard가 client-only, server-only, combined stream을 같은 top-level schema로 처리할 수 있다.

## 5. Success Criteria

- [ ] `--json-metrics` 또는 동일한 JSONL reporter path가 observed envelope JSON을 출력한다.
- [ ] JSON property naming은 camelCase로 유지된다.
- [ ] client metrics는 `clientObserved` 아래에 위치하고 `serverObserved`는 client-only stream에서 null로 표현된다.
- [ ] console reporter output은 기존 사람이 읽는 형태를 유지한다.
- [ ] unit test가 JSONL serialization shape와 representative field names를 검증한다.
- [ ] `dotnet test`가 통과한다.

## 6. Schedule

| Phase | Target Date | Status |
|-------|------------|--------|
| Plan | 2026-04-28 | Completed |
| Design | 2026-04-28 | Pending |
| Implementation | 2026-04-28 | Pending |
| Review | 2026-04-28 | Pending |

## 7. Risks & Mitigations

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| Existing JSONL consumer가 내부 `MetricsSnapshot` shape에 의존할 수 있음 | Medium | Low | design 단계에서 compatibility option 필요 여부를 확인하고, 문서에 intentional contract change를 명시한다. |
| observed DTO field와 loadrunner 내부 metric의 의미가 1:1이 아닐 수 있음 | Medium | Medium | `ObservedMetricsExtensions`에 mapping을 집중시키고 tests로 주요 field를 고정한다. |
| envelope가 client-only use case에서 장황해질 수 있음 | Low | Medium | dashboard/server와 같은 schema를 공유하는 장점을 우선하고, 필요 시 compact mode는 후속 feature로 분리한다. |

## 8. References

- `FastPortLoadRunner/Metrics.cs`
- `FastPortLoadRunner/ObservedMetricsExtensions.cs`
- `LibNetworks/Telemetry/ObservedMetrics.cs`
- `LibCommonTest/FastPortLoadRunnerTests.cs`
- `LibCommonTest/ObservedMetricsTests.cs`
- `docs/archive/2026-04/telemetry-export-metric-contract/telemetry-export-metric-contract.report.md`
