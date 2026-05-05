# cloud-server-runner-split-load-validation - Plan Document

> Version: 1.0.0 | Date: 2026-05-01 | Status: Completed
> Level: Starter

---

## 1. Overview

### 1.1 Purpose

`cloud-server-runner-split-load-validation`은 로컬 단일 머신 10K 검증의 환경 노이즈를 줄이기 위해, cloud VM에서 server와 runner를 분리해 Release 기준 부하 검증 baseline을 만드는 feature다.

목표는 즉시 성능 최적화를 넣는 것이 아니라, 같은 Mac 안에서 server/runner를 함께 실행할 때 섞이는 영향을 제거하고 다음 성능 판단을 더 신뢰할 수 있게 만드는 것이다.

### 1.2 Background

최근 `loadrunner-10k-session-rtt-validation` 결과는 focused 10K validation을 통과했지만 다음 문제가 남아 있다.

- RTT P95: `19,210.39ms`
- RTT P99: `24,863.90ms`
- Max TPS: `9,371.08`
- `send|IOException|NoBufferSpaceAvailable`: `1,639`
- `receive|IOException|TimedOut`: `184`
- Server send backpressure events: `1,583`

현재까지의 same-machine 10K 결과는 비교에는 유용하지만, server 처리량과 runner/OS/socket 한계가 섞일 수 있다. 특히 로컬 Mac에서는 다른 앱, loopback path, file descriptor, ephemeral port, scheduler, thermal state가 결과에 영향을 줄 수 있다.

OCI CLI는 로컬에서 연결 확인이 완료되었지만, Always Free A1 capacity 부족으로 VM 생성이 막혔다. 2026-05-05 기준으로 cloud provider는 Azure로 전환한다.

- Azure CLI: installed, `az account show` succeeded
- Azure environment: `AzureCloud`
- Subscription state: `Enabled`
- Resource group list access: succeeded
- Active reservation: `Standard_B2s`, `koreacentral`, quantity `1`, utilization `0%`

repo에는 OCI/Azure user IDs, tenant/subscription IDs, private keys, public key contents, public IPs, or generated artifacts를 기록하지 않는다.

## 2. Goals

### 2.1 Primary Goals

- [ ] Azure에서 server VM과 runner VM을 분리한 validation topology를 정의한다.
- [ ] `FastPortTestSmokeServer`를 cloud server VM에서 Release로 실행하는 절차를 정리한다.
- [ ] `FastPortTestLoadValidation` 또는 `FastPortTestLoadRunner`를 runner VM에서 Release로 실행하는 절차를 정리한다.
- [ ] server telemetry JSONL과 client/runner metrics JSONL을 수집하고 병합하는 artifact contract를 정한다.
- [ ] same-machine 10K 결과와 cloud split-run 결과를 비교할 기준 지표를 정한다.
- [ ] runner 병목 여부를 판정할 수 있는 최소 관측 항목을 정한다.

### 2.2 Non-Goals

- 이번 feature에서 성능 최적화 코드를 바로 변경하지 않는다.
- 이번 feature에서 MAUI dashboard를 만들지 않는다.
- 이번 feature에서 game server template 구조화를 진행하지 않는다.
- 공개 GitHub repository에서 GitHub Actions로 cloud 배포 자동화를 구성하지 않는다.
- 이번 feature에서 Terraform/Ansible 같은 full infrastructure automation을 필수로 만들지 않는다.
- OCI/Azure credential, IDs, private key, generated artifacts를 repo에 커밋하지 않는다.
- Azure reserved instance가 compute 할인/예약 적용을 의미하더라도 전체 VM 비용 무료를 보장한다고 취급하지 않는다.
- 이번 feature에서 Azure VM/resource group/network를 자동 생성하지 않는다. 생성 전 사용자가 직접 생성하거나 별도 승인된 CLI 절차를 거친다.

## 3. Scope

### 3.1 In Scope

- Cloud validation topology 설계:
  - Server VM 1대
  - Runner VM 1대
  - 같은 region, 가능하면 같은 VCN/subnet 또는 low-latency path
- Azure 우선 검토:
  - `koreacentral`
  - reserved `Standard_B2s` 1대를 server 후보로 사용
  - runner는 별도 SKU/비용 확인 후 선택
  - 첫 실행은 full 10K가 아니라 smoke/lower-stage부터 시작
