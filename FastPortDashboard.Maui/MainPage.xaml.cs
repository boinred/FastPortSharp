using FastPortDashboard.Maui.ViewModels;
using FastPortDashboard.Maui.Views;
using Microsoft.Maui.Graphics;

namespace FastPortDashboard.Maui;

// Design Ref: §2 (dashboard-chart-graphicsview-migration) — SkiaSharp / Microcharts 의존 제거.
// Design Ref: §2 (dashboard-chart-multi-rtt-overlay-v2) — RTT는 MultiLineChartDrawable(P50/P95/P99),
// Throughput은 기존 LineChartDrawable single-line 보존.
public partial class MainPage : ContentPage
{
    private readonly DashboardViewModel _viewModel;
    private readonly MultiLineChartDrawable _rttMultiDrawable;
    private readonly LineChartDrawable _throughputDrawable;

    // Design Ref: §10 — RTT percentile 색상 + Throughput 색상 한 곳에 통합.
    private static readonly Color RttP50Color = Color.FromArgb("#2196F3");        // blue
    private static readonly Color RttP95Color = Color.FromArgb("#FF9800");        // orange
    private static readonly Color RttP99Color = Color.FromArgb("#F44336");        // red
    private static readonly Color ThroughputLineColor = Color.FromArgb("#4CAF50"); // green

    public MainPage()
    {
        InitializeComponent();

        _viewModel = new DashboardViewModel();
        BindingContext = _viewModel;

        _rttMultiDrawable = new MultiLineChartDrawable
        {
            ShowLegend = true,
        };
        _throughputDrawable = new LineChartDrawable
        {
            LineColor = ThroughputLineColor,
            ValueFormat = "F0",
        };

        RttChartView.Drawable = _rttMultiDrawable;
        ThroughputChartView.Drawable = _throughputDrawable;

        _viewModel.ClientRttSeries.CollectionChanged += (_, _) => UpdateRttChart();
        _viewModel.ThroughputSeries.CollectionChanged += (_, _) => UpdateThroughputChart();
        UpdateRttChart();
        UpdateThroughputChart();
    }

    // Design Ref: §2.2 — ClientRttSeries(TimedRttPoint) → 3 LineChartSeries snapshot.
    private void UpdateRttChart()
    {
        var points = _viewModel.ClientRttSeries.ToArray();
        _rttMultiDrawable.Series = new[]
        {
            new LineChartSeries(RttP50Color, points.Select(p => p.P50Ms).ToArray(), "P50"),
            new LineChartSeries(RttP95Color, points.Select(p => p.P95Ms).ToArray(), "P95"),
            new LineChartSeries(RttP99Color, points.Select(p => p.P99Ms).ToArray(), "P99"),
        };
        RttChartView.Invalidate();
    }

    // Design Ref: §2.2 — Throughput single-line (직전 cycle parity 유지).
    private void UpdateThroughputChart()
    {
        _throughputDrawable.Values = _viewModel.ThroughputSeries
            .Select(p => p.Value)
            .ToArray();
        ThroughputChartView.Invalidate();
    }

    private async void OnBrowseClicked(object? sender, EventArgs e)
    {
        try
        {
            FileResult? picked = await FilePicker.PickAsync(new PickOptions
            {
                PickerTitle = "Pick server.metrics.jsonl",
            });
            if (picked is not null)
            {
                _viewModel.FilePath = picked.FullPath;
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("File Picker Error", ex.Message, "OK");
        }
    }
}
