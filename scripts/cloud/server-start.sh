#!/usr/bin/env bash
set -euo pipefail

FASTPORT_SERVER_PORT="${FASTPORT_SERVER_PORT:-6628}"
FASTPORT_CLOUD_OUTPUT="${FASTPORT_CLOUD_OUTPUT:-artifacts/load-validation/cloud-server-runner-split}"
FASTPORT_SERVER_METRICS="$FASTPORT_CLOUD_OUTPUT/server/server.metrics.jsonl"

mkdir -p "$FASTPORT_CLOUD_OUTPUT/server"

scripts/cloud/os-readiness.sh > "$FASTPORT_CLOUD_OUTPUT/server/os-readiness.txt"

export FastPortTestSmokeServer__Host=0.0.0.0
export FastPortTestSmokeServer__Port="$FASTPORT_SERVER_PORT"
export Telemetry__Output="$FASTPORT_SERVER_METRICS"
export Telemetry__IntervalSeconds=1
export FASTPORT_CLOUD_COMMAND="${FASTPORT_CLOUD_COMMAND:-scripts/cloud/server-start.sh}"

scripts/cloud/write-manifest.sh server

echo "Starting FastPortTestSmokeServer on 0.0.0.0:$FASTPORT_SERVER_PORT"
echo "Server metrics: $FASTPORT_SERVER_METRICS"

dotnet build FastPortTestSmokeServer/FastPortTestSmokeServer.csproj -c Release
dotnet run --no-build -c Release --project FastPortTestSmokeServer
