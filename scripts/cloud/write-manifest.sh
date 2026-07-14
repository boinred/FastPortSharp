#!/usr/bin/env bash
set -euo pipefail

fail() {
  printf 'ERROR: %s\n' "$*" >&2
  exit 1
}

json_escape() {
  printf '%s' "$1" | sed 's/\\/\\\\/g; s/"/\\"/g'
}

role="${1:-run}"
if [[ ! "$role" =~ ^[A-Za-z0-9._-]+$ ]]; then
  fail "manifest role must contain only letters, numbers, dot, underscore, or dash"
fi

FASTPORT_CLOUD_OUTPUT="${FASTPORT_CLOUD_OUTPUT:-artifacts/load-validation/cloud-server-runner-split}"
FASTPORT_CLOUD_PROVIDER="${FASTPORT_CLOUD_PROVIDER:-azure}"
FASTPORT_BUILD_CONFIGURATION="${FASTPORT_BUILD_CONFIGURATION:-Release}"
FASTPORT_ENDPOINT_TYPE="${FASTPORT_ENDPOINT_TYPE:-public-ip}"
FASTPORT_RUNNER_MODE="${FASTPORT_RUNNER_MODE:-local}"
FASTPORT_SERVER_PORT="${FASTPORT_SERVER_PORT:-6628}"

mkdir -p "$FASTPORT_CLOUD_OUTPUT"

created_utc="$(date -u +"%Y-%m-%dT%H:%M:%SZ")"
manifest_json="$FASTPORT_CLOUD_OUTPUT/manifest.$role.json"
manifest_md="$FASTPORT_CLOUD_OUTPUT/manifest.$role.md"

git_sha="unknown"
git_branch="unknown"
tracked_dirty="unknown"
if git rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  git_sha="$(git rev-parse HEAD 2>/dev/null || printf 'unknown')"
  git_branch="$(git rev-parse --abbrev-ref HEAD 2>/dev/null || printf 'unknown')"
  tracked_dirty=false
  if ! git diff --quiet --ignore-submodules -- 2>/dev/null || ! git diff --cached --quiet --ignore-submodules -- 2>/dev/null; then
    tracked_dirty=true
  fi
fi

dotnet_version="not-installed"
if command -v dotnet >/dev/null 2>&1; then
  dotnet_version="$(dotnet --version 2>/dev/null || printf 'unknown')"
fi

location="${FASTPORT_AZURE_LOCATION:-${FASTPORT_OCI_REGION:-unknown}}"
resource_group="${FASTPORT_AZURE_RESOURCE_GROUP:-unknown}"
server_size="${FASTPORT_AZURE_SERVER_SIZE:-${FASTPORT_OCI_SHAPE:-unknown}}"
runner_size="${FASTPORT_AZURE_RUNNER_SIZE:-unknown}"
cloud_command="${FASTPORT_CLOUD_COMMAND:-scripts/cloud/$role.sh}"

server_host_state="unset"
if [[ -n "${FASTPORT_SERVER_HOST:-}" ]]; then
  server_host_state="set-redacted"
fi

cat > "$manifest_json" <<JSON
{
  "createdUtc": "$(json_escape "$created_utc")",
  "role": "$(json_escape "$role")",
  "provider": "$(json_escape "$FASTPORT_CLOUD_PROVIDER")",
  "location": "$(json_escape "$location")",
  "resourceGroup": "$(json_escape "$resource_group")",
  "endpointType": "$(json_escape "$FASTPORT_ENDPOINT_TYPE")",
  "runnerMode": "$(json_escape "$FASTPORT_RUNNER_MODE")",
  "serverHost": "$(json_escape "$server_host_state")",
  "serverPort": "$(json_escape "$FASTPORT_SERVER_PORT")",
  "serverSize": "$(json_escape "$server_size")",
  "runnerSize": "$(json_escape "$runner_size")",
  "buildConfiguration": "$(json_escape "$FASTPORT_BUILD_CONFIGURATION")",
  "gitSha": "$(json_escape "$git_sha")",
  "gitBranch": "$(json_escape "$git_branch")",
  "trackedDirty": "$(json_escape "$tracked_dirty")",
  "dotnetVersion": "$(json_escape "$dotnet_version")",
  "command": "$(json_escape "$cloud_command")"
}
JSON

cat > "$manifest_md" <<MD
# FastPort Cloud Validation Manifest

- created UTC: \`$created_utc\`
- role: \`$role\`
- provider: \`$FASTPORT_CLOUD_PROVIDER\`
- location: \`$location\`
- resource group: \`$resource_group\`
- endpoint type: \`$FASTPORT_ENDPOINT_TYPE\`
- runner mode: \`$FASTPORT_RUNNER_MODE\`
- server host: \`$server_host_state\`
- server port: \`$FASTPORT_SERVER_PORT\`
- server size: \`$server_size\`
- runner size: \`$runner_size\`
- build configuration: \`$FASTPORT_BUILD_CONFIGURATION\`
- git SHA: \`$git_sha\`
- git branch: \`$git_branch\`
- tracked dirty: \`$tracked_dirty\`
- dotnet version: \`$dotnet_version\`
- command: \`$cloud_command\`

This manifest intentionally redacts host/IP details and does not record tenant IDs, subscription IDs, keys, or credentials.
MD

printf 'Wrote cloud manifest: %s\n' "$manifest_json"
