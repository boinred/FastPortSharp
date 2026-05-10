# fix-server-telemetry-export-jsonl-flush-flakiness PRD

> **Lightweight PRD** (internal QA reliability cycle — same shape as
> `fix-base-session-send-fifo-test-flakiness`).
>
> **Project**: FastPortSharp
> **Author**: boinred
> **Date**: 2026-05-10
> **Trigger**: build.yml CI run 25616986247 attempt #2 — windows-latest job
> failed on `ServerTelemetryExportBackgroundService_WritesServerObservedJsonl`
> (~10% flake observed across 5 reruns).

---

## 1. Executive Summary

| 관점 | 내용 |
|---|---|
| **Problem** | `ServerTelemetryExportBackgroundService_WritesServerObservedJsonl` 테스트가 GHA Windows에서 ~10% flake. `Task.Delay(1200ms)` 단일 sleep이 production의 1초 interval과 너무 가까워 slow runner의 thread pool latency를 흡수하지 못함. |
| **Solution** | 고정 1.2초 sleep을 **poll-until-ready** 패턴으로 교체. 파일이 존재하고 ≥ 1줄이 쓰였을 때까지 짧은 간격으로 폴링 (timeout: 10초). Production 0줄 변경. |
| **Function/UX/Effect** | 테스트가 healthy 환경에서는 즉시 끝나고, 느린 GHA runner에서도 deterministic하게 통과. CI flake 0. |
| **Core Value** | `build.yml`의 windows job 5/5 PASS 회복 → 동일 cycle 패턴(black-box wait + production 무변경)이 두 번째 timing-dependent test에 재사용. |

---

## 2. Problem Statement

### 2.1 Observed Failure

- CI run: `25616986247` attempt #2
- Job: `windows-latest`
- Test: `FastPortTests.ServerTelemetryTests.ServerTelemetryExportBackgroundService_WritesServerObservedJsonl`
- Duration: 1 s
- Assertion: `ServerTelemetryTests.cs:270` — `Assert.IsTrue(File.Exists(path))` 실패
- 경험적 빈도: 5회 rerun 중 1회 fail (~20% empirical, PRD §4 cycle에서 ~10%로 표기했지만 실측 더 노출 가능)

### 2.2 Root Cause

`ServerTelemetryExportBackgroundService` (production):

```csharp
TimeSpan interval = TimeSpan.FromSeconds(Math.Max(1, _options.IntervalSeconds));   // ≥ 1s
...
await using FileStream stream = File.Open(outputPath, FileMode.Create, ...);       // line 44
...
while (!stoppingToken.IsCancellationRequested) {
    await Task.Delay(interval, stoppingToken);                                     // line 52, ≥ 1s
    ...write line...
}
```

Test (현재):

```csharp
await service.StartAsync(...);     // returns immediately; ExecuteAsync schedule
await Task.Delay(1200);            // 고정 1.2s
await service.StopAsync(...);      // cancellation → loop exit
Assert.IsTrue(File.Exists(path));  // ← here
```

Race timeline (slow Windows runner):

```
T+0ms     test calls StartAsync, returns synchronously
T+0..300  thread pool schedules ExecuteAsync (variable latency)
T+300ms   ExecuteAsync starts: CreateDirectory + File.Open
T+1300ms  Task.Delay(1s, token) completes → write → file has 1 line
T+1200ms  test calls StopAsync — but ExecuteAsync still in Delay
T+1200    StopAsync cancels token; ExecuteAsync's Delay throws OCE
                                  → loop exits before write happens
                                  → file may have 0 lines OR may not exist
                                    if File.Open hadn't executed yet
```

마지막 케이스(File.Open 미실행)는 thread pool latency가 ~1.2초 가까이 누적될 때 발생. Windows GHA Server 2025 runner의 cold-start latency를 고려하면 가능.

### 2.3 Why now / why windows only

