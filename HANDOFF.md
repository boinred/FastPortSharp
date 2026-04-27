# FastPortSharp Handoff

> Last updated: 2026-04-28  
> Branch: `main`  
> Remote status at handoff: `main...origin/main` clean/synced before this file was added

## Current State

- Latest pushed commit: `fbc00cb Add FastPort smoke server telemetry`
- Working feature just completed and archived: `fastport-smoke-server`
- PDCA active features: none
- `FastPortBenchmark` has been replaced by `FastPortLoadRunner`
- `FastPortSmokeServer` now owns echo/protocol smoke behavior
- `FastPortServer` has been simplified back toward a basic network engine host/sample

## Completed Work

### FastPortLoadRunner

- Replaced old benchmark-oriented flow with a real TCP load runner.
- Supports fixed payload and random payload profiles.
- Uses `EchoRequest` / `EchoResponse` framing.
- Emits client-side metrics and JSONL output.
- Archived PDCA docs under:
  - `docs/archive/2026-04/fastport-loadrunner/`

### FastPortSmokeServer + Telemetry

- Added generic telemetry primitives in `LibNetworks.Telemetry`.
- Instrumented:
  - accept success/failure
  - disconnects
  - connected sessions
  - received/sent packets and bytes
  - socket errors
  - parse/protocol errors
- Added `FastPortSmokeServer` as a dedicated smoke/test server.
- Moved echo/protocol smoke responsibility out of `FastPortServer`.
- Added integration smoke tests:
  - fixed 1K payload
  - random 4K-16K payload
- Latest verified result before commit:
  - `dotnet build FastPortCharp.sln` passed
  - `dotnet test FastPortCharp.sln --no-build` passed, 56 tests
- Archived PDCA docs under:
  - `docs/archive/2026-04/fastport-smoke-server/`

## Important Architecture Decisions

- `FastPortServer` should remain a basic, ready-to-use network engine host/sample.
- Test protocol behavior, telemetry smoke logic, and LoadRunner compatibility checks belong in `FastPortSmokeServer`.
- `LibNetworks` may expose protocol-neutral telemetry primitives, but it should not know game protocol details.
- Future game servers should be able to use the engine without inheriting smoke-specific echo logic.

## Key Files

- `FastPortLoadRunner/`
- `FastPortSmokeServer/`
- `FastPortServer/`
- `LibNetworks/Telemetry/ServerTelemetry.cs`
- `LibCommonTest/FastPortSmokeServerTests.cs`
- `LibCommonTest/ServerTelemetryTests.cs`
- `docs/.pdca-status.json`
- `docs/archive/2026-04/fastport-loadrunner/`
- `docs/archive/2026-04/fastport-smoke-server/`

## Remaining Known Limits

- `sentPackets` currently means socket send completion count, not exact FastPort packet count.
- `receivedBytes` currently uses parsed packet size, not raw socket receive bytes.
- Negative smoke tests for malformed packet / wrong protocol id are not implemented.
- 1,000 / 3,000 / 5,000 / 10,000 staged load validation has not been run.
- There is no telemetry export API yet for MAUI dashboard consumption.

## Recommended Next Work

Recommended next PDCA:

```text
$pdca pm telemetry-export-metric-contract
```

Rationale:

- MAUI dashboard and staged load validation both need stable metric names and exact semantics first.
- The current telemetry fields are useful, but some names need a clear contract:
  - packet count vs socket completion count
  - parsed packet bytes vs raw socket bytes
  - client-observed vs server-observed metrics

Suggested scope:

- Define a server/client metric contract.
- Decide exact field names and units.
- Add a telemetry snapshot/export surface usable by dashboard clients.
- Add focused tests for metric semantics.

Alternative next PDCA options:

```text
$pdca pm staged-load-validation
$pdca pm maui-dashboard
$pdca pm game-server-template
```

## Suggested Commands

Check repository state:

```bash
git status --short --branch
```

Run tests:

```bash
dotnet test FastPortCharp.sln --no-build
```

Build:

```bash
dotnet build FastPortCharp.sln
```

Inspect recent commits:

```bash
git log --oneline -5
```

## Notes For Next Session

- If continuing with PDCA, start by checking:
  - `docs/.pdca-status.json`
  - latest archive docs under `docs/archive/2026-04/`
- Do not move telemetry-specific echo behavior back into `FastPortServer`.
- Keep new telemetry export work protocol-neutral unless it belongs explicitly to `FastPortSmokeServer`.
- Commit `HANDOFF.md` separately if it should be preserved in repo history.
