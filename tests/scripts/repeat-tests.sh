#!/usr/bin/env bash
# tests/scripts/repeat-tests.sh
# Race-detector helper — repeatedly runs a test filter to surface flaky cases.
# Design Ref: fix-base-session-send-fifo-test-flakiness §8.3.
#
# Usage:
#   tests/scripts/repeat-tests.sh                   # 50 iters, BaseSession send tests
#   tests/scripts/repeat-tests.sh 100               # 100 iters, default filter
#   tests/scripts/repeat-tests.sh 50 "FullyQualifiedName~MyOtherTest"
#                                                    # custom MSTest filter
#
# Exits 0 if every iteration passes; non-zero with summary otherwise.

set -euo pipefail

REPEAT="${1:-50}"
FILTER="${2:-FullyQualifiedName~BaseSessionSendPolicyTests.BaseSession_DoWorkSendBuffers_}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
SLN="${REPO_ROOT}/FastPortSharp.sln"

echo "stress: REPEAT=${REPEAT}  FILTER=${FILTER}"
echo "build (Release, --no-restore tolerated by build itself)..."
dotnet build "${SLN}" -c Release >/dev/null

fail=0
log_dir="$(mktemp -d -t scaffold-stress.XXXXXX)"

for i in $(seq 1 "${REPEAT}"); do
  out="${log_dir}/iter-${i}.log"
  if ! dotnet test "${SLN}" -c Release --no-build \
        --filter "${FILTER}" \
        --logger "console;verbosity=quiet" >"${out}" 2>&1; then
    echo "iter ${i}: FAIL"
    tail -20 "${out}"
    fail=$((fail + 1))
  fi
  if (( i % 10 == 0 )); then
    echo "  progress: iter ${i}/${REPEAT} (fail so far: ${fail})"
  fi
done

echo
echo "── summary ──"
echo "  pass: $((REPEAT - fail)) / ${REPEAT}"
echo "  fail: ${fail} / ${REPEAT}"
if [ "${fail}" -gt 0 ]; then
  echo "  logs kept under: ${log_dir}"
  exit 1
fi

# clean up logs on success
rm -rf "${log_dir}"
exit 0
