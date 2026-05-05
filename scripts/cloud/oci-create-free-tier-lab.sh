#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=free-tier-guard.sh
source "$SCRIPT_DIR/free-tier-guard.sh"

require_command oci
require_command jq

VCN_NAME="${FASTPORT_VCN_NAME:-fastport-load-vcn}"
SUBNET_NAME="${FASTPORT_SUBNET_NAME:-fastport-load-public-subnet}"
IGW_NAME="${FASTPORT_IGW_NAME:-fastport-load-igw}"
ROUTE_TABLE_NAME="${FASTPORT_ROUTE_TABLE_NAME:-fastport-load-public-rt}"
SECURITY_LIST_NAME="${FASTPORT_SECURITY_LIST_NAME:-fastport-load-security-list}"
SERVER_NAME="${FASTPORT_SERVER_NAME:-fastport-server-a1}"
RUNNER_NAME="${FASTPORT_RUNNER_NAME:-fastport-runner-a1}"
VCN_CIDR="${FASTPORT_VCN_CIDR:-10.0.0.0/16}"
SUBNET_CIDR="${FASTPORT_SUBNET_CIDR:-10.0.1.0/24}"
SSH_PUBLIC_KEY_PATH="${OCI_SSH_PUBLIC_KEY_PATH:-$HOME/.ssh/id_ed25519.pub}"
STATE_DIR="${FASTPORT_CLOUD_OUTPUT:-artifacts/load-validation/cloud-server-runner-split}"
STATE_FILE="$STATE_DIR/oci-state.env"

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

first_or_empty() {
  jq -r '.data[0].id // empty'
}

require_non_empty() {
  local name="$1"
  local value="$2"
  [[ -n "$value" && "$value" != "null" ]] || fail "$name is empty"
}

progress() {
  printf '%s\n' "$*" >&2
}

detect_admin_cidr() {
  if [[ -n "${FASTPORT_ADMIN_CIDR:-}" ]]; then
    printf '%s\n' "$FASTPORT_ADMIN_CIDR"
    return
  fi

  require_command curl
  local ip
  ip="$(curl -4 -fsS https://ifconfig.me)"
  [[ "$ip" =~ ^([0-9]{1,3}\.){3}[0-9]{1,3}$ ]] || fail "could not detect admin IPv4 address"
  printf '%s/32\n' "$ip"
}

get_or_create_vcn() {
  local existing
  existing="$(oci network vcn list \
    --compartment-id "$COMPARTMENT_ID" \
    --display-name "$VCN_NAME" \
    --lifecycle-state AVAILABLE \
    --query 'data[0].id' \
    --raw-output 2>/dev/null || true)"

  if [[ -n "$existing" && "$existing" != "null" ]]; then
    progress "Using existing VCN: $VCN_NAME"
    printf '%s\n' "$existing"
    return
  fi

  progress "Creating VCN: $VCN_NAME"
  oci network vcn create \
    --compartment-id "$COMPARTMENT_ID" \
    --cidr-blocks "[\"$VCN_CIDR\"]" \
    --display-name "$VCN_NAME" \
    --dns-label fastport \
    --wait-for-state AVAILABLE >/dev/null

  oci network vcn list \
    --compartment-id "$COMPARTMENT_ID" \
    --display-name "$VCN_NAME" \
    --lifecycle-state AVAILABLE \
    --query 'data[0].id' \
    --raw-output
}

get_or_create_igw() {
  local existing
  existing="$(oci network internet-gateway list \
    --compartment-id "$COMPARTMENT_ID" \
    --vcn-id "$VCN_ID" \
    --display-name "$IGW_NAME" \
    --lifecycle-state AVAILABLE \
    --query 'data[0].id' \
    --raw-output 2>/dev/null || true)"

  if [[ -n "$existing" && "$existing" != "null" ]]; then
    progress "Using existing internet gateway: $IGW_NAME"
    printf '%s\n' "$existing"
    return
  fi

  progress "Creating internet gateway: $IGW_NAME"
  oci network internet-gateway create \
    --compartment-id "$COMPARTMENT_ID" \
    --vcn-id "$VCN_ID" \
    --is-enabled true \
    --display-name "$IGW_NAME" \
    --wait-for-state AVAILABLE >/dev/null

  oci network internet-gateway list \
    --compartment-id "$COMPARTMENT_ID" \
    --vcn-id "$VCN_ID" \
    --display-name "$IGW_NAME" \
    --lifecycle-state AVAILABLE \
    --query 'data[0].id' \
    --raw-output
}

