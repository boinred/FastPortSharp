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

require_command ssh
require_command scp

require_var FASTPORT_SERVER_SSH_TARGET

FASTPORT_CLOUD_REMOTE_OUTPUT="${FASTPORT_CLOUD_REMOTE_OUTPUT:-FastPortSharp/artifacts/load-validation/cloud-server-runner-split}"
FASTPORT_CLOUD_OUTPUT="${FASTPORT_CLOUD_OUTPUT:-artifacts/load-validation/cloud-server-runner-split}"
FASTPORT_SSH_CONNECT_TIMEOUT="${FASTPORT_SSH_CONNECT_TIMEOUT:-8}"
FASTPORT_COLLECTED_OUTPUT="${FASTPORT_COLLECTED_OUTPUT:-$FASTPORT_CLOUD_OUTPUT/collected}"
FASTPORT_RUNNER_MODE="${FASTPORT_RUNNER_MODE:-local}"

[[ "$FASTPORT_CLOUD_REMOTE_OUTPUT" != *"'"* ]] || fail "FASTPORT_CLOUD_REMOTE_OUTPUT must not contain single quotes"

ssh_opts=(
  -o BatchMode=yes
  -o ConnectTimeout="$FASTPORT_SSH_CONNECT_TIMEOUT"
)
if [[ -n "${FASTPORT_SSH_KEY_PATH:-}" ]]; then
  ssh_opts+=(-i "$FASTPORT_SSH_KEY_PATH")
fi
scp_opts=("${ssh_opts[@]}")

copy_remote_path() {
  local role="$1"
  local target="$2"
  local relative_path="$3"
  local destination="$4"

  if ssh "${ssh_opts[@]}" "$target" "test -e '$FASTPORT_CLOUD_REMOTE_OUTPUT/$relative_path'"; then
    scp "${scp_opts[@]}" -r "$target:$FASTPORT_CLOUD_REMOTE_OUTPUT/$relative_path" "$destination/"
    info "$role copied: $relative_path"
  else
    warn "$role artifact missing: $relative_path"
  fi
}

collect_role() {
  local role="$1"
  local target="$2"
  shift 2

  local destination="$FASTPORT_COLLECTED_OUTPUT/$role"
  mkdir -p "$destination"

  info ""
  info "## Collecting $role artifacts"
  info "target: set-redacted"
  info "destination: $destination"

  local relative_path
  for relative_path in "$@"; do
    copy_remote_path "$role" "$target" "$relative_path" "$destination"
  done
}

collect_role "server" "$FASTPORT_SERVER_SSH_TARGET" \
  server \
  manifest.server.json \
  manifest.server.md

if [[ "$FASTPORT_RUNNER_MODE" == "cloud" ]]; then
  require_var FASTPORT_RUNNER_SSH_TARGET
  collect_role "runner" "$FASTPORT_RUNNER_SSH_TARGET" \
    runner \
    smoke \
    s5-random-10k \
    manifest.runner-smoke.json \
    manifest.runner-smoke.md \
    manifest.runner-10k.json \
    manifest.runner-10k.md
else
  local_runner_output="$FASTPORT_COLLECTED_OUTPUT/runner-local"
  mkdir -p "$local_runner_output"

  info ""
  info "## Local runner artifacts"
  info "runner mode: $FASTPORT_RUNNER_MODE"
  info "source: $FASTPORT_CLOUD_OUTPUT"
  info "destination: $local_runner_output"

  for path in runner smoke s5-random-10k manifest.runner-smoke.json manifest.runner-smoke.md manifest.runner-10k.json manifest.runner-10k.md; do
    if [[ -e "$FASTPORT_CLOUD_OUTPUT/$path" ]]; then
      cp -R "$FASTPORT_CLOUD_OUTPUT/$path" "$local_runner_output/"
      info "local runner copied: $path"
    else
      warn "local runner artifact missing: $path"
    fi
  done
fi

info ""
info "Artifact collection finished under $FASTPORT_COLLECTED_OUTPUT."
info "Generated artifacts remain local runtime output; do not commit them."
