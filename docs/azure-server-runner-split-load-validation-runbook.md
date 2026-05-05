# Azure Server/Runner Split Load Validation Runbook

> Feature: `cloud-server-runner-split-load-validation`
> Date: 2026-05-05
> Status: Draft

## Rule

Use Azure only after explicit cost confirmation.

The current environment has one active `Standard_B2s` reserved VM instance in `koreacentral`. Treat that as the server VM candidate. The reservation quantity is `1`, so it does not cover a separate runner VM.

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
export FASTPORT_AZURE_RUNNER_SIZE="<runner size after review>"
export FASTPORT_AZURE_ADMIN_USER="azureuser"
export FASTPORT_AZURE_SSH_PUBLIC_KEY_PATH="$HOME/.ssh/id_ed25519.pub"
```

Run read-only discovery:

```bash
scripts/cloud/azure-discover.sh
```

This script does not create resources.

## Target Topology

| Role | Suggested name | Size | Responsibility |
|------|----------------|------|----------------|
| server | `fastport-server-vm` | `Standard_B2s` | Runs `FastPortTestSmokeServer` Release and writes `server.metrics.jsonl` |
| runner | `fastport-runner-vm` | TBD | Runs `FastPortTestLoadValidation` Release and collects summary/client metrics |

Network target:

- Same Azure region for server and runner.
- Same VNet/subnet for the first pass.
- Runner connects to server private IP on TCP `6628`.
- SSH TCP `22` is allowed only from the local admin IP.
- TCP `6628` is not opened to the public internet.

## Manual Approval Gates

Before creating anything:

1. Confirm the server VM is created as `Standard_B2s` in `koreacentral`.
2. Confirm selected runner SKU availability and expected cost.
3. Confirm cleanup command sequence.
4. Confirm public inbound rules are restricted.
5. Confirm generated public IPs and disks will be deleted if the test is discarded.

## VM Preparation

Run on each VM:

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

## Runner VM

Set server private IP:

```bash
export FASTPORT_SERVER_HOST="<server private ip>"
export FASTPORT_SERVER_PORT=6628
```

Run smoke first:

```bash
cd FastPortSharp
scripts/cloud/runner-smoke.sh
```

Run focused 10K only after smoke passes and VM size is confirmed sufficient:

```bash
scripts/cloud/runner-10k.sh
```

## Cleanup

After validation:

- Stop server process.
- Stop or delete Azure VMs and disks if they are not intentionally kept.
- Confirm public IPs and NSG rules are removed if no longer needed.
- Do not commit generated artifacts.

## References

- `docs/01-plan/features/cloud-server-runner-split-load-validation.plan.md`
- `docs/02-design/features/cloud-server-runner-split-load-validation.design.md`
- `docs/loadrunner-os-limits.md`
- Azure free services: https://azure.microsoft.com/pricing/free-services
- Azure B-series burstable VM sizes: https://learn.microsoft.com/azure/virtual-machines/sizes/b-series-burstable