get_or_create_route_table() {
  local existing
  existing="$(oci network route-table list \
    --compartment-id "$COMPARTMENT_ID" \
    --vcn-id "$VCN_ID" \
    --display-name "$ROUTE_TABLE_NAME" \
    --lifecycle-state AVAILABLE \
    --query 'data[0].id' \
    --raw-output 2>/dev/null || true)"

  if [[ -n "$existing" && "$existing" != "null" ]]; then
    progress "Using existing route table: $ROUTE_TABLE_NAME"
    printf '%s\n' "$existing"
    return
  fi

  local rules
  rules="$(jq -cn --arg igw "$IGW_ID" '[{cidrBlock:"0.0.0.0/0", networkEntityId:$igw}]')"

  progress "Creating route table: $ROUTE_TABLE_NAME"
  oci network route-table create \
    --compartment-id "$COMPARTMENT_ID" \
    --vcn-id "$VCN_ID" \
    --display-name "$ROUTE_TABLE_NAME" \
    --route-rules "$rules" \
    --wait-for-state AVAILABLE >/dev/null

  oci network route-table list \
    --compartment-id "$COMPARTMENT_ID" \
    --vcn-id "$VCN_ID" \
    --display-name "$ROUTE_TABLE_NAME" \
    --lifecycle-state AVAILABLE \
    --query 'data[0].id' \
    --raw-output
}

get_or_create_security_list() {
  local existing
  existing="$(oci network security-list list \
    --compartment-id "$COMPARTMENT_ID" \
    --vcn-id "$VCN_ID" \
    --display-name "$SECURITY_LIST_NAME" \
    --lifecycle-state AVAILABLE \
    --query 'data[0].id' \
    --raw-output 2>/dev/null || true)"

  if [[ -n "$existing" && "$existing" != "null" ]]; then
    progress "Using existing security list: $SECURITY_LIST_NAME"
    printf '%s\n' "$existing"
    return
  fi

  local ingress_rules
  local egress_rules
  ingress_rules="$(jq -cn --arg admin "$ADMIN_CIDR" --arg vcn "$VCN_CIDR" '
    [
      {
        source: $admin,
        protocol: "6",
        tcpOptions: {destinationPortRange: {min: 22, max: 22}}
      },
      {
        source: $vcn,
        protocol: "6",
        tcpOptions: {destinationPortRange: {min: 6628, max: 6628}}
      },
      {
        source: $vcn,
        protocol: "1",
        icmpOptions: {type: 3, code: 4}
      }
    ]')"
  egress_rules="$(jq -cn '[{destination:"0.0.0.0/0", protocol:"all"}]')"

  progress "Creating security list: $SECURITY_LIST_NAME"
  oci network security-list create \
    --compartment-id "$COMPARTMENT_ID" \
    --vcn-id "$VCN_ID" \
    --display-name "$SECURITY_LIST_NAME" \
    --ingress-security-rules "$ingress_rules" \
    --egress-security-rules "$egress_rules" \
    --wait-for-state AVAILABLE >/dev/null

  oci network security-list list \
    --compartment-id "$COMPARTMENT_ID" \
    --vcn-id "$VCN_ID" \
    --display-name "$SECURITY_LIST_NAME" \
    --lifecycle-state AVAILABLE \
    --query 'data[0].id' \
    --raw-output
}

