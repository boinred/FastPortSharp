# cloud-server-runner-split-load-validation - Design Document

> Version: 1.0.0 | Date: 2026-05-01 | Status: Completed
> Level: Starter | Plan: docs/01-plan/features/cloud-server-runner-split-load-validation.plan.md

---

## 1. Overview

`cloud-server-runner-split-load-validation`은 cloud server VM에 local Mac runner가 외부에서 접속하는 FastPortSharp Release 기준 load validation baseline을 만드는 설계다. Feature 이름은 기존 PDCA 흐름을 유지하지만, 현재 기본 topology는 server-only cloud다.

이번 feature는 engine 최적화가 아니다. 실제 외부 클라이언트가 cloud server public endpoint에 접속하는 경로를 먼저 검증하고, 서버/로컬 runner/네트워크 영향을 명시적으로 기록하는 것이 목적이다.

2026-05-05 update: OCI A1 capacity 문제로 인해 현재 구현 대상 provider를 Azure로 전환한다. 아래 OCI 설계는 보류된 historical context이며, 실제 Do phase는 Azure CLI 기반 discovery와 Azure VM 준비 절차를 우선한다.

OCI CLI 인증은 로컬에서 확인된 상태다.

- Region: `us-chicago-1`
- Region key: `ORD`
- Region subscription status: `READY`

Azure CLI 접근도 로컬에서 확인된 상태다.

- `az --version`: `2.85.0`
- `az account show`: succeeded
- Environment: `AzureCloud`
- Subscription state: `Enabled`
- `az group list`: succeeded, accessible resource group count `8`
- Candidate location: `koreacentral`
- Active reservation: `Standard_B2s`, `koreacentral`, quantity `1`, utilization `0%`

The reserved instance should be treated as the server candidate. A runner VM is not part of the default topology anymore; it remains an optional controlled-baseline follow-up if local-runner results cannot isolate the bottleneck.

OCI 공식 Always Free 문서에 따르면 Always Free compute는 tenancy home region에서 만들어야 하며, Ampere A1 `VM.Standard.A1.Flex`는 월 `3,000 OCPU hours`와 `18,000 GB hours`가 무료 범위다. Always Free tenancy에서는 이것이 총 `4 OCPU / 24GB`에 해당한다. 단, `out of host capacity`는 정상적인 capacity 리스크로 보고 fallback을 둔다.

## 2. Architecture

### 2.0 Current Azure Target Topology

```text
Local Mac
  |
  | az CLI / SSH / artifact copy
  v
Azure koreacentral
  |
  +-- Resource group: fastport-load-rg
      |
      +-- VNet: fastport-load-vnet
          |
          +-- Subnet: fastport-load-subnet
              |
              +-- NSG: fastport-load-nsg
                  |
                  +-- fastport-server-vm
                  |     Size: Standard_B2s reserved instance candidate
                  |     Runs: FastPortTestSmokeServer Release
                  |     Listens: 0.0.0.0:6628
                  |     Telemetry: server.metrics.jsonl

Local Mac
  |
  | FastPortTestLoadValidation Release
  | Connects to server public IP/DNS:6628
  | Artifacts: summary + client metrics + copied server metrics
  v
fastport-server-vm public endpoint
```

Azure network rules:

| Direction | Rule | Reason |
|-----------|------|--------|
| Local admin -> server | TCP 22 from local public IP only | SSH deployment and log retrieval |
| Local runner -> server | TCP 6628 from local public IP only | External-client load validation traffic |
| Public internet except local IP -> 6628 | deny | Avoid public load-test exposure |

### 2.0.1 Azure Resource Strategy

The first Azure implementation pass is discovery and manual preparation, not provisioning automation.

| Item | Current choice |
|------|----------------|
| First region candidate | `koreacentral` |
| Server size candidate | `Standard_B2s` reserved instance |
| Runner mode | `local` |
| Optional runner size candidate | TBD only if controlled cloud-runner baseline is needed |
| Resource group name | `fastport-load-rg` |
| VNet/subnet | `fastport-load-vnet` / `fastport-load-subnet` |
| NSG | `fastport-load-nsg` |

Do not create resources until:

- the user creates the server VM or explicitly approves a CLI sequence;
- cleanup commands are ready;
- public inbound rules are restricted.

### 2.1 Target Topology

