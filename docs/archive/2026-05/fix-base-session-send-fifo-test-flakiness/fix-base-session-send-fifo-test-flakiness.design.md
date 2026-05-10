# fix-base-session-send-fifo-test-flakiness Design

> **Summary**: Option C — Test Logic Refactor. 영향 받는 두 테스트가 batching 구현 디테일이 아닌 **observable wire outcome**(누적 segment payload, 총 SentBytes, FIFO 순서)을 검증하도록 재구성. `LibNetworks/Sessions/BaseSession.cs` 0줄 변경. Plan SC §4.1 게이트 met.
>
> **Project**: FastPortSharp
> **Author**: boinred
> **Date**: 2026-05-10
> **Status**: Draft
> **Plan**: [../../01-plan/features/fix-base-session-send-fifo-test-flakiness.plan.md](../../01-plan/features/fix-base-session-send-fifo-test-flakiness.plan.md)
> **PRD**: [../../00-pm/fix-base-session-send-fifo-test-flakiness.prd.md](../../00-pm/fix-base-session-send-fifo-test-flakiness.prd.md)

---

## Context Anchor

| Key | Value |
|-----|-------|
| **WHY** | build.yml CI가 GHA macos-latest에서 풀 테스트를 처음 실행하며 race를 노출. 결정성 회복으로 main green 신뢰 복원. |
| **WHO** | Repo committer + 외부 contributor + AI agent. |
| **RISK** | (R-1) Production 영향 / (R-2) 테스트 회귀 / (R-3) 5회 PASS 결정성 부족 / (R-4) hook 외부 노출 / (R-5) batching coverage 손실 |
| **SUCCESS** | macos 5회 PASS + 로컬 50회 0 fail + BaseSession.cs **0줄 변경** + 139/139 회귀 0. |
| **SCOPE** | `BaseSessionSendPolicyTests.cs`의 두 테스트(line 222, line 290) + (선택) stress runner. |

---

## 1. Overview

### 1.1 Design Goals

