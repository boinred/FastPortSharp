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

require_var() {
  local name="$1"
  local value="${!name:-}"
  [[ -n "$value" ]] || fail "required environment variable is empty: $name"
}

require_integer() {
  local name="$1"
  local value="${!name:-}"
  [[ "$value" =~ ^[0-9]+$ ]] || fail "$name must be an integer, got: ${value:-<empty>}"
}

FASTPORT_OCI_REGION="${FASTPORT_OCI_REGION:-us-chicago-1}"
FASTPORT_OCI_SHAPE="${FASTPORT_OCI_SHAPE:-VM.Standard.A1.Flex}"
FASTPORT_RUNNER_MODE="${FASTPORT_RUNNER_MODE:-local}"
FASTPORT_SERVER_OCPUS="${FASTPORT_SERVER_OCPUS:-2}"
FASTPORT_SERVER_MEMORY_GB="${FASTPORT_SERVER_MEMORY_GB:-12}"
if [[ "$FASTPORT_RUNNER_MODE" == "cloud" ]]; then
  FASTPORT_RUNNER_OCPUS="${FASTPORT_RUNNER_OCPUS:-2}"
  FASTPORT_RUNNER_MEMORY_GB="${FASTPORT_RUNNER_MEMORY_GB:-12}"
else
  FASTPORT_RUNNER_OCPUS="${FASTPORT_RUNNER_OCPUS:-0}"
  FASTPORT_RUNNER_MEMORY_GB="${FASTPORT_RUNNER_MEMORY_GB:-0}"
fi
FASTPORT_SERVER_BOOT_VOLUME_GB="${FASTPORT_SERVER_BOOT_VOLUME_GB:-50}"
if [[ "$FASTPORT_RUNNER_MODE" == "cloud" ]]; then
  FASTPORT_RUNNER_BOOT_VOLUME_GB="${FASTPORT_RUNNER_BOOT_VOLUME_GB:-50}"
else
  FASTPORT_RUNNER_BOOT_VOLUME_GB="${FASTPORT_RUNNER_BOOT_VOLUME_GB:-0}"
fi

FASTPORT_FREE_TIER_REGION="us-chicago-1"
FASTPORT_FREE_TIER_SHAPE="VM.Standard.A1.Flex"
FASTPORT_FREE_TIER_MAX_OCPUS=4
FASTPORT_FREE_TIER_MAX_MEMORY_GB=24
FASTPORT_FREE_TIER_MAX_BOOT_VOLUME_GB=200

assert_free_tier_config() {
  require_integer FASTPORT_SERVER_OCPUS
  require_integer FASTPORT_SERVER_MEMORY_GB
  require_integer FASTPORT_RUNNER_OCPUS
  require_integer FASTPORT_RUNNER_MEMORY_GB
  require_integer FASTPORT_SERVER_BOOT_VOLUME_GB
  require_integer FASTPORT_RUNNER_BOOT_VOLUME_GB

  [[ "$FASTPORT_OCI_REGION" == "$FASTPORT_FREE_TIER_REGION" ]] ||
    fail "refusing non-home/free-tier region: $FASTPORT_OCI_REGION (expected $FASTPORT_FREE_TIER_REGION)"

  [[ "$FASTPORT_OCI_SHAPE" == "$FASTPORT_FREE_TIER_SHAPE" ]] ||
    fail "refusing non-free-tier shape: $FASTPORT_OCI_SHAPE (expected $FASTPORT_FREE_TIER_SHAPE)"

  local total_ocpus=$((FASTPORT_SERVER_OCPUS + FASTPORT_RUNNER_OCPUS))
  local total_memory=$((FASTPORT_SERVER_MEMORY_GB + FASTPORT_RUNNER_MEMORY_GB))
  local total_boot_volume=$((FASTPORT_SERVER_BOOT_VOLUME_GB + FASTPORT_RUNNER_BOOT_VOLUME_GB))

  (( total_ocpus <= FASTPORT_FREE_TIER_MAX_OCPUS )) ||
    fail "refusing OCPU total $total_ocpus; max free-tier A1 total is $FASTPORT_FREE_TIER_MAX_OCPUS"

  (( total_memory <= FASTPORT_FREE_TIER_MAX_MEMORY_GB )) ||
    fail "refusing memory total ${total_memory}GB; max free-tier A1 total is ${FASTPORT_FREE_TIER_MAX_MEMORY_GB}GB"

  (( total_boot_volume <= FASTPORT_FREE_TIER_MAX_BOOT_VOLUME_GB )) ||
    fail "refusing boot volume total ${total_boot_volume}GB; max planned free-tier block volume total is ${FASTPORT_FREE_TIER_MAX_BOOT_VOLUME_GB}GB"

  (( FASTPORT_SERVER_OCPUS > 0 )) ||
    fail "server OCPUs must be greater than zero"

  (( FASTPORT_SERVER_MEMORY_GB > 0 )) ||
    fail "server memory must be greater than zero"

  if [[ "$FASTPORT_RUNNER_MODE" == "cloud" ]]; then
    (( FASTPORT_RUNNER_OCPUS > 0 )) ||
      fail "cloud runner OCPUs must be greater than zero"
    (( FASTPORT_RUNNER_MEMORY_GB > 0 )) ||
      fail "cloud runner memory must be greater than zero"
  fi
}

print_free_tier_config() {
  info "FastPort cloud validation free-tier config:"
  info "  region: $FASTPORT_OCI_REGION"
  info "  shape: $FASTPORT_OCI_SHAPE"
  info "  runner mode: $FASTPORT_RUNNER_MODE"
  info "  server: ${FASTPORT_SERVER_OCPUS} OCPU / ${FASTPORT_SERVER_MEMORY_GB}GB RAM / ${FASTPORT_SERVER_BOOT_VOLUME_GB}GB boot"
  info "  runner: ${FASTPORT_RUNNER_OCPUS} OCPU / ${FASTPORT_RUNNER_MEMORY_GB}GB RAM / ${FASTPORT_RUNNER_BOOT_VOLUME_GB}GB boot"
  info "  total: $((FASTPORT_SERVER_OCPUS + FASTPORT_RUNNER_OCPUS)) OCPU / $((FASTPORT_SERVER_MEMORY_GB + FASTPORT_RUNNER_MEMORY_GB))GB RAM / $((FASTPORT_SERVER_BOOT_VOLUME_GB + FASTPORT_RUNNER_BOOT_VOLUME_GB))GB boot"
}

assert_free_tier_config

if [[ "${BASH_SOURCE[0]}" == "$0" ]]; then
  print_free_tier_config
fi