The OCI topology is historical fallback context. If OCI capacity becomes available, keep the same server-only cloud default and run the runner locally first.

```text
Local Mac
  |
  | SSH / artifact copy + FastPortTestLoadValidation
  v
OCI us-chicago-1 VCN
  |
  +-- Public Subnet 10.0.1.0/24
        |
        +-- fastport-server-a1
        |     Shape: VM.Standard.A1.Flex
        |     Suggested first pass: 2 OCPU / 12GB
        |     Runs: FastPortTestSmokeServer Release
        |     Listens: 0.0.0.0:6628
        |     Telemetry: server.metrics.jsonl
```

### 2.2 Network Boundary

Use one VCN and one subnet for the first pass.

| Direction | Rule | Reason |
|-----------|------|--------|
| Local admin -> server | TCP 22 from local public IP only | SSH deployment and log retrieval |
| Local runner -> server | TCP 6628 from local public IP only | Load validation traffic |
| server -> runner | ephemeral response traffic | TCP response path |
| public internet except local IP -> 6628 | deny | Avoid public load-test exposure |

The local runner should target the server public IP or DNS. This is the default external-client baseline. A cloud runner/private-IP path can be added later only for controlled bottleneck isolation.

### 2.3 Runtime Boundary

| Process | Host | Responsibility |
|---------|------|----------------|
| `FastPortTestSmokeServer` | server VM | Test echo server, server telemetry export |
| `FastPortTestLoadValidation` | local Mac | Stage orchestration, LoadRunner command generation, summary evaluation |
| `FastPortTestLoadRunner` | local Mac | Actual TCP client sessions and client observed JSONL |

`FastPortServer` remains a basic engine host/sample and is not used for this load validation path.

## 3. Resource Design

### 3.1 OCI Shape Strategy

First-pass target:

| VM | Shape | OCPU | Memory | Notes |
|----|-------|-----:|-------:|-------|
| server | `VM.Standard.A1.Flex` | 2 | 12GB | Runs server and telemetry export |
| runner | local Mac | - | - | Runs validation and client load |

This consumes only the server portion of the Always Free A1 envelope. If capacity is unavailable, the fallback order is:

1. Retry another availability domain in `us-chicago-1`.
2. Use smaller A1 shapes for smoke/staged lower session validation.
3. Use a smaller A1 server shape for smoke/lower-stage validation.
4. Postpone full cloud baseline until capacity is available.

The AMD micro shape is not considered suitable for 10K runner or server load.

### 3.2 OS Image

Use an Always Free eligible Linux image compatible with ARM64.

Preferred:

- Ubuntu ARM64 LTS, or
- Oracle Linux ARM64

The design does not require a specific distro as long as the following work:

- .NET SDK/runtime for `linux-arm64`
- SSH
- `ulimit` and sysctl checks
- file copy via `scp` or equivalent

## 4. Provisioning Design

### 4.0 Azure Local Inputs

Do not commit these values.

```bash
export FASTPORT_AZURE_LOCATION="koreacentral"
export FASTPORT_AZURE_RESOURCE_GROUP="fastport-load-rg"
export FASTPORT_AZURE_SERVER_SIZE="Standard_B2s"
export FASTPORT_RUNNER_MODE="local"
export FASTPORT_AZURE_ADMIN_USER="azureuser"
export FASTPORT_AZURE_SSH_PUBLIC_KEY_PATH="$HOME/.ssh/id_ed25519.pub"
```

Read-only discovery:

```bash
scripts/cloud/azure-discover.sh
```

This script must not create resource groups, VNets, public IPs, NICs, disks, or VMs.

### 4.1 Local Inputs

Do not commit these values.

```bash
export OCI_COMPARTMENT_OCID="<compartment ocid>"
export OCI_AVAILABILITY_DOMAIN="<availability domain name>"
export OCI_SSH_PUBLIC_KEY_PATH="$HOME/.ssh/id_ed25519.pub"
export FASTPORT_CLOUD_OUTPUT="artifacts/load-validation/cloud-server-runner-split"
```

The local `~/.oci/config` remains outside the repo.

### 4.2 Discovery Commands

Use CLI discovery before provisioning.

```bash
oci iam region-subscription list
oci iam availability-domain list --compartment-id "$OCI_COMPARTMENT_OCID"
oci compute shape list --compartment-id "$OCI_COMPARTMENT_OCID" --availability-domain "$OCI_AVAILABILITY_DOMAIN"
```