get_or_create_subnet() {
  local existing
  existing="$(oci network subnet list \
    --compartment-id "$COMPARTMENT_ID" \
    --vcn-id "$VCN_ID" \
    --display-name "$SUBNET_NAME" \
    --lifecycle-state AVAILABLE \
    --query 'data[0].id' \
    --raw-output 2>/dev/null || true)"

  if [[ -n "$existing" && "$existing" != "null" ]]; then
    progress "Using existing subnet: $SUBNET_NAME"
    printf '%s\n' "$existing"
    return
  fi

  progress "Creating public subnet: $SUBNET_NAME"
  oci network subnet create \
    --compartment-id "$COMPARTMENT_ID" \
    --vcn-id "$VCN_ID" \
    --cidr-block "$SUBNET_CIDR" \
    --display-name "$SUBNET_NAME" \
    --dns-label load \
    --route-table-id "$ROUTE_TABLE_ID" \
    --security-list-ids "[\"$SECURITY_LIST_ID\"]" \
    --prohibit-public-ip-on-vnic false \
    --wait-for-state AVAILABLE >/dev/null

  oci network subnet list \
    --compartment-id "$COMPARTMENT_ID" \
    --vcn-id "$VCN_ID" \
    --display-name "$SUBNET_NAME" \
    --lifecycle-state AVAILABLE \
    --query 'data[0].id' \
    --raw-output
}

get_latest_image_id() {
  oci compute image list \
    --compartment-id "$COMPARTMENT_ID" \
    --shape "$FASTPORT_OCI_SHAPE" \
    --operating-system "Canonical Ubuntu" \
    --operating-system-version "24.04" \
    --sort-by TIMECREATED \
    --sort-order DESC \
    --limit 1 \
    --query 'data[0].id' \
    --raw-output
}

get_instance_id_by_name() {
  local name="$1"
  oci compute instance list \
    --compartment-id "$COMPARTMENT_ID" \
    --display-name "$name" \
    --lifecycle-state RUNNING \
    --query 'data[0].id' \
    --raw-output 2>/dev/null || true
}

launch_instance() {
  local name="$1"
  local role="$2"
  local ocpus="$3"
  local memory_gb="$4"
  local hostname="$5"

  local existing
  existing="$(get_instance_id_by_name "$name")"
  if [[ -n "$existing" && "$existing" != "null" ]]; then
    progress "Using existing running instance: $name"
    printf '%s\n' "$existing"
    return
  fi

  local shape_config
  local tags
  shape_config="$(jq -cn --argjson ocpus "$ocpus" --argjson memory "$memory_gb" '{ocpus:$ocpus, memoryInGBs:$memory}')"
  tags="$(jq -cn --arg role "$role" '{fastport:"cloud-server-runner-split-load-validation", role:$role, freeTierOnly:"true"}')"

  progress "Launching instance: $name (${ocpus} OCPU / ${memory_gb}GB)"
  if ! oci compute instance launch \
    --compartment-id "$COMPARTMENT_ID" \
    --availability-domain "$AVAILABILITY_DOMAIN" \
    --display-name "$name" \
    --hostname-label "$hostname" \
    --shape "$FASTPORT_OCI_SHAPE" \
    --shape-config "$shape_config" \
    --image-id "$IMAGE_ID" \
    --boot-volume-size-in-gbs 50 \
    --subnet-id "$SUBNET_ID" \
    --assign-public-ip true \
    --assign-private-dns-record true \
    --ssh-authorized-keys-file "$SSH_PUBLIC_KEY_PATH" \
    --freeform-tags "$tags" \
    --wait-for-state RUNNING \
    --max-wait-seconds 1200 \
    --wait-interval-seconds 15 >/dev/null; then
    fail "failed to launch instance: $name"
  fi

  local launched
  launched="$(get_instance_id_by_name "$name")"
  require_non_empty "$name instance_id" "$launched"
  printf '%s\n' "$launched"
}

