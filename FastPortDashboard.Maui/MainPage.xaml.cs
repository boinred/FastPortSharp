using FastPortDashboard.Maui.ViewModels;
using FastPortDashboard.Maui.Views;
using Microsoft.Maui.Graphics;

namespace FastPortDashboard.Maui;

// Design Ref: §2 (dashboard-chart-graphicsview-migration) — SkiaSharp / Microcharts 의존 제거.
// 두 GraphicsView는 LineChartDrawable 인스턴스에 snapshot list를 주입하고 Invalidate()로 재렌더.
public partial class MainPage : ContentPage
{
    private readonly DashboardViewModel _viewModel;
    private readonly LineChartDrawable _rttDrawable;
    private readonly LineChartDrawable _throughputDrawable;

    // Design Ref: §5.4 — 시각화 parity를 위해 기존 Material 색상 유지.
    private static readonly Color RttP95Color = Color.FromArgb("#FF9800");
    private static readonly Color ThroughputLineColor = Color.FromArgb("#4CAF50");

    public MainPage()
    {
        InitializeComponent();

        _viewModel = new DashboardViewModel();
        BindingContext = _viewModel;

        _rttDrawable = new LineChartDrawable
        {
            LineColor = RttP95Color,
            ValueFormat = "F0",
        };
        _throughputDrawable = new LineChartDrawable
        {
            LineColor = ThroughputLineColor,
            ValueFormat = "F0",
        };

        RttChartView.Drawable = _rttDrawable;
        ThroughputChartView.Drawable = _throughputDrawable;

        _viewModel.ClientRttSeries.CollectionChanged += (_, _) => UpdateRttChart();
        _viewModel.ThroughputSeries.CollectionChanged += (_, _) => UpdateThroughputChart();
        UpdateRttChart();
        UpdateThroughputChart();
    }

    // Design Ref: §2.2 — ClientRttSeries(TimedRttPoint) → P95 snapshot → Invalidate.
    private void UpdateRttChart()
    {
        _rttDrawable.Values = _viewModel.ClientRttSeries
            .Select(p => p.P95Ms)
            .ToArray();
        RttChartView.Invalidate();
    }

    // Design Ref: §2.2 — Throughput mirror.
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