- Server 실행 절차:
  - Release build
  - `FastPortTestSmokeServer`
  - server telemetry export enabled
  - fixed logging level
- Runner 실행 절차:
  - Release build
  - `FastPortTestLoadValidation`
  - server private IP 또는 stable endpoint target
  - staged/focused profile selection
- OS/runtime readiness checklist:
  - `ulimit -n`
  - ephemeral port range
  - TCP backlog settings
  - .NET version
  - CPU/memory/network observation
- Artifact layout:
  - server metrics JSONL
  - client metrics JSONL
  - combined metrics JSONL
  - summary JSON/Markdown
  - manifest with commit SHA, instance shape, region, build configuration, command line
- Comparison plan against latest same-machine baseline:
  - `artifacts/load-validation/s5-session-rtt-validation/summary.md`

### 3.2 Out of Scope

- Multi-runner orchestration beyond noting when one runner becomes the bottleneck.
- Production deployment hardening.
- GitHub Actions based cloud deployment automation.
- Public internet latency benchmarking across regions.
- Cloud cost optimization beyond avoiding non-free resources for the first pass.
- Secrets management beyond local OCI CLI/API key hygiene.

## 4. Success Criteria

- [ ] Plan identifies the exact server/runner split topology and why it reduces local test noise.
- [ ] Design can produce a repeatable command sequence for preparing two Azure VMs.
- [ ] Design can produce a repeatable command sequence for Release build and server/runner execution.
- [ ] Validation artifacts include enough metadata to compare cloud split-run with same-machine runs.
- [ ] Result interpretation can distinguish at least:
  - server bottleneck
  - runner bottleneck
  - network/path bottleneck
  - cloud instance capacity/cost limit
- [ ] No secrets or local cloud credentials are written into committed repo files.
- [ ] Next decision is clear:
  - keep cloud split-run as baseline,
  - add more runner VMs,
  - tune server/runtime,
  - or continue local-only diagnostics.

## 5. Schedule

| Phase | Target Date | Status |
|-------|------------|--------|
| Plan | 2026-05-01 | Completed |
| Design | 2026-05-01 | Completed |
| Implementation | TBD | Active |
| Check | TBD | Pending |
| Report | TBD | Pending |

## 6. Risks & Mitigations

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| Azure reserved instance covers only one `Standard_B2s` VM | Runner cost/capacity remains unresolved | Medium | Use reserved VM as server first; select runner SKU only after cost/size review |
| Pay-as-you-go subscription creates unintended cost | Cloud bill impact | Medium | Keep scripts read-only until user creates resources or explicitly approves provisioning |
| Runner VM becomes bottleneck first | Server result still ambiguous | High | Track runner CPU/memory/socket errors; split runner later if needed |
| Public network path distorts RTT | RTT not comparable to local run | Medium | Prefer same-region/private IP path where possible; record endpoint type |
| Instance shape too weak for 10K | False low benchmark | Medium | Treat first cloud run as baseline, not production capacity claim |
| Security list/NSG/firewall misconfiguration | Runner cannot connect | Medium | Include explicit inbound port and private path checks in design |
| Secrets accidentally committed | Security risk | Low | Do not write tenant/subscription IDs, IPs, keys, or credentials to docs |
| OS limits differ from local docs | Validation fails before app is stressed | Medium | Reuse and extend `docs/loadrunner-os-limits.md` for Linux VM checks |

## 7. References

- `HANDOFF.md`
- `docs/loadrunner-os-limits.md`
- `docs/load-validation-benchmark-results.md`
- `docs/archive/2026-04/loadrunner-10k-session-rtt-validation/loadrunner-10k-session-rtt-validation.report.md`
- `docs/archive/2026-04/loadrunner-10k-session-rtt-validation/loadrunner-10k-session-rtt-validation.analysis.md`
- `FastPortTestSmokeServer/`
- `FastPortTestLoadValidation/`
- `FastPortTestLoadRunner/`

## 8. Open Questions

- Should the first cloud run target full 10K immediately, or start with 1K/3K/5K/10K staged validation?
- Should the server be exposed through public IP for simplicity, or private IP through same VCN runner for cleaner latency?
- Which server port should be standardized for cloud validation?
- Should server telemetry export path be local disk on server VM and copied back, or streamed/collected by runner?
- What runner CPU/network threshold should define "runner bottleneck"?

## 9. Next Phase

Recommended next command:

```text
$pdca do cloud-server-runner-split-load-validation
```
