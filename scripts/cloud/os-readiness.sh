#!/usr/bin/env bash
set -euo pipefail

echo "# OS readiness"
date -u +"timestamp_utc=%Y-%m-%dT%H:%M:%SZ"
echo "hostname=$(hostname)"
echo

echo "## Kernel"
uname -a
echo

echo "## .NET"
if command -v dotnet >/dev/null 2>&1; then
  dotnet --info
else
  echo "dotnet: not installed"
fi
echo

echo "## Limits"
echo "ulimit_n=$(ulimit -n)"
echo

echo "## TCP settings"
if [[ -r /proc/sys/net/ipv4/ip_local_port_range ]]; then
  echo -n "ip_local_port_range="
  cat /proc/sys/net/ipv4/ip_local_port_range
fi
sysctl net.core.somaxconn 2>/dev/null || true
sysctl net.ipv4.tcp_max_syn_backlog 2>/dev/null || true
echo

echo "## CPU and memory"
nproc 2>/dev/null || true
free -h 2>/dev/null || true
echo

echo "## Socket summary"
ss -s 2>/dev/null || true
