#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=free-tier-guard.sh
source "$SCRIPT_DIR/free-tier-guard.sh"

require_command oci
require_command jq

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

sum_json_field() {
  local file="$1"
  local field="$2"
  jq --arg field "$field" '[.[][$field] // 0] | add // 0' "$file"
}

COMPARTMENT_ID="$(resolve_compartment_id)"
[[ -n "$COMPARTMENT_ID" ]] || fail "No compartment ID found. Set OCI_COMPARTMENT_OCID or configure tenancy in ~/.oci/config."

TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT

COMPARTMENTS_JSON="$TMP_DIR/compartments.json"
ADS_JSON="$TMP_DIR/ads.json"
INSTANCES_JSON="$TMP_DIR/instances.json"
BOOT_VOLUMES_JSON="$TMP_DIR/boot-volumes.json"
VCNS_JSON="$TMP_DIR/vcns.json"

print_free_tier_config

info ""
info "Reading accessible compartments..."
oci iam compartment list \
  --include-root \
  --compartment-id-in-subtree true \
  --access-level ACCESSIBLE \
  --all \
  --output json |
  jq '[.data[] | select(."lifecycle-state" == "ACTIVE") | {id, name}]' > "$COMPARTMENTS_JSON"

info "  compartments: $(jq 'length' "$COMPARTMENTS_JSON")"

info ""
info "Reading availability domains..."
oci iam availability-domain list \
  --compartment-id "$COMPARTMENT_ID" \
  --output json |
  jq '[.data[] | {name}]' > "$ADS_JSON"

info "  availability domains: $(jq 'length' "$ADS_JSON")"

printf '[]\n' > "$INSTANCES_JSON"
printf '[]\n' > "$BOOT_VOLUMES_JSON"
printf '[]\n' > "$VCNS_JSON"