Image discovery should filter for ARM64-compatible images and the selected OS.

```bash
oci compute image list \
  --compartment-id "$OCI_COMPARTMENT_OCID" \
  --shape VM.Standard.A1.Flex \
  --sort-by TIMECREATED \
  --sort-order DESC
```

### 4.3 Network Resources

The first design can use Console or CLI. The target resources are:

| Resource | Suggested Name | CIDR / Setting |
|----------|----------------|----------------|
| VCN | `fastport-load-vcn` | `10.0.0.0/16` |
| Subnet | `fastport-load-public-subnet` | `10.0.1.0/24` |
| Internet Gateway | `fastport-load-igw` | enabled |
| Route Table | `fastport-load-public-rt` | default route to IGW |
| Security List or NSG | `fastport-load-nsg` | SSH + runner-to-server 6628 |

Security should prefer NSG rules if available. Keep port `6628` closed to the public internet.

### 4.4 Instance Resources

Instance names:

- `fastport-server-a1`
- `fastport-runner-a1`

Both instances should be tagged or named so artifacts can record their roles.

Minimum instance metadata to capture in the run manifest:

- instance name
- shape
- OCPU
- memory
- region
- availability domain
- image name/version
- private IP
- public IP presence

## 5. Deployment Design

### 5.1 Automation Boundary

Deployment and load validation should be driven from the local machine, not GitHub Actions.

Reasons:

- This is a public GitHub repository.
- OCI credentials, SSH keys, VM addresses, and cloud control permissions should remain local.
- GitHub Actions should stay limited to build/test workflows unless a separate hardening pass is explicitly approved.
- Cloud validation failures should be easier to diagnose before CI, deployment, networking, and benchmark execution are combined.

For the Azure transition, the expected automation shape is local scripts plus Azure CLI and SSH. OCI scripts remain historical/blocked until OCI capacity is available:

```text
scripts/cloud/
  azure-discover.sh
  oci-discover.sh
  deploy-server.sh
  ssh-readiness.sh
  runner-connectivity.sh
  run-smoke.sh
  run-10k.sh
  collect-artifacts.sh
```

These scripts may be committed later only if they contain no secrets and read environment variables for all account-specific values.

### 5.2 Build Strategy

Two acceptable modes exist.

Mode A: build on each VM.

```bash
git clone https://github.com/boinred/FastPortSharp.git
cd FastPortSharp
dotnet build FastPortCharp.sln -c Release
dotnet test FastPortCharp.sln --no-build
```

Mode B: publish locally for `linux-arm64`, copy artifacts.

```bash
dotnet publish FastPortTestSmokeServer/FastPortTestSmokeServer.csproj -c Release -r linux-arm64 --self-contained false
dotnet publish FastPortTestLoadValidation/FastPortTestLoadValidation.csproj -c Release -r linux-arm64 --self-contained false
dotnet publish FastPortTestLoadRunner/FastPortTestLoadRunner.csproj -c Release -r linux-arm64 --self-contained false
```

For the first pass, Mode A is simpler and records the exact git SHA on each VM.

### 5.3 Server VM Runtime

Server command:

```bash
mkdir -p artifacts/load-validation/cloud-server-runner-split

dotnet run -c Release --project FastPortTestSmokeServer -- \
  --FastPortTestSmokeServer:Host 0.0.0.0 \
  --FastPortTestSmokeServer:Port 6628 \
  --Telemetry:Output artifacts/load-validation/cloud-server-runner-split/server.metrics.jsonl \
  --Telemetry:IntervalSeconds 1
```

If app configuration binding does not accept command-line `--Section:Key` arguments in this project host, set the same values through environment variables:

```bash
export FastPortTestSmokeServer__Host=0.0.0.0
export FastPortTestSmokeServer__Port=6628
export Telemetry__Output=artifacts/load-validation/cloud-server-runner-split/server.metrics.jsonl
export Telemetry__IntervalSeconds=1
dotnet run -c Release --project FastPortTestSmokeServer
```

### 5.4 Local Runner Runtime

Start with progressive staged validation before full 10K interpretation.

Smoke:

