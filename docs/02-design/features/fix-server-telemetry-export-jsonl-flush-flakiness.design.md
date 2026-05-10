# fix-server-telemetry-export-jsonl-flush-flakiness Design

> **Summary**: Option B — 테스트 파일 내부 private static `WaitForFileWithLinesAsync` helper. 고정 1.2초 sleep을 polling-until-ready로 교체. `FastPortTestSmokeServer/ServerTelemetryExportBackgroundService.cs` 0줄 변경. 직전 cycle(`fix-base-session-send-fifo-test-flakiness`) 패턴과 동일.
>
> **Project**: FastPortSharp
> **Author**: boinred
> **Date**: 2026-05-10
> **Status**: Draft
> **Plan**: [../../01-plan/features/fix-server-telemetry-export-jsonl-flush-flakiness.plan.md](../../01-plan/features/fix-server-telemetry-export-jsonl-flush-flakiness.plan.md)
> **PRD**: [../../00-pm/fix-server-telemetry-export-jsonl-flush-flakiness.prd.md](../../00-pm/fix-server-telemetry-export-jsonl-flush-flakiness.prd.md)

---

## Context Anchor

| Key | Value |
|-----|-------|
| **WHY** | windows runner thread pool latency가 1.2초 sleep을 초과해 race 노출. 직전 cycle과 동일 패턴 fix로 main green 회복. |
| **WHO** | Repo committer + 외부 contributor + AI agent. |
| **RISK** | (R-1) timeout 부족 / (R-2) 다른 회귀 / (R-3) flake 재발 / (R-4) runner 분산 |
| **SUCCESS** | windows 5/5 + 로컬 50/50 + production 0줄 + 변경 ≤ 50줄 / 1 파일 |
| **SCOPE** | `ServerTelemetryTests.cs:248-285` 테스트 + helper. |

---

## 1. Overview

### 1.1 Design Goals

1. Production zero-touch (`ServerTelemetryExportBackgroundService.cs` / 모든 엔진 코드 0줄).
2. healthy 환경에서 polling overhead 미미 (≤ 50ms 추가).
3. flaky 환경에서 timeout 10초 흡수.
4. timeout 도달 시 진단 메시지로 다음 디버그 비용 최소화.
5. helper는 직전 cycle BatchedFifoObserver와 동일 isolation 정책 (private static, 같은 파일 내).

### 1.2 Design Principles

- **Wait-for-condition over fixed-delay**: 직전 cycle "observable outcome over implementational" 원칙의 시간축 변형.
- **Fail-loud on timeout**: helper가 silent fail하지 않도록 `Assert.Fail`로 명시 종료.
- **Test-only**: production semantics 변경 0.

---

## 2. Architecture Options (Selected)

### 2.0 Comparison

| Criteria | Option A: Inline polling | **Option B: Private static helper** | Option C: Helpers/ 분리 |
|---|:-:|:-:|:-:|
| Production diff | 0줄 | 0줄 | 0줄 |
| Test diff | ~25줄 | ~35줄 | ~30줄 + helper 파일 신규 |
| 재사용성 | 낮음 | 같은 파일 내만 | 높음 |
| 직전 cycle 일관성 | medium | **high** | low |
| YAGNI 준수 | 높음 | **균형** | 낮음 (사용처 1곳) |
| Effort | Low | **Low** | Medium |
| **Recommendation** | | **Selected** | |

### 2.1 Selected: Option B

**Rationale**:
- 사용처 1곳이지만 helper로 분리하면 테스트 의도(polling)가 본 단언 코드에서 분리되어 가독성 ↑.
- 직전 cycle BatchedFifoObserver와 동일 isolation 정책 → 일관된 패턴.
- 별도 디렉토리 분리(Option C)는 사용처 ≥ 3 발생 시점에 별도 micro-cycle로 처리 (YAGNI).

### 2.2 Component Diagram

```
┌──────────────────────────────────────────────────────────────────────┐
│  FastPortTests/ServerTelemetryTests.cs                               │
│                                                                      │
│  ├── ServerTelemetryExportBackgroundService_WritesServerObservedJsonl│
│  │     (REWRITTEN line 248-285)                                      │
│  │     await Task.Delay(1200) 제거                                    │
│  │     → await WaitForFileWithLinesAsync(path, 1, 10s, 50ms)         │
│  │     → StopAsync                                                   │
│  │     → 기존 단언 모두 유지                                          │
│  │                                                                   │
│  └── WaitForFileWithLinesAsync (NEW — private static)                │
│        - 50ms 간격 polling                                           │
│        - File.Exists + File.ReadAllLinesAsync 체크                   │
│        - timeout 도달 시 Assert.Fail with diagnostic                 │
└──────────────────────────────────────────────────────────────────────┘
                              │
                              │ no diff
                              ▼
┌──────────────────────────────────────────────────────────────────────┐
│  FastPortTestSmokeServer/ServerTelemetryExportBackgroundService.cs   │
│                              (0 diff)                                │
└──────────────────────────────────────────────────────────────────────┘
```

