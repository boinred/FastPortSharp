# FastPortLoadRunner OS Limits

This document captures the operating-system limits that can affect large local load tests such as `--sessions 10000`.

## Why It Matters

10,000 client sessions can fail before the server is stressed if the runner machine runs out of:

- file descriptors or socket handles
- ephemeral ports
- TCP backlog capacity
- CPU time for the client runner itself
- memory for socket buffers and payload allocations

Treat 10,000 sessions as an environment-dependent target, not a guaranteed laptop default.

## Recommended Ramp-Up

Use progressive validation:

| Step | Sessions | Purpose |
|------|----------|---------|
| 1 | 1,000 | baseline sanity |
| 2 | 3,000 | observe client CPU/memory |
| 3 | 5,000 | validate socket limits |
| 4 | 10,000 | target load test |

Example:

```bash
dotnet run -c Release --project FastPortLoadRunner -- \
  --sessions 10000 \
  --payload random:4096-16384 \
  --rate 1 \
  --ramp-up 60s \
  --duration 5m \
  --metrics-interval 1s
```

## macOS Checks

Inspect file descriptor limits:

```bash
ulimit -n
```

For high-session tests, the effective limit should be comfortably above the target session count. If the value is too low, raise it in the shell/session used to start the server and runner.

Inspect ephemeral port range:

```bash
sysctl net.inet.ip.portrange.first
sysctl net.inet.ip.portrange.last
```

If server and runner are on the same machine, loopback port exhaustion can distort results.

## Linux Checks

Inspect file descriptor limits:

```bash
ulimit -n
```

Inspect ephemeral port range:

```bash
cat /proc/sys/net/ipv4/ip_local_port_range
```

Inspect TCP backlog-related settings:

```bash
sysctl net.core.somaxconn
sysctl net.ipv4.tcp_max_syn_backlog
```

## Windows Checks

On Windows, use PowerShell and system monitoring tools to watch:

- TCP dynamic port range
- handle count
- process memory
- CPU utilization
- network throughput

The dynamic port range can be inspected with:

```powershell
netsh int ipv4 show dynamicport tcp
```

## Runner Placement

For meaningful server results, prefer:

- server and runner on separate machines
- multiple runner processes when one runner becomes CPU-bound
- fixed server logging level for comparable runs
- no per-packet console logging during load

## Result Interpretation

If connection failures increase before server CPU or memory is saturated, suspect runner-side OS limits first. Increase limits or split the load across multiple runner processes/machines before changing server code.