while IFS=$'\t' read -r compartment_id compartment_name; do
  [[ -n "$compartment_id" ]] || continue

  instances_tmp="$TMP_DIR/instances-one.json"
  oci compute instance list \
    --compartment-id "$compartment_id" \
    --all \
    --output json |
    jq --arg compartment "$compartment_name" '
      [.data[]
        | select(."lifecycle-state" != "TERMINATED")
        | {
            compartment: $compartment,
            name: ."display-name",
            state: ."lifecycle-state",
            shape: .shape,
            ad: ."availability-domain",
            ocpus: (."shape-config".ocpus // 0),
            memory: (."shape-config"."memory-in-gbs" // 0)
          }
      ]' > "$instances_tmp"
  jq -s '.[0] + .[1]' "$INSTANCES_JSON" "$instances_tmp" > "$INSTANCES_JSON.next"
  mv "$INSTANCES_JSON.next" "$INSTANCES_JSON"

  vcns_tmp="$TMP_DIR/vcns-one.json"
  oci network vcn list \
    --compartment-id "$compartment_id" \
    --all \
    --output json |
    jq --arg compartment "$compartment_name" '
      [.data[]
        | select(."lifecycle-state" == "AVAILABLE")
        | {
            compartment: $compartment,
            name: ."display-name",
            cidr: ."cidr-block",
            state: ."lifecycle-state"
          }
      ]' > "$vcns_tmp"
  jq -s '.[0] + .[1]' "$VCNS_JSON" "$vcns_tmp" > "$VCNS_JSON.next"
  mv "$VCNS_JSON.next" "$VCNS_JSON"

  while IFS=$'\t' read -r ad_name; do
    [[ -n "$ad_name" ]] || continue

    boot_tmp="$TMP_DIR/boot-one.json"
    oci bv boot-volume list \
      --compartment-id "$compartment_id" \
      --availability-domain "$ad_name" \
      --all \
      --output json |
      jq --arg compartment "$compartment_name" --arg ad "$ad_name" '
        [.data[]
          | select(."lifecycle-state" != "TERMINATED" and ."lifecycle-state" != "TERMINATING")
          | {
              compartment: $compartment,
              name: ."display-name",
              ad: $ad,
              state: ."lifecycle-state",
              size: (."size-in-gbs" // 0)
            }
        ]' > "$boot_tmp"
    jq -s '.[0] + .[1]' "$BOOT_VOLUMES_JSON" "$boot_tmp" > "$BOOT_VOLUMES_JSON.next"
    mv "$BOOT_VOLUMES_JSON.next" "$BOOT_VOLUMES_JSON"
  done < <(jq -r '.[].name' "$ADS_JSON")
done < <(jq -r '.[] | [.id, .name] | @tsv' "$COMPARTMENTS_JSON")

info ""
info "Active A1 instances:"
jq -r --arg shape "$FASTPORT_OCI_SHAPE" '
  [.[] | select(.shape == $shape)]
  | if length == 0 then
      "  none"
    else
      (["  name", "state", "ocpus", "memory_gb", "compartment", "ad"] | @tsv),
      (.[] | ["  " + .name, .state, (.ocpus|tostring), (.memory|tostring), .compartment, .ad] | @tsv)
    end
' "$INSTANCES_JSON"

EXISTING_A1_JSON="$TMP_DIR/existing-a1.json"
jq --arg shape "$FASTPORT_OCI_SHAPE" '[.[] | select(.shape == $shape)]' "$INSTANCES_JSON" > "$EXISTING_A1_JSON"

EXISTING_A1_OCPUS="$(sum_json_field "$EXISTING_A1_JSON" ocpus)"
EXISTING_A1_MEMORY="$(sum_json_field "$EXISTING_A1_JSON" memory)"
PLANNED_OCPUS=$((FASTPORT_SERVER_OCPUS + FASTPORT_RUNNER_OCPUS))
PLANNED_MEMORY=$((FASTPORT_SERVER_MEMORY_GB + FASTPORT_RUNNER_MEMORY_GB))

info ""
info "A1 capacity check:"
info "  existing A1: ${EXISTING_A1_OCPUS} OCPU / ${EXISTING_A1_MEMORY}GB"
info "  planned add: ${PLANNED_OCPUS} OCPU / ${PLANNED_MEMORY}GB"
info "  after plan: $(jq -n --argjson a "$EXISTING_A1_OCPUS" --argjson b "$PLANNED_OCPUS" '$a + $b') OCPU / $(jq -n --argjson a "$EXISTING_A1_MEMORY" --argjson b "$PLANNED_MEMORY" '$a + $b')GB"

AFTER_OCPUS="$(jq -n --argjson a "$EXISTING_A1_OCPUS" --argjson b "$PLANNED_OCPUS" '$a + $b')"
AFTER_MEMORY="$(jq -n --argjson a "$EXISTING_A1_MEMORY" --argjson b "$PLANNED_MEMORY" '$a + $b')"

(( ${AFTER_OCPUS%.*} <= FASTPORT_FREE_TIER_MAX_OCPUS )) ||
  fail "planned A1 OCPU total would exceed free-tier max: $AFTER_OCPUS > $FASTPORT_FREE_TIER_MAX_OCPUS"

(( ${AFTER_MEMORY%.*} <= FASTPORT_FREE_TIER_MAX_MEMORY_GB )) ||
  fail "planned A1 memory total would exceed free-tier max: $AFTER_MEMORY > $FASTPORT_FREE_TIER_MAX_MEMORY_GB"

BOOT_TOTAL="$(sum_json_field "$BOOT_VOLUMES_JSON" size)"
PLANNED_BOOT=$((FASTPORT_SERVER_BOOT_VOLUME_GB + FASTPORT_RUNNER_BOOT_VOLUME_GB))
AFTER_BOOT="$(jq -n --argjson a "$BOOT_TOTAL" --argjson b "$PLANNED_BOOT" '$a + $b')"

info ""
info "Boot volume check:"
info "  existing boot volumes: ${BOOT_TOTAL}GB"
info "  planned add: ${PLANNED_BOOT}GB"
info "  after plan: ${AFTER_BOOT}GB"

(( ${AFTER_BOOT%.*} <= FASTPORT_FREE_TIER_MAX_BOOT_VOLUME_GB )) ||
  fail "planned boot volume total would exceed planned free-tier max: $AFTER_BOOT > $FASTPORT_FREE_TIER_MAX_BOOT_VOLUME_GB"

info ""
info "Active VCNs:"
jq -r '
  if length == 0 then
    "  none"
  else
    (["  name", "cidr", "compartment"] | @tsv),
    (.[] | ["  " + .name, .cidr, .compartment] | @tsv)
  end
' "$VCNS_JSON"

VCN_COUNT="$(jq 'length' "$VCNS_JSON")"
if (( VCN_COUNT >= 2 )); then
  warn "VCN count is already $VCN_COUNT. Reuse an existing VCN or delete unused VCNs; do not create a third VCN in a free-tier tenancy."
fi

info ""
info "Preflight passed for the planned free-tier envelope."
