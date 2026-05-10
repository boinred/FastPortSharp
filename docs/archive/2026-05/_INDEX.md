# Archive Index — 2026-05

Completed PDCA cycles archived in May 2026.

| Cycle | Match Rate | Final Status | Folder |
|---|:-:|---|---|
| `game-server-template-from-network-engine` | 100% | LibCommons/LibNetworks를 네트워크 엔진으로, FastPortGameServerTemplate 신규 starter 프로젝트 추가. SampleClient로 echo round-trip 검증. | [./game-server-template-from-network-engine/](./game-server-template-from-network-engine/) |
| `game-server-template-scaffold-scripts` | 100% | Cross-platform `scripts/scaffold-game-server.{sh,ps1}` + 7 golden cases + 3-OS CI matrix. 5분 → 10초 부트스트랩(30배 단축). | [./game-server-template-scaffold-scripts/](./game-server-template-scaffold-scripts/) |
| `fix-base-session-send-fifo-test-flakiness` | 100% | `BaseSessionSendPolicyTests` 두 메서드의 race를 black-box `BatchedFifoObserver`로 제거. Production 0줄 변경. macOS GHA 5/5 + 로컬 50/50. | [./fix-base-session-send-fifo-test-flakiness/](./fix-base-session-send-fifo-test-flakiness/) |
| `fix-server-telemetry-export-jsonl-flush-flakiness` | 95-98% | `ServerTelemetryExport` test의 windows flake. 진짜 원인은 reader `FileShare.Read` mismatch. Reader fix + production hardening (WriteThrough, FileShare.ReadWrite, Math.Max(0.05) clamp). 3-OS × 5 = 15/15 PASS. | [./fix-server-telemetry-export-jsonl-flush-flakiness/](./fix-server-telemetry-export-jsonl-flush-flakiness/) |

## Conventions

- Each cycle folder contains the full PDCA document chain: `*.prd.md` → `*.plan.md` → `*.design.md` → `*.analysis.md` → `*.report.md`.
- Archives are immutable references — corrections happen in a new cycle, not by editing archived files.