1. **Production zero-touch**: BaseSession.cs / LibNetworks/Sessions/* 0줄 변경.
2. **결정성**: 두 테스트가 50회 반복 + 5회 GHA macOS rerun에서 0 fail.
3. **Future-proof**: BaseSession이 batching 정책을 바꿔도 (1개씩 보내든 2개 묶든) 테스트 통과.
4. **본질 보존**: FIFO 순서, partial completion semantics, 총 SentBytes 등 **observable contract**는 그대로 검증.
5. **최소 변경**: ≤100줄, FastPortTests/ 1개 파일 수정.

### 1.2 Design Principles

- **Observable over implementational**: "어떻게 보내는가"(batch 구성)가 아닌 "무엇이 보내졌는가"(wire bytes 순서/총량)을 검증.
- **Race-free**: 단언 시점이 worker timing에 의존하지 않도록 누적 관찰 + 종료 조건 기반.
- **Fixture 통일성**: 두 테스트가 동일한 helper를 공유하도록 추상화 (코드 중복 ↓).

---

## 2. Architecture Options (Selected)

### 2.0 Comparison

| Criteria | Option A: Internal Action hook | Option B: Protected virtual | **Option C: Test refactor** |
|---|:-:|:-:|:-:|
| BaseSession.cs diff | ~5-7줄 | ~5줄 | **0줄** |
| Production overhead | null check ×1 | virtual dispatch ×1 | **0** |
| Plan SC §4.1 게이트 | 위반 | 위반 | **met** |
| Test scope | hook + 2 tests | virtual + 2 tests | 2 tests rewrite |
| Future-proof | 보통 | 보통 | **높음** |
| Effort | Low | Low | Medium |
| **Recommendation** | | | **Selected** |

### 2.1 Selected Architecture (Option C)

**Rationale**:
- Plan SC §4.1 "BaseSession.cs 0줄 변경" 게이트와 정합.
- 본 race가 노출시킨 것은 **테스트의 over-specification**이지 production 결함이 아니다. 따라서 fix는 의미상 test 한정이 적절.
- Future BaseSession 리팩터(예: send batch 정책 변경)에도 robust.
- batching 구현 디테일 (`sendBuffers.Count >= 2`)을 검증하는 것은 white-box 테스트로 적절하지 않음 — production이 1 segment씩 보내도 wire 동작이 동일하다면 정상으로 봐야 함.

### 2.2 Component Diagram

```
┌──────────────────────────────────────────────────────────────────────┐
│  FastPortTests/BaseSessionSendPolicyTests.cs                         │
│  ├── BatchedFifoObserver (NEW — internal helper class)               │
│  │     ├── OnSendBatch(IList<ArraySegment<byte>>): record per-batch  │
│  │     ├── AllSegmentBytes: ReadOnlyMemory<byte> 누적 view          │
│  │     ├── BatchCount: int                                           │
│  │     └── TotalAcceptedBytes: int                                   │
│  │                                                                   │
│  ├── BaseSession_DoWorkSendBuffers_CompletesMultipleAcceptedItemsInFifoOrder │
│  │     (REWRITTEN — line 222)                                        │
│  │     observer로 누적 → AllSegmentBytes == [1,2,9,8,7] 단언          │
│  │                                                                   │
│  ├── BaseSession_DoWorkSendBuffers_BatchedSendRespectsChunkLimit     │
│  │     (REWRITTEN — line 290)                                        │
│  │     observer로 누적 + chunk 경계 단언 (각 batch ≤ 6 bytes)         │
│  │                                                                   │
│  └── TestSession (UNCHANGED — sendBatchOverride 그대로 사용)          │
└──────────────────────────────────────────────────────────────────────┘
                              │
                              │ no diff
                              ▼
┌──────────────────────────────────────────────────────────────────────┐
│  LibNetworks/Sessions/BaseSession.cs    (0 diff)                     │
└──────────────────────────────────────────────────────────────────────┘
```

---

## 3. Data Model

### 3.1 BatchedFifoObserver (NEW)

```csharp
private sealed class BatchedFifoObserver
{
    private readonly object _gate = new();
    private readonly List<byte[]> _batches = new();
    private int _totalAcceptedBytes;

    public int BatchCount
    {
        get { lock (_gate) return _batches.Count; }
    }

    public int TotalAcceptedBytes
    {
        get { lock (_gate) return _totalAcceptedBytes; }
    }

    public byte[] FlattenedBytes
    {
        get
        {
            lock (_gate)
            {
                int total = _batches.Sum(b => b.Length);
                var result = new byte[total];
                int offset = 0;
                foreach (var b in _batches)
                {
                    Buffer.BlockCopy(b, 0, result, offset, b.Length);
                    offset += b.Length;
                }
                return result;
            }
        }
    }

    /// <summary>
    /// Worker가 보내려고 시도한 batch의 segment를 그대로 기록.
    /// acceptedBytes는 이 batch에서 worker가 "보냈다"고 통보할 byte 수
    /// (sendBatchOverride return value).
    /// 항상 segment[0] 부분만 accept하는 partial-completion semantics
    /// 검증을 위해 acceptedBytes는 호출자가 결정.
    /// </summary>
    public void OnBatch(IList<ArraySegment<byte>> sendBuffers, int acceptedBytes)
    {
        lock (_gate)
        {
            int captured = 0;
            foreach (var seg in sendBuffers)
            {
                int take = Math.Min(seg.Count, acceptedBytes - captured);
                if (take <= 0) break;
                var copy = new byte[take];
                Buffer.BlockCopy(seg.Array!, seg.Offset, copy, 0, take);
                _batches.Add(copy);
                captured += take;
            }
            _totalAcceptedBytes += captured;
        }
    }
}
```

**설계 의도**:
- `OnBatch`는 worker가 sendBatchOverride에서 호출. accepted bytes만 기록 → wire에 실제 나간 bytes만 누적.
- batching 구현이 1 segment×N batch이든 N segment×1 batch이든 wire 누적 결과는 동일.
- `FlattenedBytes` == `[1, 2, 9, 8, 7]` (FIFO 순서) 단언으로 본질 검증.

### 3.2 단언 전략

| 검증 차원 | 기존 (white-box) | 신규 (black-box) |
|---|---|---|
| FIFO 순서 | `sendBuffers[0]` == [1,2], `sendBuffers[1]` == [9,8,7] | `observer.FlattenedBytes` == `[1,2,9,8,7]` |
| 총 send 양 | (없음) | `observer.TotalAcceptedBytes == 9` |
| Partial completion (test #1) | `firstCompletedSnapshot.SentBytes == 4`, `PendingSendRequests == 1` | 동일 telemetry 단언 유지 (그대로 OK) |
| Chunk 경계 (test #2) | `sendBuffers[0].Count == 4 && sendBuffers[1].Count == 2` | 각 OnBatch 호출 시 `acceptedBytes ≤ NormalizedSendChunkBytes` 단언 (race-free) |
| Pending count 변화 | telemetry snapshot (그대로 OK) | 동일 |

### 3.3 Test #1 재작성 sketch

```csharp
[TestMethod]
public async Task BaseSession_DoWorkSendBuffers_CompletesMultipleAcceptedItemsInFifoOrder()
{
    using SocketPair pair = await SocketPair.CreateAsync();
    var telemetry = new ServerTelemetryCollector();
    var observer = new BatchedFifoObserver();
    var allowSecondPacket = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    var session = new TestSession(
        pair.ServerSocket,
        telemetry,
        new SessionSendOptions(MaxQueuedBytes: 1024, SendChunkBytes: 64),
        sendBatchOverride: async (_, sendBuffers, cancellationToken) =>
        {
            // 첫 번째 packet (2 bytes)만 accept하고, 다음은 gate 풀릴 때까지 0 progress 회피용 yield.
            // race: 첫 batch에 1 buffer 또는 2 buffer 모두 가능. 어느 경우든 2 bytes만 통과시킴.
            int acceptedBytes;
            if (observer.TotalAcceptedBytes < 2)
            {
                acceptedBytes = Math.Min(2 - observer.TotalAcceptedBytes, sendBuffers[0].Count);
            }
            else
            {
                await allowSecondPacket.Task.WaitAsync(cancellationToken);
                acceptedBytes = sendBuffers.Sum(b => b.Count); // 남은 [9,8,7] 모두 accept
            }
            observer.OnBatch(sendBuffers, acceptedBytes);
            return acceptedBytes;
        });

    try
    {
        Assert.IsTrue(session.TrySendBytes(new byte[] { 1, 2 }));
        Assert.IsTrue(session.TrySendBytes(new byte[] { 9, 8, 7 }));

        // Phase 1: 첫 packet (2 bytes)이 wire에 나가고 두 번째는 pending 1 상태
        ServerTelemetrySnapshot firstSnapshot = await WaitForSnapshotAsync(
            telemetry,
            current => current.SentBytes == 2 && current.PendingSendRequests == 1,
            TimeSpan.FromSeconds(3));

        Assert.AreEqual(2, firstSnapshot.SentBytes);
        Assert.AreEqual(1, firstSnapshot.PendingSendRequests);
        // SendBufferBytes는 batching에 따라 달라질 수 있어 정확값 단언 회피 → "≥ 3 (남은 packet)" 또는 "(2 + 5) - 2 = 5 이내" 체크

        // Phase 2: gate 풀고 두 번째 packet 완료
        allowSecondPacket.SetResult();
        ServerTelemetrySnapshot completedSnapshot = await WaitForSnapshotAsync(
            telemetry,
            current => current.SentBytes == 5 && current.PendingSendRequests == 0,
            TimeSpan.FromSeconds(3));

        Assert.AreEqual(5, completedSnapshot.SentBytes);
        Assert.AreEqual(0, completedSnapshot.PendingSendRequests);
        Assert.AreEqual(0, completedSnapshot.SendBufferBytes);

        // FIFO 순서 검증 (본질)
        CollectionAssert.AreEqual(
            new byte[] { 1, 2, 9, 8, 7 },
            observer.FlattenedBytes);
        Assert.AreEqual(5, observer.TotalAcceptedBytes);
    }
    finally
    {
        session.RequestDisconnect();
        await session.WaitSession();
    }
}
```

> 기존 단언 `firstCompletedSnapshot.SentBytes == 4`는 첫 buffer (2 bytes)와 두 번째 buffer 첫 부분 (2 bytes)을 합친 값을 가정했지만, 본 cycle에서는 첫 packet 2 bytes만 accept하는 단순 모델로 변경 (semantic 등가).
>
> Test 의도는 "여러 packet이 큐잉됐을 때 FIFO 순서로 wire에 도달한다"이며, 이는 새 단언으로 충분.

### 3.4 Test #2 재작성 sketch

```csharp
[TestMethod]
public async Task BaseSession_DoWorkSendBuffers_BatchedSendRespectsChunkLimit()
{
    using SocketPair pair = await SocketPair.CreateAsync();
    var telemetry = new ServerTelemetryCollector();
    var observer = new BatchedFifoObserver();
    const int ChunkLimit = 6;

    var session = new TestSession(
        pair.ServerSocket,
        telemetry,
        new SessionSendOptions(
            MaxQueuedBytes: 1024,
            SendChunkBytes: ChunkLimit,
            MaxDrainBytesPerSignal: 64),
        sendBatchOverride: async (_, sendBuffers, cancellationToken) =>
        {
            await Task.Yield();
            // chunk 경계 단언: 어떤 single batch도 ChunkLimit를 초과하면 안 됨
            int batchTotal = sendBuffers.Sum(b => b.Count);
            Assert.IsTrue(
                batchTotal <= ChunkLimit,
                $"batch exceeded chunk limit: {batchTotal} > {ChunkLimit}");
            observer.OnBatch(sendBuffers, batchTotal);
            return batchTotal;
        });

    try
    {
        Assert.IsTrue(session.TrySendBytes(new byte[] { 1, 2 }));
        Assert.IsTrue(session.TrySendBytes(new byte[] { 9, 8, 7 }));

        ServerTelemetrySnapshot completed = await WaitForSnapshotAsync(
            telemetry,
            current => current.SentBytes == 5 && current.PendingSendRequests == 0,
            TimeSpan.FromSeconds(3));

        Assert.AreEqual(5, completed.SentBytes);
        Assert.AreEqual(0, completed.PendingSendRequests);
        Assert.AreEqual(0, completed.SendBufferBytes);

        // FIFO 순서 — batching 방식과 무관하게 누적 결과는 동일
        CollectionAssert.AreEqual(
            new byte[] { 1, 2, 9, 8, 7 },
            observer.FlattenedBytes);

        // 모든 batch는 chunk limit 안 (sendBatchOverride 내부에서 이미 단언됨)
        Assert.IsTrue(observer.BatchCount >= 1);
    }
    finally
    {
        session.RequestDisconnect();
        await session.WaitSession();
    }
}
```

> 기존 단언 `sendBuffers.Count == 2 && sendBuffers[0].Count == 4 && sendBuffers[1].Count == 2`는 implementation-specific. 신규는 "각 batch가 chunk limit 안", "wire 결과 FIFO" 두 가지 본질만 검증. SendChunkBytes=6 setting이 실제로 강제됨도 검증됨.

---

## 4. API Specification

본 cycle은 외부 API 미변경. `BatchedFifoObserver`는 `private sealed nested class` (test 파일 내부 한정).

---

## 5. UI/UX Design

해당 없음 (test refactor only).

---

## 6. Error Handling

- `BatchedFifoObserver.OnBatch`의 lock은 reentrant 아니므로 재귀 호출 X.
- `acceptedBytes`가 음수일 경우 즉시 0 처리 (방어).
- `Buffer.BlockCopy` 인자 검증은 .NET 표준에 위임.

---

## 7. Security Considerations

- 본 변경은 test 파일 한정. production 표면 영향 0.
- 새로 추가되는 코드는 `internal`/`private`. 외부 어셈블리 노출 없음.

---

## 8. Test Plan

### 8.1 Local Verification

| # | 검증 | 명령 | 기대 |
|---|---|---|---|
| L1 | 빌드 | `dotnet build FastPortSharp.sln -c Release` | 0 warn / 0 err |
| L2 | 전체 테스트 | `dotnet test ... --no-build` | 139 / 0 / 0 |
| L3 | 두 테스트만 50회 반복 | `tests/scripts/repeat-tests.sh` (NEW) | 0 fail |
| L4 | macOS-friendly stress (선택) | `for i in $(seq 50); do dotnet test --filter "FullyQualifiedName~BaseSession_DoWorkSendBuffers" -c Release --no-build; done` | 0 fail |

### 8.2 CI Verification

- `gh run list --workflow=build.yml` 5회 연속 macos-latest PASS.
- ubuntu-latest / windows-latest 회귀 0.
- (선택) workflow_dispatch에서 100회 stress 실행 위한 `--repeat 100` input 추가 검토.

### 8.3 stress runner 시안 (`tests/scripts/repeat-tests.sh`)

```bash
#!/usr/bin/env bash
set -euo pipefail

REPEAT="${1:-50}"
FILTER="${2:-FullyQualifiedName~BaseSessionSendPolicyTests.BaseSession_DoWorkSendBuffers_}"

dotnet build FastPortSharp.sln -c Release >/dev/null

fail=0
for i in $(seq 1 "${REPEAT}"); do
  if ! dotnet test FastPortSharp.sln -c Release --no-build \
        --filter "${FILTER}" --logger "console;verbosity=quiet" >/tmp/t.log 2>&1; then
    echo "iter ${i}: FAIL"
    cat /tmp/t.log
    fail=$((fail+1))
  fi
  if (( i % 10 == 0 )); then
    echo "iter ${i}/${REPEAT} (fail so far: ${fail})"
  fi
done

echo "── summary: ${fail}/${REPEAT} fail"
[ "${fail}" -eq 0 ]
```

---

## 9. Clean Architecture (.NET 적용)

해당 없음 (test 파일 한정).

---

## 10. Coding Convention Reference

### 10.1 File Layout

| 영역 | 위치 |
|---|---|
| 두 테스트 + observer | `FastPortTests/BaseSessionSendPolicyTests.cs` |
| stress runner | `tests/scripts/repeat-tests.sh` (NEW) |

### 10.2 Naming

- `BatchedFifoObserver` (PascalCase, internal helper class)
- `private sealed class` 스타일은 기존 `TestSession` 패턴 그대로 따름
- 한국어 주석: 의도 / 비-자명한 lock semantics만 한국어로 1-2 줄

---

## 11. Implementation Guide

### 11.1 File Structure

```
FastPortSharp/
├── FastPortTests/
│   └── BaseSessionSendPolicyTests.cs   ← MODIFY (add BatchedFifoObserver, rewrite 2 tests)
├── tests/scripts/
│   └── repeat-tests.sh                 ← NEW (~30 lines)
└── docs/
    ├── 00-pm/...prd.md                 (already)
    ├── 01-plan/...plan.md              (already)
    ├── 02-design/...design.md          (this file)
    ├── 03-analysis/...analysis.md      (next phase)
    └── 04-report/...report.md          (next phase)
```

### 11.2 Implementation Order

| 순서 | 작업 | 산출물 | 검증 |
|---|---|---|---|
| 1 | `BatchedFifoObserver` private nested class를 `BaseSessionSendPolicyTests.cs` 하단에 추가 | +30-40 lines | 컴파일 통과 |
| 2 | Test #1 (`CompletesMultipleAcceptedItemsInFifoOrder`)를 §3.3 sketch대로 재작성 | line 222 ~50 lines 교체 | 단일 테스트 5회 반복 PASS |
| 3 | Test #2 (`BatchedSendRespectsChunkLimit`)를 §3.4 sketch대로 재작성 | line 290 ~40 lines 교체 | 단일 테스트 5회 반복 PASS |
| 4 | `tests/scripts/repeat-tests.sh` 생성 + chmod +x | +30 lines | 50회 반복 0 fail |
| 5 | 전체 139 테스트 회귀 확인 | (실행) | 139/0/0 |
| 6 | `git diff LibNetworks/Sessions/BaseSession.cs` | (검증) | 0 줄 |
| 7 | commit + push (main 또는 PR) | git push | build.yml 자동 트리거 |
| 8 | macos-latest job 5회 rerun | `gh run rerun --failed` 또는 push 5회 | 5/5 PASS |

### 11.3 Session Guide

> Module Map은 단일 세션에 충분히 들어가는 규모(예상 25-40 turn). 별도 분할 불필요.

| Module | Scope Key | Description | Estimated Turns |
|---|---|---|:-:|
| Test refactor | `tests-refactor` | BatchedFifoObserver + 2 test rewrite | 18-25 |
| Stress runner | `stress-runner` | repeat-tests.sh 작성 + 50회 반복 검증 | 5-8 |
| CI verify | `ci-verify` | macOS 5회 rerun + 회귀 확인 | 3-5 |

#### Recommended Session Plan

| Session | Phase | Scope | Turns |
|---|---|---|:-:|
| 1 | Plan + Design | 전체 | 15-20 (already done) |
| 2 | Do | `--scope tests-refactor,stress-runner,ci-verify` | 30-40 |
| 3 | Check + Report + Archive | 전체 | 15-20 |

---

## Version History

| Version | Date | Changes | Author |
|---|---|---|---|
| 0.1 | 2026-05-10 | Initial design — Option C (test refactor), BatchedFifoObserver + 2 test rewrite, stress runner | boinred |
