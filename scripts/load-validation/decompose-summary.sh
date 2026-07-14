#!/usr/bin/env bash
set -euo pipefail

SUMMARY_PATH="${1:-artifacts/load-validation/s5-session-rtt-validation/summary.json}"
STAGE_INDEX="${STAGE_INDEX:-0}"

fail() {
  printf 'ERROR: %s\n' "$*" >&2
  exit 1
}

require_command() {
  command -v "$1" >/dev/null 2>&1 || fail "required command not found: $1"
}

json_number() {
  local path="$1"
  jq -r --argjson index "$STAGE_INDEX" "$path // 0" "$SUMMARY_PATH"
}

json_string() {
  local path="$1"
  jq -r --argjson index "$STAGE_INDEX" "$path // \"\"" "$SUMMARY_PATH"
}

format_number() {
  local value="$1"
  awk -v value="$value" 'BEGIN { printf "%.2f", value }'
}

format_int() {
  local value="$1"
  awk -v value="$value" 'BEGIN { printf "%.0f", value }'
}

verdict() {
  local condition="$1"
  local yes="$2"
  local no="$3"
  if [[ "$condition" == "true" ]]; then
    printf '%s\n' "$yes"
  else
    printf '%s\n' "$no"
  fi
}

compare_float() {
  local left="$1"
  local op="$2"
  local right="$3"
  awk -v left="$left" -v right="$right" -v op="$op" '
    BEGIN {
      if (op == ">") exit !(left > right)
      if (op == ">=") exit !(left >= right)
      if (op == "<") exit !(left < right)
      if (op == "<=") exit !(left <= right)
      if (op == "==") exit !(left == right)
      exit 2
    }'
}

require_command jq
require_command awk

[[ -f "$SUMMARY_PATH" ]] || fail "summary file not found: $SUMMARY_PATH"

STAGE_COUNT="$(jq '.stages | length' "$SUMMARY_PATH")"
if (( STAGE_INDEX < 0 || STAGE_INDEX >= STAGE_COUNT )); then
  fail "STAGE_INDEX $STAGE_INDEX is out of range; stage count is $STAGE_COUNT"
fi

stage_id="$(json_string '.stages[$index].stageId')"
passed="$(jq -r --argjson index "$STAGE_INDEX" '.stages[$index].passed // false' "$SUMMARY_PATH")"
target_sessions="$(json_number '.stages[$index].targetSessions')"
peak_sessions="$(json_number '.stages[$index].peakCurrentSessions')"
max_tps="$(json_number '.stages[$index].maxTps')"
rtt_p95="$(json_number '.stages[$index].maxRttP95Ms')"
rtt_p99="$(json_number '.stages[$index].maxRttP99Ms')"
pending_requests="$(json_number '.stages[$index].maxPendingRequestCount')"
pending_send_requests="$(json_number '.stages[$index].maxPendingSendRequests')"
pacing_wait="$(json_number '.stages[$index].maxPacingAverageWaitMs')"
min_window="$(json_number '.stages[$index].minObservedPacingWindow')"
max_window="$(json_number '.stages[$index].maxObservedPacingWindow')"
send_backpressure="$(json_number '.stages[$index].maxSendBackpressureEvents')"
send_drain_yield="$(json_number '.stages[$index].maxSendDrainYieldCount')"
send_buffer_bytes="$(json_number '.stages[$index].maxSendBufferBytes')"
session_p95_of_p95="$(json_number '.stages[$index].maxSessionRttP95OfP95Ms')"
scheduler_drift="$(json_number '.stages[$index].maxSchedulerDriftMs')"
no_buffer_errors="$(jq -r --argjson index "$STAGE_INDEX" '.stages[$index].socketErrorCountsByClass["send|IOException|NoBufferSpaceAvailable"] // 0' "$SUMMARY_PATH")"
receive_timeouts="$(jq -r --argjson index "$STAGE_INDEX" '.stages[$index].socketErrorCountsByClass["receive|IOException|TimedOut"] // 0' "$SUMMARY_PATH")"

rtt_gap="$(awk -v global="$rtt_p95" -v session="$session_p95_of_p95" 'BEGIN { print global - session }')"
rtt_gap_abs="$(awk -v gap="$rtt_gap" 'BEGIN { if (gap < 0) print -gap; else print gap }')"
rtt_gap_ratio="$(awk -v gap="$rtt_gap_abs" -v g="$rtt_p95" 'BEGIN { if (g <= 0) print 0; else print gap / g }')"
pending_per_session="$(awk -v pending="$pending_requests" -v s="$target_sessions" 'BEGIN { if (s <= 0) print 0; else print pending / s }')"

systemic_tail=false
if compare_float "$rtt_p95" ">=" 1000 && compare_float "$rtt_gap_ratio" "<=" 0.15; then
  systemic_tail=true
fi

