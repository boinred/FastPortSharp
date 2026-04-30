# Gap Analysis: loadrunner-per-session-rtt-tail-telemetry

> Date: 2026-04-30 | Design: docs/02-design/features/loadrunner-per-session-rtt-tail-telemetry.design.md

---

## Match Rate: 100%

계산 기준:

- 설계의 필수 구현 항목 25개 중 25개가 코드/테스트로 확인됨.
- optional smoke validation은 설계상 선택 항목이라 match rate 분모에서 제외함.
- Act 단계에서 public JSON field naming 차이와 slow-session tie-break 전용 테스트 gap을 해소함.

## Summary

`loadrunner-per-session-rtt-tail-telemetry` 구현은 설계의 핵심 목표와 output contract에 일치한다.

확인된 핵심 일치 항목:

- 엔진 send/receive path를 변경하지 않고 LoadRunner/LoadValidation 계측만 확장했다.
- `LoadSession.ParseEchoResponse`가 local `sessionId`와 함께 RTT를 기록한다.
- 기존 global RTT P50/P95/P99 계산은 유지했다.
- session ID별 bounded RTT sample collector를 추가했다.
- session당 retained sample cap 256, tail 판단 최소 sample 8, slow session Top 20을 구현했다.
- JSONL에는 raw RTT sample을 쓰지 않고 compact `sessionRtt` summary만 추가했다.
- LoadValidation summary JSON/Markdown에 session RTT tail 정보를 집계한다.
- 과거 JSONL처럼 `sessionRtt`가 없는 client observed sample도 deserialize 가능하다.
- unit test는 계산, 호출 경로, slow-session tie-break, JSON envelope, backward compatibility, Validation summary 출력까지 포함한다.

## Implemented Items

- [x] `LibNetworks/Telemetry/ObservedMetrics.cs`
  - `SessionRttSummarySnapshot` 추가.
  - `SlowSessionRttSnapshot` 추가.
  - `ClientObservedMetricsSnapshot.SessionRtt` optional field 추가.
  - 설계 JSON 계약의 `minSamplesPerSession` field name과 일치.
  - 확인: `LibNetworks/Telemetry/ObservedMetrics.cs:32`, `LibNetworks/Telemetry/ObservedMetrics.cs:45`, `LibNetworks/Telemetry/ObservedMetrics.cs:95`

- [x] `FastPortLoadRunner/LoadSession.cs`
  - `ParseEchoResponse`에서 local `sessionId`를 `RecordRtt`로 전달.
  - 확인: `FastPortLoadRunner/LoadSession.cs:200`

- [x] `FastPortLoadRunner/Metrics.cs`
  - `RecordRtt(long, long)` compatibility helper 유지.
  - `RecordRtt(int sessionId, long, long)` 추가.
  - 기존 global `_rttSamplesMs` 기록 유지.
  - `sessionId > 0`만 session RTT collector에 기록.
  - 확인: `FastPortLoadRunner/Metrics.cs:120`, `FastPortLoadRunner/Metrics.cs:125`

- [x] per-session bounded sample collector
  - `ConcurrentDictionary<int, SessionRttSamples>` 사용.
  - session별 lock은 개별 collector 내부에 한정.
  - retained sample cap 256.
  - `TotalSampleCount`와 retained `SampleCount`를 분리.
  - 확인: `FastPortLoadRunner/Metrics.cs:16`, `FastPortLoadRunner/Metrics.cs:429`

- [x] session RTT summary algorithm
  - eligible 조건: `TotalSampleCount >= 8`.
  - session P95 distribution의 P50/P95/P99 계산.
  - max session P95/P99/max 계산.
  - slow session 정렬: P95 desc, P99 desc, max desc, session ID asc.
  - slow session Top 20 제한.
  - 확인: `FastPortLoadRunner/Metrics.cs:366`

- [x] percentile behavior reuse
  - global RTT와 session RTT가 동일한 linear interpolation helper를 사용.
  - 확인: `FastPortLoadRunner/Metrics.cs:408`

- [x] `MetricsSnapshot.SessionRtt`
  - `CreateSnapshot`에서 session RTT summary 포함.
  - 확인: `FastPortLoadRunner/Metrics.cs:243`, `FastPortLoadRunner/Metrics.cs:524`

- [x] `ObservedMetricsExtensions`
  - `MetricsSnapshot.SessionRtt`를 `ClientObservedMetricsSnapshot.SessionRtt`로 매핑.
  - 확인: `FastPortLoadRunner/ObservedMetricsExtensions.cs:49`

- [x] `FastPortLoadValidation/LoadValidationStage.cs`
  - stage summary session RTT fields 추가.
  - 설계보다 `MaxSessionRttP50OfP95Ms`를 추가로 포함함.
  - 확인: `FastPortLoadValidation/LoadValidationStage.cs:109`

- [x] `FastPortLoadValidation/LoadValidationEvaluator.cs`
  - client samples에서 `SessionRtt`를 읽어 stage worst values로 집계.
  - slow sessions는 session ID별 worst entry를 고른 뒤 Top 20으로 제한.
  - 확인: `FastPortLoadValidation/LoadValidationEvaluator.cs:21`, `FastPortLoadValidation/LoadValidationEvaluator.cs:154`, `FastPortLoadValidation/LoadValidationEvaluator.cs:180`

