#!/usr/bin/env bash
set -euo pipefail

fail() {
  printf 'ERROR: %s\n' "$*" >&2
  exit 1
}

info() {
  printf '%s\n' "$*"
}

require_var() {
  local name="$1"
  local value="${!name:-}"
  [[ -n "$value" ]] || fail "required environment variable is empty: $name"
}

require_var FASTPORT_SERVER_HOST

FASTPORT_SERVER_PORT="${FASTPORT_SERVER_PORT:-6628}"
FASTPORT_CONNECT_TIMEOUT_SECONDS="${FASTPORT_CONNECT_TIMEOUT_SECONDS:-5}"

[[ "$FASTPORT_SERVER_PORT" =~ ^[0-9]+$ ]] || fail "FASTPORT_SERVER_PORT must be an integer"
[[ "$FASTPORT_CONNECT_TIMEOUT_SECONDS" =~ ^[0-9]+$ ]] || fail "FASTPORT_CONNECT_TIMEOUT_SECONDS must be an integer"
[[ "$FASTPORT_SERVER_HOST" =~ ^[A-Za-z0-9._:-]+$ ]] || fail "FASTPORT_SERVER_HOST contains unsupported characters"

info "FastPort runner connectivity check"
info "server host: set-redacted"
info "server port: $FASTPORT_SERVER_PORT"

if command -v nc >/dev/null 2>&1; then
  nc -vz -w "$FASTPORT_CONNECT_TIMEOUT_SECONDS" "$FASTPORT_SERVER_HOST" "$FASTPORT_SERVER_PORT"
else
  timeout "$FASTPORT_CONNECT_TIMEOUT_SECONDS" bash -c "cat < /dev/null > /dev/tcp/$FASTPORT_SERVER_HOST/$FASTPORT_SERVER_PORT"
fi

info "Runner can connect to server TCP port $FASTPORT_SERVER_PORT."