pacing_pressure=false
if compare_float "$pacing_wait" ">=" 100 || compare_float "$min_window" "<=" 1; then
  pacing_pressure=true
fi

client_outpacing=false
if compare_float "$pending_per_session" ">=" 1; then
  client_outpacing=true
fi

server_send_pressure=false
if compare_float "$pending_send_requests" ">" 0 || compare_float "$send_backpressure" ">" 0 || compare_float "$send_buffer_bytes" ">" 0; then
  server_send_pressure=true
fi

socket_pressure=false
if compare_float "$no_buffer_errors" ">" 0 || compare_float "$receive_timeouts" ">" 0; then
  socket_pressure=true
fi

scheduler_noise=false
if compare_float "$scheduler_drift" ">=" 50; then
  scheduler_noise=true
fi

next_lane="send-throughput-drain-fairness-optimization"
if [[ "$pacing_pressure" == "true" && "$client_outpacing" == "true" ]]; then
  next_lane="adaptive-client-pacing-threshold-tuning"
elif [[ "$socket_pressure" == "true" && "$server_send_pressure" == "true" ]]; then
  next_lane="send-throughput-drain-fairness-optimization"
elif [[ "$socket_pressure" == "true" && "$receive_timeouts" -gt 0 ]]; then
  next_lane="receive-timeout-tail-flow-control"
elif [[ "$scheduler_noise" == "true" ]]; then
  next_lane="cloud-split-validation-or-local-noise-isolation"
fi

cat <<MARKDOWN
# Throughput/Pacing/Server Processing Decomposition

Source: \`$SUMMARY_PATH\`

| Field | Value |
|-------|------:|
| Stage | \`$stage_id\` |
| Passed | \`$passed\` |
| Peak sessions | $(format_int "$peak_sessions") / $(format_int "$target_sessions") |
| Max TPS | $(format_number "$max_tps") |
| RTT P95 | $(format_number "$rtt_p95") ms |
| RTT P99 | $(format_number "$rtt_p99") ms |
| Per-session P95-of-P95 | $(format_number "$session_p95_of_p95") ms |
| RTT gap ratio | $(format_number "$(awk -v ratio="$rtt_gap_ratio" 'BEGIN { print ratio * 100 }')")% |
| Pending requests | $(format_int "$pending_requests") |
| Pending requests/session | $(format_number "$pending_per_session") |
| Pending server send requests | $(format_int "$pending_send_requests") |
| Server send backpressure events | $(format_int "$send_backpressure") |
| Server send drain yields | $(format_int "$send_drain_yield") |
| Max server send buffer bytes | $(format_int "$send_buffer_bytes") |
| Pacing average wait max | $(format_number "$pacing_wait") ms |
| Pacing window range | $(format_int "$min_window") - $(format_int "$max_window") |
| NoBufferSpaceAvailable | $(format_int "$no_buffer_errors") |
| Receive timeouts | $(format_int "$receive_timeouts") |
| Scheduler drift max | $(format_number "$scheduler_drift") ms |

## Diagnostic Findings

| Segment | Finding | Evidence |
|---------|---------|----------|
| RTT tail shape | $(verdict "$systemic_tail" "systemic broad pressure" "isolated or unclear tail") | global P95 and per-session P95-of-P95 gap ratio $(format_number "$(awk -v ratio="$rtt_gap_ratio" 'BEGIN { print ratio * 100 }')")% |
| Client pacing | $(verdict "$pacing_pressure" "pacing is actively throttling" "pacing is not the dominant visible signal") | avg wait $(format_number "$pacing_wait") ms, window $(format_int "$min_window")-$(format_int "$max_window") |
| Client outstanding depth | $(verdict "$client_outpacing" "client has broad outstanding backlog" "client backlog is not broad") | $(format_number "$pending_per_session") pending requests/session |
| Server send path | $(verdict "$server_send_pressure" "server send pressure is visible" "server send pressure is low in summary") | pending send $(format_int "$pending_send_requests"), backpressure $(format_int "$send_backpressure"), buffer $(format_int "$send_buffer_bytes") bytes |
| Socket pressure | $(verdict "$socket_pressure" "socket pressure is visible" "socket pressure is not visible") | NoBufferSpaceAvailable $(format_int "$no_buffer_errors"), receive timeouts $(format_int "$receive_timeouts") |
| Local scheduler | $(verdict "$scheduler_noise" "scheduler noise may affect interpretation" "scheduler drift is low") | max drift $(format_number "$scheduler_drift") ms |

## Recommended Next Lane

\`$next_lane\`

## Interpretation

- Treat this as a local same-machine decomposition, not a production capacity claim.
- The current artifact points to broad RTT pressure with active pacing and visible send/socket pressure.
- Before another engine optimization, add lower-risk client write/read duration metrics or run the same decomposition on a cloud split artifact when OCI A1 capacity becomes available.
MARKDOWN
