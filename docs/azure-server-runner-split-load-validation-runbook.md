# Azure Server With Local Runner Load Validation Runbook

> Feature: `cloud-server-runner-split-load-validation`
> Date: 2026-05-05
> Status: Draft

## Rule

Use Azure only after explicit cost confirmation.

The current environment has one active `Standard_B2s` reserved VM instance in `koreacentral`. Treat that as the server VM candidate. The default runner is the local Mac, not a second cloud VM.

Do not put tenant IDs, subscription IDs, private keys, public keys, IP addresses, or credentials into committed files.

GitHub Actions must not deploy cloud resources for this public repository. Use local scripts plus Azure CLI and SSH.

## Local Setup

Required local tools:

```bash
az --version
ssh -V
jq --version
```

Validate Azure authentication:

```bash
az account show
az group list --query "length(@)" -o tsv
```

Set local-only variables:

```bash
export FASTPORT_AZURE_LOCATION="koreacentral"
export FASTPORT_AZURE_RESOURCE_GROUP="fastport-load-rg"
export FASTPORT_AZURE_SERVER_SIZE="Standard_B2s"
export FASTPORT_RUNNER_MODE="local"
export FASTPORT_ENDPOINT_TYPE="public-ip"
export FASTPORT_AZURE_ADMIN_USER="azureuser"
export FASTPORT_AZURE_SSH_PUBLIC_KEY_PATH="$HOME/.ssh/id_ed25519.pub"
```

Run read-only discovery:

```bash
scripts/cloud/azure-discover.sh
```

This script does not create resources.

After the server VM is created, verify the expected metadata without printing concrete IP values:

```bash
export FASTPORT_AZURE_SERVER_VM="fastport-server-vm"
export FASTPORT_RUNNER_MODE="local"
scripts/cloud/azure-vm-readiness.sh
```

## Target Topology

| Role | Suggested name | Size | Responsibility |
|------|----------------|------|----------------|
| server | `fastport-server-vm` | `Standard_B2s` | Runs `FastPortTestSmokeServer` Release and writes `server.metrics.jsonl` |
| runner | local Mac | existing local machine | Runs `FastPortTestLoadValidation` Release and collects summary/client metrics |

Network target:

- Server is in Azure `koreacentral`.
- Runner connects from the local Mac to the server public IP or DNS on TCP `6628`.
- SSH TCP `22` is allowed only from the local admin IP.
- TCP `6628` is allowed only from the local public IP.
- A cloud runner VM is optional later if local-runner results cannot isolate the server bottleneck.

## Manual Approval Gates

Before creating anything:

1. Confirm the server VM is created as `Standard_B2s` in `koreacentral`.
2. Confirm cleanup command sequence.
3. Confirm SSH and TCP `6628` inbound rules are restricted to the local public IP.
4. Confirm generated public IPs and disks will be deleted if the test is discarded.

## VM Preparation

Run on the server VM:

```bash
sudo apt-get update
sudo apt-get install -y git jq tmux
```

Install .NET SDK for the selected Linux distribution using Microsoft's official package instructions.

Clone and build:

```bash
git clone https://github.com/boinred/FastPortSharp.git
cd FastPortSharp
git checkout main
dotnet build FastPortCharp.sln -c Release
dotnet test FastPortCharp.sln --no-build
```

Capture readiness:

```bash
scripts/cloud/os-readiness.sh
```

From the local machine, verify server SSH and local runner prerequisites:

```bash
export FASTPORT_SERVER_SSH_TARGET="azureuser@<server public ip or dns>"
export FASTPORT_RUNNER_MODE="local"
scripts/cloud/ssh-readiness.sh
```

The runtime scripts also write redacted cloud manifests under the configured output directory:

```text
artifacts/load-validation/cloud-server-runner-split/manifest.server.json
artifacts/load-validation/cloud-server-runner-split/manifest.runner-smoke.json
artifacts/load-validation/cloud-server-runner-split/manifest.runner-10k.json
```

These manifests record role, provider, location, VM size candidates, build configuration, git SHA, .NET version, and command name. They intentionally redact server host/IP details and do not record tenant IDs, subscription IDs, keys, or credentials.

## Server VM

Run:

```bash
cd FastPortSharp
tmux new -s fastport-server
scripts/cloud/server-start.sh
```

Verify listening socket:

```bash
ss -ltnp | grep 6628
```

Then verify the local runner can reach the server public endpoint:

```bash
cd FastPortSharp
export FASTPORT_RUNNER_MODE="local"
export FASTPORT_ENDPOINT_TYPE="public-ip"
export FASTPORT_SERVER_HOST="<server public ip or dns>"
export FASTPORT_SERVER_PORT=6628
scripts/cloud/runner-connectivity.sh
```

## Before Every Load Run

Start each smoke or 10K validation from a clean server process. Reusing a server process after a failed load run can leave stale sessions and pending sends that distort the next result.

On the server VM:

```bash
cd FastPortSharp
tmux kill-session -t fastport-server || true
tmux new -d -s fastport-server 'scripts/cloud/server-start.sh'
```

Then verify the listener and the latest server metrics:

```bash
ss -ltnp | grep 6628
tail -n 1 artifacts/load-validation/cloud-server-runner-split/server/server.metrics.jsonl | jq '.serverObserved | {currentSessions, pendingSendRequests, maxPendingSendRequests, sendBackpressureEvents, sendRejectedRequests, socketErrorCount}'
```

Before starting the runner, `currentSessions` should be `0` or explicitly explained. If it is not `0`, restart the server again and keep the stale value with the run notes.

## Local Runner

Set server public IP or DNS:

```bash
export FASTPORT_RUNNER_MODE="local"
export FASTPORT_ENDPOINT_TYPE="public-ip"
export FASTPORT_SERVER_HOST="<server public ip or dns>"
export FASTPORT_SERVER_PORT=6628
```

Run smoke first:

```bash
cd FastPortSharp
scripts/cloud/ssh-readiness.sh
scripts/cloud/runner-connectivity.sh
scripts/cloud/runner-smoke.sh
```

Run focused 10K only after smoke passes, server metrics are clean, and VM size is confirmed sufficient:

```bash
scripts/cloud/runner-10k.sh
```

## Artifact Collection

After smoke or 10K completes, collect server artifacts and copy local runner artifacts into the collected layout:

```bash
export FASTPORT_SERVER_SSH_TARGET="azureuser@<server public ip or dns>"
export FASTPORT_RUNNER_MODE="local"
scripts/cloud/collect-artifacts.sh
```

The collection script copies available server metrics plus local runner summaries, runner metrics, and redacted manifests under:

```text
artifacts/load-validation/cloud-server-runner-split/collected/
```

Do not commit generated artifacts. Use copied summary values only when updating `docs/load-validation-benchmark-results.md`.

## Cleanup

After validation:

- Stop server process.
- Stop or delete the Azure server VM and disk if it is not intentionally kept.
- Confirm public IPs and NSG rules are removed if no longer needed.
- Do not commit generated artifacts.

## References

- `docs/01-plan/features/cloud-server-runner-split-load-validation.plan.md`
- `docs/02-design/features/cloud-server-runner-split-load-validation.design.md`
- `docs/loadrunner-os-limits.md`
- Azure free services: https://azure.microsoft.com/pricing/free-services
- Azure B-series burstable VM sizes: https://learn.microsoft.com/azure/virtual-machines/sizes/b-series-burstable
