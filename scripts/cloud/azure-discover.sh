#!/usr/bin/env bash
set -euo pipefail

fail() {
  printf 'ERROR: %s\n' "$*" >&2
  exit 1
}

info() {
  printf '%s\n' "$*"
}

warn() {
  printf 'WARN: %s\n' "$*" >&2
}

require_command() {
  command -v "$1" >/dev/null 2>&1 || fail "required command not found: $1"
}

require_command az

FASTPORT_AZURE_LOCATION="${FASTPORT_AZURE_LOCATION:-koreacentral}"
FASTPORT_AZURE_SERVER_SIZE="${FASTPORT_AZURE_SERVER_SIZE:-Standard_B2s}"
FASTPORT_RUNNER_MODE="${FASTPORT_RUNNER_MODE:-local}"
FASTPORT_AZURE_RUNNER_SIZE="${FASTPORT_AZURE_RUNNER_SIZE:-}"

info "FastPort Azure discovery config:"
info "  location: $FASTPORT_AZURE_LOCATION"
info "  server size candidate: $FASTPORT_AZURE_SERVER_SIZE"
info "  runner mode: $FASTPORT_RUNNER_MODE"
if [[ "$FASTPORT_RUNNER_MODE" == "cloud" ]]; then
  info "  runner size candidate: ${FASTPORT_AZURE_RUNNER_SIZE:-unset}"
fi
info ""

info "Azure account summary:"
az account show \
  --query '{environmentName:environmentName,subscriptionName:name,state:state,isDefault:isDefault}' \
  -o table

info ""
info "Accessible resource group count:"
az group list --query 'length(@)' -o tsv

info ""
info "Active reservation orders:"
az reservations reservation-order list \
  --query "[?provisioningState=='Succeeded'].{displayName:displayName,expiryDate:expiryDate,originalQuantity:originalQuantity,term:term}" \
  -o table

info ""
info "Existing VMs in $FASTPORT_AZURE_LOCATION:"
az vm list -d \
  --query "[?location=='$FASTPORT_AZURE_LOCATION'].{name:name,resourceGroup:resourceGroup,powerState:powerState,size:hardwareProfile.vmSize}" \
  -o table

info ""
info "Candidate locations:"
az account list-locations \
  --query "[?name=='$FASTPORT_AZURE_LOCATION' || name=='koreacentral' || name=='koreasouth' || name=='eastasia' || name=='southeastasia' || name=='centralus' || name=='westus3'].{name:name,displayName:displayName,regionalDisplayName:regionalDisplayName}" \
  -o table

info ""
info "VM SKU availability in $FASTPORT_AZURE_LOCATION:"
az vm list-skus \
  --location "$FASTPORT_AZURE_LOCATION" \
  --resource-type virtualMachines \
  --all \
  --query "[?name=='Standard_B1s' || name=='Standard_B2s' || name=='Standard_B2pts_v2' || name=='Standard_B2ats_v2' || name=='$FASTPORT_AZURE_SERVER_SIZE' || name=='$FASTPORT_AZURE_RUNNER_SIZE'].{name:name,tier:tier,locations:join(',', locations),restrictionCount:length(restrictions)}" \
  -o table

warn "This discovery script is read-only. It does not create Azure resources."
warn "The default topology uses the active Standard_B2s candidate for the server and the local Mac as runner."