---

## 3. Data Model

### 3.1 Helper 시그니처

```csharp
// Design Ref: §3.1 — race-free file readiness wait.
// healthy 환경에서는 50ms 단위로 즉시 감지, slow runner에서도 timeout까지 흡수.
private static async Task<string[]> WaitForFileWithLinesAsync(
    string path,
    int minLines,
    TimeSpan timeout,
    TimeSpan pollInterval,
    CancellationToken cancellationToken = default)
{
    var deadline = DateTime.UtcNow + timeout;
    bool everSawFile = false;
    int lastLineCount = 0;

    while (DateTime.UtcNow < deadline)
    {
        if (File.Exists(path))
        {
            everSawFile = true;
            try
            {
                string[] lines = await File.ReadAllLinesAsync(path, cancellationToken);
                lastLineCount = lines.Length;
                if (lines.Length >= minLines) { return lines; }
            }
            catch (IOException)
            {
                // exporter가 write 중 read 충돌 — 다음 polling에 재시도
            }
        }
        await Task.Delay(pollInterval, cancellationToken);
    }

    Assert.Fail(
        $"WaitForFileWithLinesAsync timeout ({timeout.TotalSeconds:F1}s): " +
        $"path={path}, fileEverExisted={everSawFile}, lastLineCount={lastLineCount}, " +
        $"minRequired={minLines}");
    return Array.Empty<string>();   // unreachable
}
```

**설계 의도**:
- `everSawFile` / `lastLineCount` 캡처로 timeout 시 진단 메시지 제공
- write 중 read race가 IOException 던지면 ignore + 재시도 (slow runner safety)
- `Assert.Fail` 후 `return Array.Empty<string>()`은 컴파일러 만족용 (실제 도달 안 함)

### 3.2 단언 전략 변화

| 검증 차원 | 기존 (race-prone) | 신규 (poll-based) |
|---|---|---|
| 파일 존재 | `Assert.IsTrue(File.Exists(path))` (라인 270) | helper가 polling으로 흡수 |
| 줄 수 ≥ 1 | `Assert.IsTrue(lines.Length >= 1)` (라인 272) | helper return 시점에 보장 |
| Snapshot 내용 | `Assert.AreEqual(...)` × 5 (라인 281-284) | **그대로 유지** — observable contract |

### 3.3 Test 재작성 sketch

```csharp
[TestMethod]
public async Task ServerTelemetryExportBackgroundService_WritesServerObservedJsonl()
{
    string directory = Path.Combine(Path.GetTempPath(), $"fastport-server-telemetry-{Guid.NewGuid():N}");
    string path = Path.Combine(directory, "server.metrics.jsonl");
    var telemetry = new ServerTelemetryCollector();
    telemetry.RecordAccept();
    telemetry.RecordSendRequested(128, queuedBytes: 256);
    var exporter = new ServerTelemetryExporter(telemetry);
    var options = new FastPortTestSmokeServer.FastPortTestSmokeServerTelemetryOptions
    {
        Output = path,
        IntervalSeconds = 1
    };
    var service = new FastPortTestSmokeServer.ServerTelemetryExportBackgroundService(
        NullLogger<FastPortTestSmokeServer.ServerTelemetryExportBackgroundService>.Instance,
        exporter,
        options);

    await service.StartAsync(CancellationToken.None);
    try
    {
        // Design Ref: §3.1 — fixed Task.Delay(1200) 대신 polling으로 race 흡수.
        // healthy 환경 ~1초 즈음 즉시 통과, slow runner에서 최대 10초까지 흡수.
        string[] lines = await WaitForFileWithLinesAsync(
            path,
            minLines: 1,
            timeout: TimeSpan.FromSeconds(10),
            pollInterval: TimeSpan.FromMilliseconds(50));

        // 기존 단언 유지 — observable contract 그대로
        ObservedMetricsSnapshot? snapshot = JsonSerializer.Deserialize<ObservedMetricsSnapshot>(
            lines[0],
            ObservedMetricsJson.SerializerOptions);

        Assert.IsNotNull(snapshot);
        Assert.IsNull(snapshot.ClientObserved);
        Assert.IsNotNull(snapshot.ServerObserved);
        Assert.AreEqual(1, snapshot.ServerObserved!.TotalAcceptedSessions);
        Assert.AreEqual(1, snapshot.ServerObserved.TotalSendRequests);
        Assert.AreEqual(1, snapshot.ServerObserved.PendingSendRequests);
        Assert.AreEqual(256, snapshot.ServerObserved.SendBufferBytes);
    }
    finally
    {
        await service.StopAsync(CancellationToken.None);
    }
}
```