- [x] `FastPortLoadValidation/LoadValidationSummaryWriter.cs`
  - Markdown table에 `Session RTT` column 추가.
  - low-sample excluded count detail line 추가.
  - slow session detail line은 Top 5로 제한.
  - 확인: `FastPortLoadValidation/LoadValidationSummaryWriter.cs:67`, `FastPortLoadValidation/LoadValidationSummaryWriter.cs:113`, `FastPortLoadValidation/LoadValidationSummaryWriter.cs:118`

- [x] unit tests
  - `LoadSession.ParseEchoResponse` 실제 호출 경로에서 session RTT 기록 확인.
  - session RTT summary, threshold, cap, concurrent writes 확인.
  - P95 동률 시 P99, max, session ID 순서로 slow session ordering 확인.
  - JSON serialize/deserialize와 older JSON compatibility 확인.
  - JSON envelope가 `minSamplesPerSession`을 쓰고 `minSampleCountForTail`을 쓰지 않음을 확인.
  - LoadValidation evaluator/Markdown rendering 확인.
  - 확인: `LibCommonTest/FastPortLoadRunnerTests.cs:281`, `LibCommonTest/FastPortLoadRunnerTests.cs:354`, `LibCommonTest/FastPortLoadRunnerTests.cs:427`, `LibCommonTest/FastPortLoadRunnerTests.cs:449`, `LibCommonTest/FastPortLoadRunnerTests.cs:538`, `LibCommonTest/ObservedMetricsTests.cs:200`, `LibCommonTest/ObservedMetricsTests.cs:271`, `LibCommonTest/FastPortLoadValidationTests.cs:269`, `LibCommonTest/FastPortLoadValidationTests.cs:409`

## Missing Items

- [ ] Optional smoke validation artifact
  - 설계의 smoke validation은 optional로 정의되어 있어 필수 gap은 아니다.
  - 현재 `dotnet build`, `dotnet test`, Release build는 통과했지만 실제 `FastPortLoadValidation --profile smoke` artifact는 아직 생성하지 않았다.
  - 다음 실제 진단 단계에서 `sessionRtt`가 JSONL/summary.md에 나타나는지 artifact로 확인하면 된다.

## Changed Items (Deviations from Design)

- [x] `SessionRttSummarySnapshot` field naming
  - 설계 JSON 예시는 `minSamplesPerSession`이었다.
  - Act 단계에서 구현 record field를 `MinSamplesPerSession`으로 변경했다.
  - JSON envelope test에서 `minSamplesPerSession` 출력과 `minSampleCountForTail` 미출력을 확인한다.
  - 확인: `LibNetworks/Telemetry/ObservedMetrics.cs:36`

- [x] Markdown Session RTT format 확장
  - 설계는 `p95-of-p95`와 `max-p95` 중심의 compact format이었다.
  - 구현은 `p50/p95/p99-of-p95`를 한 column에 함께 표시한다.
  - 설계보다 정보가 늘어난 변경이며, 진단 가치가 더 높아 positive deviation으로 판단한다.
  - 확인: `FastPortLoadValidation/LoadValidationSummaryWriter.cs:150`

- [x] LoadValidation stage field 확장
  - 설계의 optional fields에 없던 `MaxSessionRttP50OfP95Ms`가 추가됐다.
  - session P95 분포의 중앙값을 함께 보기 위한 확장이다.
  - 확인: `FastPortLoadValidation/LoadValidationStage.cs:112`

## Verification

실행한 검증:

```text
dotnet test FastPortCharp.sln --no-build
통과: 104, 실패: 0, 건너뜀: 0

git diff --check
통과

dotnet build FastPortCharp.sln -c Release
경고 0개, 오류 0개
```

직전 Do 단계에서 확인한 검증:

```text
dotnet build FastPortCharp.sln
경고 0개, 오류 0개

dotnet build FastPortCharp.sln -c Release
경고 0개, 오류 0개
```

## Risk Assessment

- 기능 리스크: 낮음
  - 기존 global RTT path가 유지되고, 새 session RTT는 optional nested field로 추가됐다.

- 성능 리스크: 중간 이하
  - session별 collector lock은 per-session 단위라 global lock은 피했다.
  - 10K session 기준 retained sample 상한은 `10,000 * 256` double sample 수준이다.
  - 실제 10K telemetry overhead는 아직 runtime artifact로 확인하지 않았다.

- 계약 리스크: 낮음
  - 과거 JSON deserialize는 테스트로 확인했다.
  - `minSamplesPerSession` output contract는 설계와 일치한다.

## Recommendations

1. `$pdca report loadrunner-per-session-rtt-tail-telemetry`로 진행한다.
2. 다음 성능 진단 전에는 smoke 또는 focused load validation을 한 번 실행해서 실제 `summary.md`와 JSONL의 `sessionRtt` 출력을 확인한다.
3. 10K 재측정 후 `sessionRtt`의 p50/p95/p99-of-p95와 slow session Top N으로 tail 집중 여부를 판단한다.

## Next Steps

- [x] Gap analysis document 작성
- [x] Act iteration으로 naming/test precision gap 해소
- [ ] `$pdca report loadrunner-per-session-rtt-tail-telemetry`