```bash
export FASTPORT_RUNNER_MODE=local
export FASTPORT_ENDPOINT_TYPE=public-ip
export FASTPORT_SERVER_HOST="<server public ip or dns>"
export FASTPORT_SERVER_PORT=6628
scripts/cloud/runner-smoke.sh
```

Focused 10K:

```bash
export FASTPORT_RUNNER_MODE=local
export FASTPORT_ENDPOINT_TYPE=public-ip
export FASTPORT_SERVER_HOST="<server public ip or dns>"
export FASTPORT_SERVER_PORT=6628
scripts/cloud/runner-10k.sh
```

Important: the local runner will not have live access to the server metrics file unless it is copied from the server VM. First-pass options:

1. Run validation without `--server-metrics` first.
2. Copy `server.metrics.jsonl` from the server VM after the run.
3. Merge or compare the server and local runner timelines from collected artifacts.

The design prefers this two-step workflow for the first public-endpoint validation.

## 6. Artifact Design

### 6.1 Directory Layout

Generated artifacts remain ignored under `artifacts/load-validation/`.

```text
artifacts/load-validation/cloud-server-runner-split/
  manifest.md
  manifest.json
  server/
    server.metrics.jsonl
    server.log
    os-readiness.txt
  runner/
    runner.log
    os-readiness.txt
  smoke/
    summary.md
    summary.json
    *.metrics.jsonl
    *.combined.metrics.jsonl
  s5-random-10k/
    summary.md
    summary.json
    s5-random-10k.metrics.jsonl
    s5-random-10k.combined.metrics.jsonl
```

### 6.2 Manifest Fields

`manifest.json` should include:

- git SHA
- branch
- build configuration
- .NET SDK/runtime versions
- server role metadata
- runner role metadata
- runner mode: `local` or `cloud`
- OCI region
- endpoint type: `private-ip` or `public-ip`
- server command
- runner command
- OS limit snapshot
- start/end timestamps

Do not include:

- user OCID
- tenancy OCID
- private key paths
- public key content
- public IPs unless intentionally needed for reproducibility

## 7. OS Readiness Checks

Run on the server VM and local runner before validation.

```bash
uname -a
dotnet --info
ulimit -n
cat /proc/sys/net/ipv4/ip_local_port_range
sysctl net.core.somaxconn
sysctl net.ipv4.tcp_max_syn_backlog
free -h
nproc
```

Runner-specific:

```bash
ss -s
cat /proc/net/sockstat
```

Server-specific:

```bash
ss -ltnp
```

These outputs go to `os-readiness.txt`.

## 8. Evaluation Design

### 8.1 Compare Against Current Same-Machine Baseline

Current same-machine baseline:

- `artifacts/load-validation/s5-session-rtt-validation/summary.md`
- Peak sessions: `10,000 / 10,000`
- Final disconnects: `0`
- Max TPS: `9,371.08`
- RTT P95: `19,210.39ms`
- RTT P99: `24,863.90ms`
- Max pending request count: `36,695`
- Max pending send requests: `1,095`
- `send|IOException|NoBufferSpaceAvailable`: `1,639`
- `receive|IOException|TimedOut`: `184`

### 8.2 Primary Cloud Metrics

| Category | Metrics |
|----------|---------|
| Capacity | peak sessions, final disconnects, socket error rate |
| Throughput | max TPS, received/sent packets, per-stage result |
| RTT | RTT P50/P95/P99, per-session RTT p95-of-session-P95 |
| Client pressure | pending requests, pacing wait count/avg, scheduler drift |
| Server pressure | pending send requests, send backpressure events, max send buffer bytes |
| Socket errors | phase/class counts, NoBuffer, TimedOut, Reset |
| Environment | CPU, memory, socket counts, fd limit, port range |

### 8.3 Interpretation Rules

| Observation | Likely Meaning | Next Action |
|-------------|----------------|-------------|
| Local runner CPU/socket saturated, server not saturated | local runner bottleneck | reduce target or add cloud runner controlled baseline |
| Server pending send/backpressure grows | server send path bottleneck | continue send/drain optimization |
| RTT reflects stable public path | external-client baseline is usable | keep cloud-server/local-runner baseline |
| RTT worsens but server/local runner look healthy | public network/path effect | record as external path cost; optionally add cloud runner baseline |
| NoBuffer shifts to runner errors | runner socket pressure | runner OS/runtime tuning |
| 10K fails at connection ramp | OS/security/network limit | inspect fd, port range, backlog, NSG/local network |

