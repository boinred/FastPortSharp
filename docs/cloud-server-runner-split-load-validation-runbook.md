# Cloud Server/Runner Split Load Validation Runbook

> Feature: `cloud-server-runner-split-load-validation`
> Date: 2026-05-01
> Status: Draft

## Rule

Use Oracle Cloud Free Tier resources only.

This runbook refuses the first-pass target if it would exceed the planned free-tier envelope:

- Region: `us-chicago-1`
- Shape: `VM.Standard.A1.Flex`
- Total compute: max `4 OCPU / 24GB RAM`
- Suggested split:
  - server: `2 OCPU / 12GB RAM`
  - runner: `2 OCPU / 12GB RAM`
- Planned boot volume total: max `200GB`

Do not put OCI OCIDs, private keys, public keys, IP addresses, or credentials into committed files.

GitHub Actions must not deploy to OCI for this public repository. Use local scripts plus OCI CLI and SSH.

## Local Setup

Required local tools:

```bash
oci --version
ssh -V
jq --version
```

Validate OCI authentication:

```bash
oci iam region-subscription list
```

Expected region:

```text
us-chicago-1
```

Set local-only variables:

```bash
export OCI_COMPARTMENT_OCID="<compartment ocid>"
export OCI_AVAILABILITY_DOMAIN="<availability domain>"
export FASTPORT_OCI_REGION="us-chicago-1"
export FASTPORT_OCI_SHAPE="VM.Standard.A1.Flex"
export FASTPORT_SERVER_OCPUS=2
export FASTPORT_SERVER_MEMORY_GB=12
export FASTPORT_RUNNER_OCPUS=2
export FASTPORT_RUNNER_MEMORY_GB=12
```

Run read-only OCI discovery:

```bash
scripts/cloud/oci-discover.sh
```

This script does not create resources.

## OCI Resource Shape

Create or verify these resources manually in OCI Console or with separately reviewed CLI commands:

| Role | Name | Shape | OCPU | Memory |
|------|------|-------|-----:|-------:|
| server | `fastport-server-a1` | `VM.Standard.A1.Flex` | 2 | 12GB |
| runner | `fastport-runner-a1` | `VM.Standard.A1.Flex` | 2 | 12GB |

Network target:

- Same region: `us-chicago-1`
- Same VCN/subnet for first pass
- Runner connects to server private IP on TCP `6628`
- SSH TCP `22` is allowed only from the local admin IP
- TCP `6628` is not opened to the public internet

## VM Preparation

Run on each VM:

Ubuntu:

```bash
sudo apt-get update
sudo apt-get install -y git jq tmux
```

Oracle Linux:

```bash
sudo dnf install -y git jq tmux
```

Install .NET SDK for the selected Linux ARM64 distribution using Microsoft's official package instructions for that distro.

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

Defaults:

- listen: `0.0.0.0:6628`
- metrics: `artifacts/load-validation/cloud-server-runner-split/server/server.metrics.jsonl`
- OS readiness: `artifacts/load-validation/cloud-server-runner-split/server/os-readiness.txt`

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

Run focused 10K only after smoke passes:

```bash
scripts/cloud/runner-10k.sh
```

Defaults:

- smoke output: `artifacts/load-validation/cloud-server-runner-split/smoke`
- 10K output: `artifacts/load-validation/cloud-server-runner-split/s5-random-10k`
- runner readiness: `artifacts/load-validation/cloud-server-runner-split/runner/os-readiness.txt`

## Server Metrics Merge

For first pass, it is acceptable to run without server metrics merge if the server file is not yet copied to runner before evaluation.

Preferred follow-up:

1. Stop server after the run.
2. Copy `server.metrics.jsonl` from server VM.
3. Copy runner artifacts from runner VM.
4. Keep the artifacts under local `artifacts/load-validation/cloud-server-runner-split/`.
5. Update `docs/load-validation-benchmark-results.md` only with selected summary values.

## Result Interpretation

Use this order:

1. Smoke connectivity passed.
2. 10K reached target sessions.
3. Runner CPU/socket pressure is not saturated before server pressure.
4. Server send backlog and backpressure are compared with the same-machine baseline.
5. RTT P95/P99 is interpreted with endpoint type noted as `private-ip`.

If runner CPU or socket pressure is saturated first, do not treat the result as a server benchmark. Split the runner or reduce target sessions for the next pass.

## Cleanup

After validation:

- Stop server process.
- Stop or delete OCI instances if they are not confirmed Always Free eligible.
- Do not leave port `6628` public.
- Do not commit generated artifacts.

## References

- `docs/01-plan/features/cloud-server-runner-split-load-validation.plan.md`
- `docs/02-design/features/cloud-server-runner-split-load-validation.design.md`
- `docs/loadrunner-os-limits.md`
- Oracle Always Free Resources: https://docs.oracle.com/en-us/iaas/Content/FreeTier/freetier_topic-Always_Free_Resources.htm
- Oracle Ampere A1 pricing/free monthly units: https://www.oracle.com/cloud/compute/arm/pricing/
