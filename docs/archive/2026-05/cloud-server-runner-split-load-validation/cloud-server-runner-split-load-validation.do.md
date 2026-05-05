# cloud-server-runner-split-load-validation - Do Notes

> Plan: docs/01-plan/features/cloud-server-runner-split-load-validation.plan.md
> Design: docs/02-design/features/cloud-server-runner-split-load-validation.design.md
> Status: Local Implementation Complete; Cloud Runtime Pending

## Implemented In This Pass

- Added a free-tier-only cloud validation runbook:
  - `docs/cloud-server-runner-split-load-validation-runbook.md`
- Added an Azure transition runbook:
  - `docs/azure-server-runner-split-load-validation-runbook.md`
- Added local cloud helper scripts:
  - `scripts/cloud/azure-discover.sh`
  - `scripts/cloud/free-tier-guard.sh`
  - `scripts/cloud/oci-discover.sh`
  - `scripts/cloud/os-readiness.sh`
  - `scripts/cloud/write-manifest.sh`
  - `scripts/cloud/azure-vm-readiness.sh`
  - `scripts/cloud/ssh-readiness.sh`
  - `scripts/cloud/runner-connectivity.sh`
  - `scripts/cloud/collect-artifacts.sh`
  - `scripts/cloud/server-start.sh`
  - `scripts/cloud/runner-smoke.sh`
  - `scripts/cloud/runner-10k.sh`

## Free Tier Guard

The local guard refuses the default validation configuration unless it stays within:

- region: `us-chicago-1`
- shape: `VM.Standard.A1.Flex`
- total compute: `4 OCPU / 24GB RAM`
- planned boot volume total: `200GB`

The first-pass split remains:

| Role | OCPU | Memory |
|------|-----:|-------:|
| server | 2 | 12GB |
| runner | local | local |

## Deployment Boundary

GitHub Actions based OCI deployment is intentionally excluded because this is a public repository.

The accepted deployment path is:

```text
local machine -> Azure/OCI CLI / SSH -> server VM
local machine -> FastPortTestLoadValidation -> server public endpoint
```

Secrets stay local. Scripts must read environment variables and must not contain account-specific values.

## Discovery Verification

Read-only OCI discovery was verified from the local machine without creating resources:

- subscribed region: `us-chicago-1`
- status: `READY`
- home region: `true`
- availability domains:
  - `ZuMU:US-CHICAGO-1-AD-1`
  - `ZuMU:US-CHICAGO-1-AD-2`
  - `ZuMU:US-CHICAGO-1-AD-3`
- `VM.Standard.A1.Flex` is visible in `ZuMU:US-CHICAGO-1-AD-1`
- recent ARM64 image candidates include:
  - `Canonical-Ubuntu-24.04-aarch64-2026.03.31-0`
  - `Canonical-Ubuntu-22.04-aarch64-2026.03.31-0`
  - `Oracle-Linux-9.7-aarch64-2026.03.31-0`

No OCIDs, keys, public IPs, private IPs, or resource IDs are recorded in repository files.

## Capacity Status

OCI Free Tier A1 provisioning is currently blocked by regional capacity/rate limiting, not by project code.

Attempted target:

| Role | Shape | OCPU | Memory | Result |
|------|-------|-----:|-------:|--------|
| server | `VM.Standard.A1.Flex` | 2 | 12GB | `Out of host capacity` in AD-1 |
| server | `VM.Standard.A1.Flex` | 2 | 12GB | `Out of host capacity` in AD-2 |
| server | `VM.Standard.A1.Flex` | 2 | 12GB | rate-limited in AD-3 |

Follow-up smaller target was started but cancelled before completion to avoid overlapping with manual Console provisioning:

| Role | Shape | OCPU | Memory | Result |
|------|-------|-----:|-------:|--------|
| server | `VM.Standard.A1.Flex` | 1 | 6GB | cancelled before confirmed creation |

Post-cancel preflight showed:

- active A1 instances: `0`
- active boot volumes: `0GB`
- active VCNs: `fastport-load-vcn`

Cloud validation remains pending until the Azure server VM is prepared or OCI A1 capacity becomes available.

## Azure Transition

OCI A1 capacity remained blocked, so the implementation target is now Azure.

Local Azure CLI verification:

| Check | Result |
|-------|--------|
| `which az` | `/opt/homebrew/bin/az` |
| `az --version` | `azure-cli 2.85.0` |
| `az account show` | Succeeded |
| Azure environment | `AzureCloud` |
| Subscription state | `Enabled` |
| `az group list --query "length(@)" -o tsv` | `8` |
| Active reservation | `Standard_B2s`, `koreacentral`, quantity `1`, utilization `0%` |
| `az vm list -d` in `koreacentral` | no existing VMs |

