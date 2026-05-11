namespace FastPortDashboard.Maui.ViewModels;

// Design Ref: §3.1 (dashboard-multi-rtt-overlay) — RTT 3 percentile 묶음.
// 단일 collection으로 P50/P95/P99 overlay 표현. Throughput series는 TimedDoublePoint 유지.
public readonly record struct TimedRttPoint(
    double TimestampUnixMs,
    double P50Ms,
    double P95Ms,
    double P99Ms);
