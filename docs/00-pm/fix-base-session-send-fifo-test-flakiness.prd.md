# fix-base-session-send-fifo-test-flakiness PRD

> **Lightweight PRD** (internal QA reliability cycle — full pm-discovery /
> pm-research / pm-strategy / beachhead+GTM analysis omitted as inappropriate
> for a single-test stabilisation task. Persona = repo committer + GHA CI;
> no external GTM.)
>
> **Project**: FastPortSharp
> **Author**: boinred
> **Date**: 2026-05-10
> **Trigger**: build.yml CI run 25616156103 — macos-latest job failed on
> `BaseSession_DoWorkSendBuffers_CompletesMultipleAcceptedItemsInFifoOrder`
> while ubuntu / windows passed.

---

## 1. Executive Summary

| 관점 | 내용 |
|---|---|
| **Problem** | `BaseSessionSendPolicyTests` 한 테스트가 send queue worker와 test driver 사이의 race로 GHA macOS runner에서 비결정적으로 실패. CI 신뢰도 저하 + main 브랜치 빨간 신호. |
| **Solution** | TestSession에 worker 시작 직전 1회만 동작하는 동기화 hook을 추가하여 두 `TrySendBytes` 호출이 모두 enqueue된 후에만 batch building이 진행되도록 보장. Production 코드 변경 0. |
| **Function/UX/Effect** | 동일 시나리오를 결정적으로 검증. 다른 timing-민감 테스트도 같은 hook으로 안정화 가능한 재사용 패턴. |
| **Core Value** | CI flake 0 → PR 머지 차단 사유에서 "test 재실행 부탁드려요" 제거. main을 신뢰 가능한 green 신호로 복원. |

---

## 2. Problem Statement

### 2.1 Observed Failure

- CI run: `25616156103` (build.yml first execution)
- Job: `macos-latest`
- Test: `FastPortTests.BaseSessionSendPolicyTests.BaseSession_DoWorkSendBuffers_CompletesMultipleAcceptedItemsInFifoOrder`
- Duration: 3 s (timed out via `WaitForSnapshotAsync(..., TimeSpan.FromSeconds(3))`)
- Assertion: `BaseSessionSendPolicyTests.cs:237` — `Assert.IsTrue(sendBuffers.Count >= 2)` fails

### 2.2 Root Cause

```
test thread                     worker thread
-----------                     -------------
TrySendBytes(buf1)
  → m_SendQueue.Writer.TryWrite(buf1)
                                m_SendQueue.Reader.WaitToReadAsync → true
                                TryRead(buf1) → pendingSendItems
                                WaitForSendTelemetryRegistrationAsync(buf1)
                                BuildSendSegmentsAsync:
                                  foreach pending (buf1) → segments[0]
                                  while TryRead(out item) → false (buf2 not yet!)
TrySendBytes(buf2)              SendSocketAsync(socket, segments=[buf1])
                                → sendBatchOverride called with sendBuffers.Count == 1
                                → Assert.IsTrue(sendBuffers.Count >= 2) FAILS
  → m_SendQueue.Writer.TryWrite(buf2)
```

테스트는 두 send를 같은 batch에 묶이는 것을 가정하지만, channel 기반 worker는 더 빠르게 동작할 수 있어 첫 batch에 buf1만 들어감.

### 2.3 Why now / why macOS only

- 로컬 macOS (Apple Silicon) 및 GHA Ubuntu/Windows runner는 worker 시작이 미세하게 늦어 우연히 통과해 왔음.
- GHA macos-latest는 Intel 기반 + 가상화 오버헤드로 worker context switch가 빨라 race가 더 자주 노출.
- Cycle `game-server-template-scaffold-scripts` 직전까지 PR CI가 없어서 macOS runner에서 빌드/테스트가 돌지 않았음 → 노출 안 됨.
- 새 `build.yml` (commit `6693573`)이 처음으로 macos-latest에서 풀 테스트를 돌리며 노출.

---

## 3. Scope

### 3.1 In Scope

- TestSession (또는 동등한 fixture)에 "worker가 첫 batch building 직전 1회 await할 수 있는 동기화 gate" 추가
- 영향 받는 테스트(`BaseSession_DoWorkSendBuffers_CompletesMultipleAcceptedItemsInFifoOrder`) 1개 수정 — 두 `TrySendBytes` 후 gate 해제하도록
- 동일 패턴을 활용 가능한 자매 테스트(`BatchedSendRespectsChunkLimit` 등)도 같은 시그니처로 일관성 유지 검토 (필요 시만)
- CI 5회 연속 macOS PASS로 결정성 검증

### 3.2 Out of Scope

- Production `BaseSession.cs` 동작 변경
- 다른 flaky 테스트 사냥 (별도 cycle)
- 테스트 framework 변경 (계속 MSTest)
- macOS runner 자체를 matrix에서 제거

---

## 4. Constraints

- Production code 1 byte도 건드리지 않을 것 (test isolation 원칙)
- 새 hook은 production path에 부담 0 (default no-op)
- MSTest 호환, 추가 NuGet 의존 0
- 수정 후 ubuntu / windows / macos 각각 5회 연속 통과 (수동 GHA re-run 또는 `dotnet test` 5회 로컬 반복)

---

## 5. Success Criteria

- [ ] build.yml의 macos-latest job이 5회 연속 PASS (`gh run rerun`)
- [ ] 동일 build.yml의 ubuntu-latest / windows-latest 회귀 0
- [ ] 로컬 `dotnet test FastPortSharp.sln -c Release` 50회 반복 0 fail (race detector 역할)
- [ ] BaseSession.cs (production) 변경 0 줄 (`git diff`로 확인)
- [ ] 변경 라인은 FastPortTests/ 하위 + (필요 시) TestSession 헬퍼 1-2 곳에만

---

## 6. Risks

| Risk | Mitigation |
|---|---|
| Hook 추가가 production timing에 영향 | hook은 virtual method override 또는 internal `Action`으로, production은 default no-op |
| 다른 테스트의 race 유발 | 기존 테스트 변경 시 빌드 후 전체 139 테스트 통과 확인 |
| macOS GHA runner intermittent passing | 5회 연속 rerun으로 결정성 확인. 부족하면 50회 stress mode (별도 GHA workflow_dispatch 옵션 추가) |

---

## 7. Stakeholders & Personas

| Persona | Pain Point |
|---|---|
| Repo committer (boinred) | "내 PR이 테스트 재실행 1번 더 해야 통과" → 머지 시간 ↑, 신뢰도 ↓ |
| 외부 contributor (가상) | flaky CI는 PR 진입 장벽. 이 cycle이 끝나면 onboarding 마찰 ↓ |
| AI agent (Claude / Codex) | flaky CI는 자동화 파이프라인을 무한 루프에 빠뜨림. 결정성은 LLM agent에게 특히 중요 |

---

## 8. Beachhead / GTM

해당 없음 (내부 QA 안정화).

---

## 9. Next Steps

1. `/pdca plan fix-base-session-send-fifo-test-flakiness`
   - 후보 fix 패턴(virtual hook vs Action delegate vs partial class) 비교
   - Plan §4 Success Criteria로 위 항목 그대로 인계
2. `/pdca design ...` — 3개 옵션 제시
3. 단일 세션 Do (≤ 30 turn) — TestSession + 1개 테스트 수정
4. Check + Report (CI 5회 PASS 확인 후)

---

## Version History

| Version | Date | Changes | Author |
|---|---|---|---|
| 1.0 | 2026-05-10 | Initial lightweight PRD (race analysis + scope) | boinred |