The active reservation should be used as the first server VM candidate when the user creates the server. A runner VM is not part of the default topology; the local Mac is the default runner.

Added Azure discovery script:

```text
scripts/cloud/azure-discover.sh
```

The script checks:

- current Azure account summary without printing tenant/subscription IDs;
- accessible resource group count;
- active reservations;
- existing `koreacentral` VMs;
- selected VM SKU availability/restrictions in the chosen region.

Default Azure planning values:

```bash
export FASTPORT_AZURE_LOCATION="koreacentral"
export FASTPORT_AZURE_SERVER_SIZE="Standard_B2s"
```

No Azure resource group, VNet, NSG, public IP, disk, or VM was created in this pass.

## Azure Runtime Verification

After the user-created Azure VM became available, the server-only cloud topology was verified with the local Mac as the runner.

Verified server setup:

| Item | Result |
|------|--------|
| Cloud provider | Azure |
| Region | `koreacentral` |
| VM size | `Standard_B2s` |
| Server role | `FastPortTestSmokeServer` only |
| Runner role | local Mac |
| SSH readiness | Passed with PEM key auth |
| TCP `6628` readiness | Passed from local runner |
| Smoke validation | Passed |
| Focused 10K validation | Completed, failed guardrails |
| Artifact collection | Passed |

The focused 10K run is recorded as a failed cloud baseline rather than a setup failure:

- Summary: `artifacts/load-validation/cloud-server-runner-split/s5-random-10k/summary.md`
- Run ID: `20260505-140926-staged`
- Peak: `9,337 / 10,000`
- Peak ratio: `93.37%`
- Max TPS: `1,085.41`
- Final disconnects: `752`
- RTT P95/P99: `106,216.65ms` / `274,206.02ms`
- Socket classes: `receive|IOException|ConnectionReset = 495`, `receive|IOException|TimedOut = 257`, `connect|SocketException|TimedOut = 56`

Post-run server telemetry showed lingering state after runner exit:

- `currentSessions = 51`
- `pendingSendRequests = 27`

Restart the Azure smoke server before the next validation run.

## Artifact Manifest Implementation

Added a shared manifest writer:

```text
scripts/cloud/write-manifest.sh
```

The server and runner scripts now call it before runtime validation:

| Script | Manifest |
|--------|----------|
| `scripts/cloud/server-start.sh` | `manifest.server.json` / `manifest.server.md` |
| `scripts/cloud/runner-smoke.sh` | `manifest.runner-smoke.json` / `manifest.runner-smoke.md` |
| `scripts/cloud/runner-10k.sh` | `manifest.runner-10k.json` / `manifest.runner-10k.md` |

Manifest fields include:

- role;
- provider and location;
- resource group name if provided;
- endpoint type;
- runner mode;
- redacted server-host state;
- server port;
- server size candidate;
- optional runner size candidate;
- build configuration;
- git SHA/branch;
- tracked dirty flag;
- .NET version;
- command name.

The manifest intentionally does not record tenant IDs, subscription IDs, OCIDs, keys, credentials, or concrete host/IP values.

Local verification:

```text
bash -n scripts/cloud/*.sh
FASTPORT_CLOUD_OUTPUT=/tmp/fastport-cloud-manifest-test scripts/cloud/write-manifest.sh runner-smoke
jq empty /tmp/fastport-cloud-manifest-test/manifest.runner-smoke.json
```

## Runtime Work Pending Azure VM Preparation

- Wait until the user creates the Azure server VM, then verify SSH and `Standard_B2s` placement.
- Verify server SSH and local-runner public endpoint connectivity.
- Run smoke validation before any focused 10K run.
- Collect artifacts and update `docs/load-validation-benchmark-results.md` only with selected summary values.

## Direction Update: Server-Only Cloud Baseline

The default validation topology is now:

```text
Azure server VM -> FastPortTestSmokeServer
Local Mac -> FastPortTestLoadValidation / FastPortTestLoadRunner
```

This better matches the real external-client path. A cloud runner VM is now a follow-up option for controlled private-network comparison only when local-runner results cannot isolate the server bottleneck.

## Act Iteration: Runtime Hardening Helpers

Added helper scripts for the gap items found during Check:

| Gap | Helper |
|-----|--------|
| Verify Azure VM placement and running state | `scripts/cloud/azure-vm-readiness.sh` |
| Verify server SSH and local runner prerequisites | `scripts/cloud/ssh-readiness.sh` |
| Verify local runner to server TCP `6628` path | `scripts/cloud/runner-connectivity.sh` |
| Collect server and local runner artifacts without committing generated outputs | `scripts/cloud/collect-artifacts.sh` |

The helpers are intentionally read-only or runtime-local. They do not create Azure resources and do not write tenant IDs, subscription IDs, keys, or concrete IP addresses to repository files.
