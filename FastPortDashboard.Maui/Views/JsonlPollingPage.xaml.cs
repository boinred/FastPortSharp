// Design Ref: §10 Migration Step 1 — 기존 MainPage 콘텐츠를 무손실 이전.
// JSONL polling 동작은 100% 보존 (FR-08 회귀 0). 코드 의미 변화 없음, 단순 이동.
using FastPortDashboard.Maui.ViewModels;
using Microsoft.Maui.Graphics;

namespace FastPortDashboard.Maui.Views;

public partial class JsonlPollingPage : ContentPage
{
    private readonly DashboardViewModel _viewModel;
    private readonly MultiLineChartDrawable _rttMultiDrawable;
    private readonly LineChartDrawable _throughputDrawable;

    private static readonly Color RttP50Color = Color.FromArgb("#2196F3");
    private static readonly Color RttP95Color = Color.FromArgb("#FF9800");
    private static readonly Color RttP99Color = Color.FromArgb("#F44336");
    private static readonly Color ThroughputLineColor = Color.FromArgb("#4CAF50");

    public JsonlPollingPage()
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