- 직전 cycle(`fix-base-session-send-fifo-test-flakiness`)에서 build.yml CI를 처음 도입.
- macos-latest는 이번 cycle에서 안정적 통과 (~26s).
- ubuntu-latest도 통과.
- windows-latest만 ~20% flake — Windows Server 2025 runner의 file I/O / thread scheduling이 macOS/Linux보다 분산이 큼.

---

## 3. Scope

### 3.1 In Scope

- `FastPortTests/ServerTelemetryTests.cs` 1개 테스트 수정 (line 248-285)
- 고정 sleep을 polling helper로 교체 (`WaitForFileWithLinesAsync` 같은 형태)
- 가능하면 동일한 polling helper를 다른 timing-dependent 테스트에서도 재사용 가능하도록 설계

### 3.2 Out of Scope

- `FastPortTestSmokeServer/ServerTelemetryExportBackgroundService.cs` (production) 변경
- `IntervalSeconds` 의 Math.Max(1, ...) clamping 정책 변경
- 다른 BackgroundService 테스트 audit
- Windows GHA runner 자체 우회

---

## 4. Constraints

- Production code 1줄도 건드리지 않을 것 (직전 cycle 패턴 일관성 유지)
- 변경 라인 수 FastPortTests/ 내부 ≤ 50줄
- 새 NuGet 의존 추가 0
- MSTest 호환 유지
- 수정 후 Windows / macOS / ubuntu 각각 5회 연속 PASS

---

## 5. Success Criteria

- [ ] build.yml의 windows-latest job이 **5회 연속 PASS** (`gh run rerun` 또는 push)
- [ ] ubuntu-latest / macos-latest 회귀 0 (5/5)
- [ ] 로컬 `dotnet test` 50회 반복 0 fail
- [ ] `ServerTelemetryExportBackgroundService.cs` (production) `git diff` = 0줄
- [ ] 변경 라인은 `ServerTelemetryTests.cs` + (재사용 헬퍼가 별도 파일이라면) 1-2 곳에만
- [ ] Test wall-clock: healthy 환경 ≤ 2초 (현재 ~1.2초, polling으로 더 빨리 끝날 수 있음)

---

## 6. Risks

| Risk | Mitigation |
|---|---|
| (R-1) polling timeout이 너무 짧으면 GHA Windows 여전히 flake | 10초 timeout 채택 (현재 1.2초의 8배 마진). interval 50ms로 폴링 |
| (R-2) polling이 production 동작을 가려서 본 의도 검증 누락 | `lines.Length >= 1`까지 polling 후 기존 단언(SentBytes, PendingSendRequests 등) 모두 유지 |
| (R-3) timeout 도달 시 명확한 에러 메시지 부재 | `Assert.Fail("file not produced within {timeout}; last state: ...")` 형태로 진단 정보 포함 |
| (R-4) 직전 cycle과 fix 패턴 중복 | 의도된 일관성 — "wait-for-condition" helper는 향후 timing-dependent test의 표준 패턴으로 자리잡음 |

---

## 7. Stakeholders & Personas

해당 없음 — 직전 cycle과 동일 (repo committer + CI + AI agent).

---

## 8. Beachhead / GTM

해당 없음 (내부 QA 안정화).

---

## 9. Next Steps

1. `/pdca plan fix-server-telemetry-export-jsonl-flush-flakiness`
   - polling timeout / interval 정책 확정
   - 재사용 가능한 helper 위치 (test 파일 내부 vs `Helpers/` 분리) 결정
2. `/pdca design ...` — 3개 옵션 제시
3. 단일 세션 Do (≤ 20 turn)
4. Check + Report (CI 5회 rerun 후)

---

## Version History

| Version | Date | Changes | Author |
|---|---|---|---|
| 1.0 | 2026-05-10 | Initial PRD (race analysis + scope, mirrors fix-base-session-send-fifo-test-flakiness) | boinred |
