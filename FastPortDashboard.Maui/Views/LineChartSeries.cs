using Microsoft.Maui.Graphics;

namespace FastPortDashboard.Maui.Views;

// Design Ref: §3.1 (dashboard-chart-multi-rtt-overlay-v2) — MultiLineChartDrawable의 입력 단위.
// record로 immutable + value equality. 매 update마다 새 인스턴스 할당.
public sealed record LineChartSeries(
    Color LineColor,
    IReadOnlyList<double> Values,
    string Label,
    float LineWidth = 2f);