## 9. Implementation Order

1. Create design document.
2. Add or update cloud validation runbook under `docs/`.
3. Add optional manifest template or checklist for cloud run metadata.
4. Verify OCI resource discovery commands locally.
5. Provision or prepare the server VM manually or by documented CLI sequence.
6. Run smoke validation from the local runner over the restricted public endpoint.
7. Run staged/focused validation.
8. Copy artifacts back and update benchmark docs with selected summary.
9. Analyze result and decide whether next feature returns to throughput decomposition or runner scaling.

## 10. Test Plan

### 10.1 Local Checks

- `jq empty docs/.pdca-status.json`
- `git diff --check`
- `dotnet build FastPortCharp.sln -c Release`
- `dotnet test FastPortCharp.sln --no-build`

### 10.2 Azure CLI Checks

- `az --version`
- `az account show`
- `az group list --query "length(@)" -o tsv`
- `az reservations reservation-order list -o json`
- `az reservations reservation list --reservation-order-id <order id> -o json`
- `az vm list -d --query "[?location=='koreacentral']" -o json`
- `scripts/cloud/azure-discover.sh`

### 10.3 OCI CLI Checks

- `oci iam region-subscription list`
- `oci iam availability-domain list --compartment-id "$OCI_COMPARTMENT_OCID"`
- `oci compute shape list --compartment-id "$OCI_COMPARTMENT_OCID" --availability-domain "$OCI_AVAILABILITY_DOMAIN"`

### 10.4 Runtime Checks

- server process starts and listens on `0.0.0.0:6628`
- local runner can connect to server public IP/DNS on `6628`
- smoke validation passes
- server telemetry file grows during the run
- focused validation produces summary and metrics artifacts
- copied/merged server metrics match runner timeline within tolerance

## 11. Security Notes

- Do not commit `~/.oci/config` or any OCI keys.
- Do not write OCIDs into docs unless they are intentionally public placeholders.
- Restrict SSH to local public IP.
- Restrict port `6628` to the local public IP for the default run.
- Prefer public IP target only for external-client baseline runs; use a cloud runner later for controlled private-IP comparison.
- Delete or stop cloud instances after experiments if they are not confirmed Always Free eligible.

## 12. Design Decisions

| Decision | Choice | Reason |
|----------|--------|--------|
| First cloud topology | 1 server VM + local runner | Matches the external-client access path and uses the existing reservation only for server |
| Current target provider | Azure | OCI A1 capacity is blocked; Azure CLI account/resource access is verified |
| First target region | `koreacentral` | Active `Standard_B2s` reservation exists in this region |
| First server shape | `Standard_B2s` | Existing reserved instance, quantity `1`, currently unused |
| First runner mode | local Mac | Avoids extra cloud cost and reflects real external access |
| First endpoint | server public IP/DNS restricted to local IP | Default benchmark is external-client path |
| First server project | `FastPortTestSmokeServer` | Existing load-test server with telemetry export |
| First runner project | `FastPortTestLoadValidation` | Existing stage orchestration and summary output |
| Deployment automation | local scripts, Azure CLI, SSH | Safer for a public repo than GitHub Actions cloud deployment |
| First run strategy | smoke then focused 10K | Avoid interpreting 10K before connectivity and telemetry are proven |
| Secrets in repo | none | Prevent accidental credential leakage |

## 13. References

- Plan: `docs/01-plan/features/cloud-server-runner-split-load-validation.plan.md`
- `HANDOFF.md`
- `docs/loadrunner-os-limits.md`
- `docs/load-validation-benchmark-results.md`
- `FastPortTestSmokeServer/`
- `FastPortTestLoadValidation/`
- `FastPortTestLoadRunner/`
- Oracle Always Free Resources: https://docs.oracle.com/en-us/iaas/Content/FreeTier/freetier_topic-Always_Free_Resources.htm
- Oracle Ampere A1 pricing/free monthly units: https://www.oracle.com/cloud/compute/arm/pricing/
- Azure free services: https://azure.microsoft.com/pricing/free-services
- Azure B-series burstable VM sizes: https://learn.microsoft.com/azure/virtual-machines/sizes/b-series-burstable

## 14. Next Phase

Recommended next command:

```text
$pdca do cloud-server-runner-split-load-validation
```
