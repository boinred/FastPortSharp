# cloud-server-runner-split-load-validation - Do Notes

> Plan: docs/01-plan/features/cloud-server-runner-split-load-validation.plan.md
> Design: docs/02-design/features/cloud-server-runner-split-load-validation.design.md
> Status: In Progress

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
| runner | 2 | 12GB |

## Deployment Boundary

GitHub Actions based OCI deployment is intentionally excluded because this is a public repository.

The accepted deployment path is:

```text
local machine -> OCI CLI / SSH -> server VM + runner VM
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

Cloud validation remains pending until Azure server/runner VMs are prepared or OCI A1 capacity becomes available.

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

The active reservation should be used as the first server VM candidate when the user creates the server. It does not cover the runner VM; runner size and expected cost still need explicit review.

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

## Next Implementation Work

- Wait until the user creates the Azure server VM, then verify SSH and `Standard_B2s` placement.
- Select runner VM size after cost/SKU review.
- Verify SSH and private-IP connectivity.
- Run smoke validation before any focused 10K run.
- Collect artifacts and update `docs/load-validation-benchmark-results.md` only with selected summary values.
