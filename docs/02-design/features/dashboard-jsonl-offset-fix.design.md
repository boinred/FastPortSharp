# dashboard-jsonl-offset-fix Design

> **Project**: FastPortSharp
> **Author**: boinred
> **Date**: 2026-05-11
> **Status**: Draft
> **Plan**: `docs/01-plan/features/dashboard-jsonl-offset-fix.plan.md`

---

## Context Anchor

| Key | Value |
|-----|-------|
| **WHY** | Race window을 좁혀 production code correctness 보강 + 동기 사용 패턴 안전화. |
| **WHO** | boinred + 미래 contributor. |
| **RISK** | (R-1) Tuple return signature / (R-2) 기존 20 tests 회귀 / (R-3) Production 동작 미세 변경 |
| **SUCCESS** | Race 제거 + Dashboard 0/0 + 20 tests 회귀 0 + 회귀 sln 0 + 단일 commit |
| **SCOPE** | `FastPortDashboard.Core/Adapters/JsonlPollingAdapter.cs` only |

---

## 1. Overview

`StreamAsync`의 offset 갱신 timing을 yield AFTER에서 yield BEFORE로 이동. `ReadNewSnapshotsAsync`가 `(Snapshots, NewOffset)` tuple 반환하여 race-safe하게 offset 확정.

---

## 2. Architecture Decision

### 2.1 Options Compared

| Option | Approach | API 영향 | 선택 |
|---|---|---|---|
| **A — Tuple return (선택)** | `ReadNewSnapshotsAsync` 시그너처를 `(snaps, newOffset)` tuple 반환으로 변경 | private method만 변경, public API 그대로 | ✅ |
| B — `ref long lastReadOffset` 파라미터 | C# ref parameter로 in-place 갱신 | 가독성 ↓, async ref 제한 (await 통과 못함) | ❌ |
| C — 별도 `GetCurrentOffset()` method | StreamAsync에서 별도 호출 | race window 그대로 (open/read/close 분리) | ❌ |

### 2.2 Selected: Option A — Tuple return

**선택 근거**:
- Public API (`IAsyncEnumerable<...> StreamAsync(CancellationToken)`) 영향 0
- C# 7+ tuple return은 표준 패턴, async-friendly
- 단일 파일, ~20줄 변경으로 race 제거

---

## 3. Detailed Design

### 3.1 Before (현재 race 코드)

```csharp
public async IAsyncEnumerable<ObservedMetricsSnapshot> StreamAsync(
    [EnumeratorCancellation] CancellationToken ct)
{
    long lastReadOffset = 0;
    while (!ct.IsCancellationRequested)
    {
        ObservedMetricsSnapshot[] newSnapshots = await ReadNewSnapshotsAsync(lastReadOffset, ct);
        foreach (var snap in newSnapshots) yield return snap;  // ← race window 시작
        try
        {
            if (File.Exists(_path))
            {
                long currentLength = new FileInfo(_path).Length;
                if (currentLength < lastReadOffset) lastReadOffset = 0;
                else lastReadOffset = currentLength;  // ← 여기서 append된 위치까지 jump
            }
        }
        catch (IOException) { }
        try { await Task.Delay(_interval, ct); }
        catch (OperationCanceledException) { yield break; }
    }
}

private async Task<ObservedMetricsSnapshot[]> ReadNewSnapshotsAsync(long startOffset, CancellationToken ct) { ... }
```

### 3.2 After (race-safe)

