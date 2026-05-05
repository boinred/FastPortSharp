#!/usr/bin/env bash
set -euo pipefail

if [[ -z "${FASTPORT_SERVER_HOST:-}" ]]; then
  echo "ERROR: FASTPORT_SERVER_HOST is required. Use the cloud server public IP or DNS for local-runner validation." >&2
  exit 1
fi

FASTPORT_SERVER_PORT="${FASTPORT_SERVER_PORT:-6628}"
FASTPORT_CLOUD_OUTPUT="${FASTPORT_CLOUD_OUTPUT:-artifacts/load-validation/cloud-server-runner-split}"
FASTPORT_OUTPUT="$FASTPORT_CLOUD_OUTPUT/s5-random-10k"
FASTPORT_SERVER_METRICS="${FASTPORT_SERVER_METRICS:-}"

mkdir -p "$FASTPORT_CLOUD_OUTPUT/runner" "$FASTPORT_OUTPUT"

scripts/cloud/os-readiness.sh > "$FASTPORT_CLOUD_OUTPUT/runner/os-readiness.txt"

dotnet build FastPortTestLoadRunner/FastPortTestLoadRunner.csproj -c Release
dotnet build FastPortTestLoadValidation/FastPortTestLoadValidation.csproj -c Release

args=(
  run --no-build -c Release --project FastPortTestLoadValidation --
  --profile staged
  --stage s5-random-10k
  --host "$FASTPORT_SERVER_HOST"
  --port "$FASTPORT_SERVER_PORT"
  --pacing-policy adaptive-window
  --output "$FASTPORT_OUTPUT"
  --runner-no-build
)

if [[ -n "$FASTPORT_SERVER_METRICS" ]]; then
  args+=(--server-metrics "$FASTPORT_SERVER_METRICS")
fi

export FASTPORT_CLOUD_COMMAND="${FASTPORT_CLOUD_COMMAND:-scripts/cloud/runner-10k.sh}"
scripts/cloud/write-manifest.sh runner-10k

echo "Running focused 10K validation against $FASTPORT_SERVER_HOST:$FASTPORT_SERVER_PORT"
dotnet "${args[@]}"