describe_instance_network() {
  local instance_id="$1"
  local name="$2"
  local vnic_id
  local vnic_json

  vnic_id="$(oci compute vnic-attachment list \
    --compartment-id "$COMPARTMENT_ID" \
    --instance-id "$instance_id" \
    --query 'data[0]."vnic-id"' \
    --raw-output)"
  require_non_empty "$name vnic_id" "$vnic_id"

  vnic_json="$(oci network vnic get --vnic-id "$vnic_id" --output json)"

  jq -r --arg name "$name" '
    .data
    | {
        name: $name,
        privateIp: ."private-ip",
        publicIp: ."public-ip",
        hostname: ."hostname-label"
      }
    | @json
  ' <<<"$vnic_json"
}

"$SCRIPT_DIR/oci-free-tier-preflight.sh"

[[ -f "$SSH_PUBLIC_KEY_PATH" ]] || fail "SSH public key not found: $SSH_PUBLIC_KEY_PATH"
ADMIN_CIDR="$(detect_admin_cidr)"
COMPARTMENT_ID="$(resolve_compartment_id)"
require_non_empty COMPARTMENT_ID "$COMPARTMENT_ID"

AVAILABILITY_DOMAIN="${OCI_AVAILABILITY_DOMAIN:-}"
if [[ -z "$AVAILABILITY_DOMAIN" ]]; then
  AVAILABILITY_DOMAIN="$(oci iam availability-domain list \
    --compartment-id "$COMPARTMENT_ID" \
    --query 'data[0].name' \
    --raw-output)"
fi
require_non_empty AVAILABILITY_DOMAIN "$AVAILABILITY_DOMAIN"

IMAGE_ID="$(get_latest_image_id)"
require_non_empty IMAGE_ID "$IMAGE_ID"

info ""
info "Provisioning target:"
info "  availability domain: $AVAILABILITY_DOMAIN"
info "  image: Canonical Ubuntu 24.04 aarch64 latest"
info "  SSH admin CIDR: $ADMIN_CIDR"
info "  port 6628: VCN-only"

VCN_ID="$(get_or_create_vcn)"
IGW_ID="$(get_or_create_igw)"
ROUTE_TABLE_ID="$(get_or_create_route_table)"
SECURITY_LIST_ID="$(get_or_create_security_list)"
SUBNET_ID="$(get_or_create_subnet)"

SERVER_ID="$(launch_instance "$SERVER_NAME" server "$FASTPORT_SERVER_OCPUS" "$FASTPORT_SERVER_MEMORY_GB" fastportserver)"
RUNNER_ID="$(launch_instance "$RUNNER_NAME" runner "$FASTPORT_RUNNER_OCPUS" "$FASTPORT_RUNNER_MEMORY_GB" fastportrunner)"

SERVER_NET="$(describe_instance_network "$SERVER_ID" "$SERVER_NAME")"
RUNNER_NET="$(describe_instance_network "$RUNNER_ID" "$RUNNER_NAME")"

mkdir -p "$STATE_DIR"
{
  printf 'export FASTPORT_CLOUD_AD=%q\n' "$AVAILABILITY_DOMAIN"
  printf 'export FASTPORT_SERVER_PRIVATE_IP=%q\n' "$(jq -r '.privateIp' <<<"$SERVER_NET")"
  printf 'export FASTPORT_SERVER_PUBLIC_IP=%q\n' "$(jq -r '.publicIp // ""' <<<"$SERVER_NET")"
  printf 'export FASTPORT_RUNNER_PRIVATE_IP=%q\n' "$(jq -r '.privateIp' <<<"$RUNNER_NET")"
  printf 'export FASTPORT_RUNNER_PUBLIC_IP=%q\n' "$(jq -r '.publicIp // ""' <<<"$RUNNER_NET")"
} > "$STATE_FILE"
chmod 600 "$STATE_FILE"

info ""
info "Created or verified FastPort free-tier lab:"
jq -n --argjson server "$SERVER_NET" --argjson runner "$RUNNER_NET" \
  '{server:$server, runner:$runner}'
info ""
info "Local state written to $STATE_FILE"
