# Archive Index — 2026-05

Completed PDCA cycles archived in May 2026.

| Cycle | Match Rate | Final Status | Folder |
|---|:-:|---|---|
| `game-server-template-from-network-engine` | 100% | LibCommons/LibNetworks를 네트워크 엔진으로, FastPortGameServerTemplate 신규 starter 프로젝트 추가. SampleClient로 echo round-trip 검증. | [./game-server-template-from-network-engine/](./game-server-template-from-network-engine/) |
| `game-server-template-scaffold-scripts` | 100% | Cross-platform `scripts/scaffold-game-server.{sh,ps1}` + 7 golden cases + 3-OS CI matrix. 5분 → 10초 부트스트랩(30배 단축). | [./game-server-template-scaffold-scripts/](./game-server-template-scaffold-scripts/) |
| `fix-base-session-send-fifo-test-flakiness` | 100% | `BaseSessionSendPolicyTests` 두 메서드의 race를 black-box `BatchedFifoObserver`로 제거. Production 0줄 변경. macOS GHA 5/5 + 로컬 50/50. | [./fix-base-session-send-fifo-test-flakiness/](./fix-base-session-send-fifo-test-flakiness/) |
| `fix-server-telemetry-export-jsonl-flush-flakiness` | 95-98% | `ServerTelemetryExport` test의 windows flake. 진짜 원인은 reader `FileShare.Read` mismatch. Reader fix + production hardening (WriteThrough, FileShare.ReadWrite, Math.Max(0.05) clamp). 3-OS × 5 = 15/15 PASS. | [./fix-server-telemetry-export-jsonl-flush-flakiness/](./fix-server-telemetry-export-jsonl-flush-flakiness/) |
| `move-test-projects-to-testprojects-folder` | 97% | 5 test 프로젝트(`FastPortTests`, `FastPortTestLoadRunner`, `FastPortTestLoadValidation`, `FastPortTestSmokeServer`, `LibTestTelemetry`)를 `tests-projects/`로 일괄 이동. Production 0줄 변경. Sanity review로 README/HANDOFF gap 19건 발견·수정 (lesson: head truncation 주의). | [./move-test-projects-to-testprojects-folder/](./move-test-projects-to-testprojects-folder/) |
| `maui-telemetry-dashboard-foundation` | 95-97.75% | `FastPortDashboard.Maui` (macOS Catalyst + Windows desktop) + 별도 `FastPortSharp.Dashboard.sln`로 build.yml CI 격리. MVVM + LiveCharts2 + IPollingAdapter (Mock/Jsonl) + 6 KPI + JSONL polling. 직전 cycle의 FileShare.ReadWrite lesson 재활용. RTT chart는 follow-up cycle로 분리. | [./maui-telemetry-dashboard-foundation/](./maui-telemetry-dashboard-foundation/) |
| `dashboard-mvvm-toolkit-migration` | 96% | `DashboardViewModel`을 `CommunityToolkit.Mvvm` 8.4 source generator로 migration. `[ObservableProperty]` × 11 + `[RelayCommand]` × 2 + `[NotifyCanExecuteChangedFor]` × 2. Manual INPC/Command 0건 잔존. LOC 193→118 (~39%, boilerplate 자체는 ~80%↓). XAML binding 무변경. 단일 commit `a82c25c`. Lesson: Plan LOC 추정 시 boilerplate와 비즈니스 로직 분리 필요. | [./dashboard-mvvm-toolkit-migration/](./dashboard-mvvm-toolkit-migration/) |

## Conventions

- Each cycle folder contains the full PDCA document chain: `*.prd.md` → `*.plan.md` → `*.design.md` → `*.analysis.md` → `*.report.md`.
- Archives are immutable references — corrections happen in a new cycle, not by editing archived files.
