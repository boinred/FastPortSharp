#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=free-tier-guard.sh
source "$SCRIPT_DIR/free-tier-guard.sh"

require_command oci

resolve_compartment_id() {
  if [[ -n "${OCI_COMPARTMENT_OCID:-}" ]]; then
    printf '%s\n' "$OCI_COMPARTMENT_OCID"
    return
  fi

  local config_file="${OCI_CONFIG_FILE:-$HOME/.oci/config}"
  local profile="${OCI_CLI_PROFILE:-DEFAULT}"

  if [[ ! -f "$config_file" ]]; then
    return
  fi

  awk -v profile="$profile" -F= '
    /^\[/ {
      current = substr($0, 2, length($0) - 2)
      next
    }
    current == profile && $1 ~ /^[[:space:]]*tenancy[[:space:]]*$/ {
      gsub(/[[:space:]]/, "", $2)
      print $2
      exit
    }
  ' "$config_file"
}

print_free_tier_config

info ""
info "OCI region subscriptions:"
oci iam region-subscription list \
  --query 'data[].{region:"region-name",status:status,home:"is-home-region"}' \
  --output table

COMPARTMENT_ID="$(resolve_compartment_id)"

if [[ -z "$COMPARTMENT_ID" ]]; then
  warn "No compartment ID found; skipping availability-domain and shape discovery."
  warn "Set OCI_COMPARTMENT_OCID locally or configure tenancy in ~/.oci/config. Do not commit OCIDs to this repository."
  exit 0
fi

info ""
info "Availability domains:"
oci iam availability-domain list \
  --compartment-id "$COMPARTMENT_ID" \
  --query 'data[].name' \
  --output table

if [[ -z "${OCI_AVAILABILITY_DOMAIN:-}" ]]; then
  warn "OCI_AVAILABILITY_DOMAIN is not set; skipping shape discovery."
  warn "Pick an availability domain from the list above and export OCI_AVAILABILITY_DOMAIN."
  exit 0
fi

info ""
info "Shape discovery for $FASTPORT_OCI_SHAPE in $OCI_AVAILABILITY_DOMAIN:"
oci compute shape list \
  --compartment-id "$COMPARTMENT_ID" \
  --availability-domain "$OCI_AVAILABILITY_DOMAIN" \
  --shape "$FASTPORT_OCI_SHAPE" \
  --limit 10 \
  --query 'data[].{shape:shape,processor:"processor-description",ocpus:ocpus,memory:memory}' \
  --output table

info ""
info "Recent ARM-compatible images for $FASTPORT_OCI_SHAPE:"
oci compute image list \
  --compartment-id "$COMPARTMENT_ID" \
  --shape "$FASTPORT_OCI_SHAPE" \
  --sort-by TIMECREATED \
  --sort-order DESC \
  --limit 10 \
  --query 'data[].{name:"display-name",os:"operating-system",version:"operating-system-version",created:"time-created"}' \
  --output table
