#!/usr/bin/env bash
set -euo pipefail

fail() {
  printf 'ERROR: %s\n' "$*" >&2
  exit 1
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

FASTPORT_CLOUD_REPO_DIR="${FASTPORT_CLOUD_REPO_DIR:-FastPortSharp}"
FASTPORT_SSH_CONNECT_TIMEOUT="${FASTPORT_SSH_CONNECT_TIMEOUT:-8}"
FASTPORT_RUNNER_MODE="${FASTPORT_RUNNER_MODE:-local}"

[[ "$FASTPORT_CLOUD_REPO_DIR" != *"'"* ]] || fail "FASTPORT_CLOUD_REPO_DIR must not contain single quotes"

require_var FASTPORT_SERVER_SSH_TARGET
if [[ "$FASTPORT_RUNNER_MODE" == "cloud" ]]; then
  require_var FASTPORT_RUNNER_SSH_TARGET
fi

ssh_opts=(
  -o BatchMode=yes
  -o ConnectTimeout="$FASTPORT_SSH_CONNECT_TIMEOUT"
)
if [[ -n "${FASTPORT_SSH_KEY_PATH:-}" ]]; then
  ssh_opts+=(-i "$FASTPORT_SSH_KEY_PATH")
fi

check_remote() {
  local role="$1"
  local target="$2"
  local readiness_file="/tmp/fastport-${role}-os-readiness.txt"

  info ""
  info "## $role SSH readiness"
  info "target: set-redacted"

  ssh "${ssh_opts[@]}" "$target" "set -e
command -v git >/dev/null
command -v dotnet >/dev/null
command -v jq >/dev/null
command -v tmux >/dev/null
test -d '$FASTPORT_CLOUD_REPO_DIR'
cd '$FASTPORT_CLOUD_REPO_DIR'
git rev-parse --short HEAD
dotnet --version
scripts/cloud/os-readiness.sh > '$readiness_file'
test -s '$readiness_file'"

  info "$role SSH readiness passed; OS readiness captured at $readiness_file"
}

check_remote "server" "$FASTPORT_SERVER_SSH_TARGET"
if [[ "$FASTPORT_RUNNER_MODE" == "cloud" ]]; then
  check_remote "runner" "$FASTPORT_RUNNER_SSH_TARGET"
else
  info ""
  info "## local runner readiness"
  command -v dotnet >/dev/null
  command -v jq >/dev/null
  dotnet --version
  scripts/cloud/os-readiness.sh > /tmp/fastport-local-runner-os-readiness.txt
  test -s /tmp/fastport-local-runner-os-readiness.txt
  info "local runner readiness passed; OS readiness captured at /tmp/fastport-local-runner-os-readiness.txt"
fi

info ""
info "Readiness passed for server SSH and runner mode: $FASTPORT_RUNNER_MODE."
