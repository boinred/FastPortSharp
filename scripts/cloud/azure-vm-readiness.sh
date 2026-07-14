#!/usr/bin/env bash
set -euo pipefail

fail() {
  printf 'ERROR: %s\n' "$*" >&2
  exit 1
}

warn() {
  printf 'WARN: %s\n' "$*" >&2
}

info() {
  printf '%s\n' "$*"
}

require_command() {
  command -v "$1" >/dev/null 2>&1 || fail "required command not found: $1"
}

require_command az
require_command jq

FASTPORT_AZURE_LOCATION="${FASTPORT_AZURE_LOCATION:-koreacentral}"
FASTPORT_AZURE_RESOURCE_GROUP="${FASTPORT_AZURE_RESOURCE_GROUP:-fastport-load-rg}"
FASTPORT_AZURE_SERVER_VM="${FASTPORT_AZURE_SERVER_VM:-fastport-server-vm}"
FASTPORT_AZURE_RUNNER_VM="${FASTPORT_AZURE_RUNNER_VM:-fastport-runner-vm}"
FASTPORT_AZURE_SERVER_SIZE="${FASTPORT_AZURE_SERVER_SIZE:-Standard_B2s}"
FASTPORT_AZURE_RUNNER_SIZE="${FASTPORT_AZURE_RUNNER_SIZE:-}"
FASTPORT_RUNNER_MODE="${FASTPORT_RUNNER_MODE:-local}"
FASTPORT_SERVER_PORT="${FASTPORT_SERVER_PORT:-6628}"

status=0

check_vm() {
  local role="$1"
  local name="$2"
  local expected_size="$3"

  info ""
  info "## $role VM"
  info "name: $name"

  local vm_json
  if ! vm_json="$(az vm show \
    --resource-group "$FASTPORT_AZURE_RESOURCE_GROUP" \
    --name "$name" \
    --show-details \
    -o json 2>/dev/null)"; then
    warn "$role VM was not found in resource group $FASTPORT_AZURE_RESOURCE_GROUP"
    status=1
    return
  fi

  local location
  local size
  local power_state
  local private_ip_state
  local public_ip_state
  location="$(jq -r '.location // "unknown"' <<<"$vm_json")"
  size="$(jq -r '.hardwareProfile.vmSize // "unknown"' <<<"$vm_json")"
  power_state="$(jq -r '.powerState // "unknown"' <<<"$vm_json")"
  private_ip_state="$(jq -r 'if (.privateIps // "") == "" then "unset" else "set-redacted" end' <<<"$vm_json")"
  public_ip_state="$(jq -r 'if (.publicIps // "") == "" then "unset" else "set-redacted" end' <<<"$vm_json")"

  info "location: $location"
  info "size: $size"
  info "power state: $power_state"
  info "private IP: $private_ip_state"
  info "public IP: $public_ip_state"

  if [[ "$location" != "$FASTPORT_AZURE_LOCATION" ]]; then
    warn "$role VM location mismatch: expected $FASTPORT_AZURE_LOCATION"
    status=1
  fi

  if [[ -n "$expected_size" && "$size" != "$expected_size" ]]; then
    warn "$role VM size mismatch: expected $expected_size"
    status=1
  fi

  if [[ "$power_state" != "VM running" ]]; then
    warn "$role VM is not running"
    status=1
  fi

  if [[ "$private_ip_state" != "set-redacted" ]]; then
    warn "$role VM private IP is missing"
    status=1
  fi
}

info "FastPort Azure VM readiness"
info "resource group: $FASTPORT_AZURE_RESOURCE_GROUP"
info "location: $FASTPORT_AZURE_LOCATION"
info "runner mode: $FASTPORT_RUNNER_MODE"
info "server port: $FASTPORT_SERVER_PORT"
info "IP values are intentionally redacted."

check_vm "server" "$FASTPORT_AZURE_SERVER_VM" "$FASTPORT_AZURE_SERVER_SIZE"
if [[ "$FASTPORT_RUNNER_MODE" == "cloud" ]]; then
  check_vm "runner" "$FASTPORT_AZURE_RUNNER_VM" "$FASTPORT_AZURE_RUNNER_SIZE"
else
  info ""
  info "## runner VM"
  info "skipped: FASTPORT_RUNNER_MODE=$FASTPORT_RUNNER_MODE"
fi

info ""
info "NSG summary in resource group:"
az network nsg list \
  --resource-group "$FASTPORT_AZURE_RESOURCE_GROUP" \
  --query '[].{name:name,ruleCount:length(securityRules)}' \
  -o table || warn "Unable to list NSGs for $FASTPORT_AZURE_RESOURCE_GROUP"

if (( status != 0 )); then
  fail "Azure VM readiness checks failed"
fi

info ""
info "Azure VM metadata readiness passed. Next: verify server SSH, start the server, then run local runner connectivity."