> 변경:
> - `await Task.Delay(1200)` 제거
> - `await service.StopAsync(...)` 위치를 `try/finally`로 옮겨서 polling helper가 throw해도 graceful shutdown 보장
> - `Assert.IsTrue(File.Exists(path))`, `Assert.IsTrue(lines.Length >= 1)` 두 단언은 helper가 흡수
> - 나머지 단언 모두 유지

---

## 4. API Specification

본 cycle은 외부 API 미변경. helper는 `private static` (test 파일 내부).

---

## 5. UI/UX Design

해당 없음 (test refactor only).

---

## 6. Error Handling

- timeout 도달 → `Assert.Fail` (file 존재 여부 + 마지막 line count 포함 메시지).
- File.ReadAllLinesAsync IOException → catch + 재시도 (write 중 read 충돌).
- cancellationToken cancelled → `OperationCanceledException` 정상 propagation (현재 호출자가 None 전달이라 미발생).

---

## 7. Security Considerations

- 본 변경은 test 파일 한정. production 표면 영향 0.
- Path는 Guid-based unique tmp dir이라 다른 테스트와 충돌 0.

---

## 8. Test Plan

### 8.1 Local Verification

| # | 검증 | 명령 | 기대 |
|---|---|---|---|
| L1 | 빌드 | `dotnet build FastPortSharp.sln -c Release` | 0/0 |
| L2 | 전체 테스트 | `dotnet test --no-build` | 139/0/0 |
| L3 | 대상 테스트 50회 반복 | `tests/scripts/repeat-tests.sh 50 "FullyQualifiedName~ServerTelemetryExportBackgroundService_WritesServerObservedJsonl"` | 0 fail |
| L4 | production diff 검증 | `git diff -- FastPortTestSmokeServer/` | 0줄 |

### 8.2 CI Verification

- `gh run list --workflow=build.yml` 5회 연속 windows-latest PASS
- ubuntu-latest / macos-latest 회귀 0

---

## 9. Clean Architecture (.NET 적용)

해당 없음 (test 파일 한정).

---

## 10. Coding Convention Reference

### 10.1 File Layout

| 영역 | 위치 |
|---|---|
| 테스트 + helper | `FastPortTests/ServerTelemetryTests.cs` |

### 10.2 Naming

- `WaitForFileWithLinesAsync` — async + Task<T> + Async suffix
- `private static` 메서드라 `_camelCase`/`m_camelCase` 등 instance prefix 미사용

---

## 11. Implementation Guide

### 11.1 File Structure

```
FastPortSharp/
├── FastPortTests/
│   └── ServerTelemetryTests.cs   ← MODIFY (테스트 1 + helper 1)
└── docs/
    ├── 00-pm/...prd.md            (already)
    ├── 01-plan/...plan.md         (already)
    ├── 02-design/...design.md     (this file)
    ├── 03-analysis/...analysis.md (next phase)
    └── 04-report/...report.md     (next phase)
```

### 11.2 Implementation Order

| 순서 | 작업 | 산출물 | 검증 |
|---|---|---|---|
| 1 | `WaitForFileWithLinesAsync` private static helper 추가 (~30줄) | helper | 컴파일 통과 |
| 2 | `ServerTelemetryExportBackgroundService_WritesServerObservedJsonl` rewrite | line 248-285 교체 | 단일 테스트 5회 반복 PASS |
| 3 | 전체 139 테스트 회귀 확인 | (실행) | 139/0/0 |
| 4 | `git diff` production 0줄 확인 | (검증) | 0 |
| 5 | 50회 반복 stress | repeat-tests.sh | 50/50 |
| 6 | commit + push | git push | build.yml 자동 트리거 |
| 7 | windows-latest 5회 rerun | `gh run rerun` | 5/5 PASS |

### 11.3 Session Guide

> 단일 세션 ≤ 20 turn 예상. `--scope` 분할 불필요.

| Module | Scope Key | Description | Estimated Turns |
|---|---|---|:-:|
| Test refactor | `tests-refactor` | helper 추가 + 1 test rewrite | 8-12 |
| Stress + CI | `verify` | 50회 + GHA 5 rerun | 5-8 |

---

## Version History

| Version | Date | Changes | Author |
|---|---|---|---|
| 0.1 | 2026-05-10 | Initial design — Option B (private static helper), polling 10s timeout / 50ms interval | boinred |
