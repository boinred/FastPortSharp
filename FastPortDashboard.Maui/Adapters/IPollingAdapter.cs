using LibTestTelemetry;

namespace FastPortDashboard.Maui.Adapters;

// Design Ref: §3.1 — telemetry stream 추상화. Mock과 Jsonl 두 구현이 같은 contract.
// 다음 cycle (HTTP/SignalR 같은 push 기반 source)도 같은 인터페이스로 추가 가능.
public interface IPollingAdapter
{
    // 1초 단위로 ObservedMetricsSnapshot을 yield. cancellationToken으로 즉시 종료.
    IAsyncEnumerable<ObservedMetricsSnapshot> StreamAsync(CancellationToken ct);
}