```csharp
public async IAsyncEnumerable<ObservedMetricsSnapshot> StreamAsync(
    [EnumeratorCancellation] CancellationToken ct)
{
    long lastReadOffset = 0;
    while (!ct.IsCancellationRequested)
    {
        // Design Ref: §3.2 (dashboard-jsonl-offset-fix) —
        // offset을 yield BEFORE에 확정해 consumer-generator gap race 회피.
        (ObservedMetricsSnapshot[] snapshots, long newOffset) = await ReadNewSnapshotsAsync(lastReadOffset, ct);
        lastReadOffset = newOffset;

        foreach (var snap in snapshots) yield return snap;

        try { await Task.Delay(_interval, ct); }
        catch (OperationCanceledException) { yield break; }
    }
}

private async Task<(ObservedMetricsSnapshot[] Snapshots, long NewOffset)> ReadNewSnapshotsAsync(
    long startOffset, CancellationToken ct)
{
    if (!File.Exists(_path)) return (Array.Empty<ObservedMetricsSnapshot>(), startOffset);

    var results = new List<ObservedMetricsSnapshot>();
    long newOffset = startOffset;
    try
    {
        using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        // Design Ref: §3.2 — truncation detection BEFORE seek.
        if (fs.Length < startOffset)
        {
            startOffset = 0;  // file truncated → 처음부터 다시
        }

        // open 직후 length를 stable snapshot으로 capture.
        // 이후 producer가 append해도 본 iteration은 fileLength까지만 처리.
        long fileLength = fs.Length;

        if (startOffset > 0 && startOffset <= fileLength)
        {
            fs.Seek(startOffset, SeekOrigin.Begin);
        }

        using var sr = new StreamReader(fs);
        string? line;
        while ((line = await sr.ReadLineAsync(ct)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            ObservedMetricsSnapshot? snap = TryDeserialize(line);
            if (snap is not null) results.Add(snap);
        }

        newOffset = fileLength;
    }
    catch (IOException) { /* 다음 polling에서 재시도 */ }
    catch (OperationCanceledException) { /* normal stop */ }

    return (results.ToArray(), newOffset);
}
```

### 3.3 Race Window 분석

| 시나리오 | Before | After |
|---|---|---|
| Consumer가 yield 받고 즉시 append | ❌ append된 위치까지 jump → 데이터 손실 | ✅ newOffset은 open 직후 length 기반 → append는 다음 iteration에서 read |
| Producer가 generator delay 중 append | ✅ 안전 (offset capture는 이전 length) | ✅ 안전 |
| File truncate during read | ✅ 안전 (next iteration에서 length < offset 감지) | ✅ 동일 (ReadNew 안에서 처리) |

### 3.4 File-Level Changes

| 파일 | 작업 | 예상 라인 |
|---|---|---|
| `FastPortDashboard.Core/Adapters/JsonlPollingAdapter.cs` | edit (StreamAsync + ReadNew rewrite) | ±25 |

---

## 4. Risks and Mitigation

| Risk | Mitigation |
|---|---|
| (R-1) Tuple deconstruction syntax | C# 7+ 표준, `net10.0` 완전 지원 |
| (R-2) StreamReader가 fs.Length 너머 read 시 newOffset 부정확 | StreamReader는 fs.Length 너머로 못 감 (EOF). fs.Length를 capture timing에 안전. |
| (R-3) 기존 백그라운드 writer test 회귀 | Background writer는 generator delay 중 write → race 없음. Fix와 무관하게 통과. |
| (R-4) Truncation 동작 회귀 | 동일 로직 (`fs.Length < startOffset` → reset). 위치만 StreamAsync에서 ReadNew로 이동. |

---

## 5. Implementation Guide

### 5.1 Implementation Order

1. `JsonlPollingAdapter.cs` — `ReadNewSnapshotsAsync` 시그너처를 `Task<(ObservedMetricsSnapshot[], long)>`로 변경
2. `StreamAsync`에서 tuple deconstruct + `lastReadOffset = newOffset` (yield 전)
3. `ReadNewSnapshotsAsync` 안에서 truncation check + fileLength capture + newOffset return
4. Dashboard 빌드 0/0
5. Dashboard test 20/0/0 회귀 0
6. FastPortSharp.sln 회귀 0
7. 단일 commit

### 5.2 Session Plan

총 ≤ 6 turn 예상. `--scope` 분할 불필요.

---

## 6. Test Plan

| Level | Test | Pass Criteria |
|---|---|---|
| Build | Dashboard sln Release | 0 errors |
| Unit | Dashboard test (백그라운드 writer 패턴) | 20/0/0 회귀 0 |
| Regression Build | FastPortSharp.sln Release | 0 errors |
| Regression Test | 139 tests | 139/0/0 |

---

## 7. Out of Scope

- 신규 test 추가 (사용자 확정)
- Long-lived FileStream
- StreamReader partial-line at EOF 처리
- MockPollingAdapter / IPollingAdapter interface 변경

---

## Version History

| Version | Date | Changes | Author |
|---|---|---|---|
| 0.1 | 2026-05-11 | Initial design (Option A — Tuple return, ~25 lines, 1 file) | boinred |
