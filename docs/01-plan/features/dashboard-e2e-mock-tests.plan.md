# dashboard-e2e-mock-tests Plan

> **Project**: FastPortSharp · **Date**: 2026-05-11

## Executive Summary

| 관점 | 내용 |
|---|---|
| **Problem** | macOS 26 SwiftUI Observation crash로 UI 실행 검증이 막힘. 비즈니스 로직 (ViewModel + Adapter pipeline) end-to-end 동작을 신뢰할 verification 경로 부재. 기존 unit tests는 component 단위만 (ApplySnapshot, individual series). |
| **Solution** | Headless E2E tests 신규 — `MockPollingAdapter → DashboardViewModel.PumpAsync → state 변화 검증`을 UI 없이 풀 pipeline로 실행. 5 tests 추가 (총 25). |
| **Function/UX/Effect** | UI render 없이 비즈니스 로직 회귀 자동 감지. macOS 26 crash와 무관하게 신뢰 검증 경로 확보. CI(dashboard.yml)에서 자동 실행. |
| **Core Value** | UI 차단 환경에서 production 가치 (Mock/JSONL polling → ViewModel state) end-to-end 보장. |

## Context Anchor

| Key | Value |
|-----|-------|
| WHY | UI 실행 검증 차단된 상태에서 비즈니스 로직 신뢰 확보 |
| WHO | boinred + CI + 미래 contributor |
| RISK | (R-1) PumpAsync timeout flakiness / (R-2) Mock random walk 비결정성 / (R-3) Test 실행 시간 증가 |
| SUCCESS | 5 E2E tests pass + 기존 20 회귀 0 + 회귀 sln 0 + CI(dashboard.yml) 자동 실행 |
| SCOPE | `tests-projects/FastPortDashboardTests/E2E/MockE2ETests.cs` 신규만. Production/ViewModel/Adapter 변경 0 |

## 1. Scope

### In Scope

| 영역 | 작업 |
|---|---|
| `tests-projects/FastPortDashboardTests/E2E/MockE2ETests.cs` | 신규 — 5 E2E tests |

### Out of Scope

- Production 코드 (ViewModel/Adapter/UI) 변경
- UI 자동화 (Appium 등)
- JSONL E2E (Mock E2E 패턴 검증 후 별도 cycle)
- Performance benchmarking

## 2. Test Scenarios

| # | Test | 검증 |
|---|---|---|
| E2E-1 | `Mock_FullPipeline_PopulatesAllSeries` | PumpAsync 후 ClientRttSeries + ThroughputSeries 모두 채워짐 |
| E2E-2 | `Mock_AllRttPercentiles_Populated` | TimedRttPoint의 P50/P95/P99이 sane 범위 (0 < P50 ≤ P95 ≤ P99) |
| E2E-3 | `Mock_Cancellation_GracefullyTerminates` | CancellationToken cancel 시 OperationCanceledException 정상 propagation, ViewModel state 유효 |
| E2E-4 | `Mock_KpiUpdatesMonotonically` | TotalAcceptedSessions, TotalSentBytes 단조 증가 |
| E2E-5 | `Mock_StartAsync_FullLifecycle` | ViewModel.UseMock=true + ConnectCommand → State Polling, Disconnect → Disconnected |

## 3. Success Criteria

- [ ] `MockE2ETests.cs` 신규 (5 tests)
- [ ] 5 tests 모두 pass
- [ ] 기존 20 tests 회귀 0 (총 25)
- [ ] Dashboard 빌드 0/0
- [ ] FastPortSharp.sln 회귀 0
- [ ] Test 실행 시간 < 5s (전체 25 tests)
- [ ] CI workflow (dashboard.yml) 자동 path filter trigger 확인 (이미 `tests-projects/FastPortDashboardTests/**` 포함)
- [ ] 단일 commit

## 4. Next Steps

1. Design (Option A — single E2E file, MSTest)
2. Do
3. Analyze / Report / Archive
