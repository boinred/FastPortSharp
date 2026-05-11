using FastPortDashboard.Maui.ViewModels;
using Microcharts;
using SkiaSharp;

namespace FastPortDashboard.Maui;

public partial class MainPage : ContentPage
{
	private readonly DashboardViewModel _viewModel;
	// Design Ref: §2 (dashboard-revert-skcanvasview-keep-data) — Microcharts ChartView 복귀.
	// SKCanvasView가 macOS 26 SwiftUI Observation crash trigger했으므로 안전한 ChartView 사용.
	// P50/P99 색상 상수는 향후 안전한 multi-line 방법 도입 시 재활용 위해 보존.
	private static readonly SKColor RttP50Color = SKColor.Parse("#2196F3");
	private static readonly SKColor RttP95Color = SKColor.Parse("#FF9800");
	private static readonly SKColor RttP99Color = SKColor.Parse("#F44336");
	// Design Ref: §3.2 (dashboard-throughput-chart) — Material green.
	private static readonly SKColor ThroughputLineColor = SKColor.Parse("#4CAF50");

	public MainPage()
	{
		InitializeComponent();

		_viewModel = new DashboardViewModel();
		BindingContext = _viewModel;

		_viewModel.ClientRttSeries.CollectionChanged += (_, _) => UpdateRttChart();
		_viewModel.ThroughputSeries.CollectionChanged += (_, _) => UpdateThroughputChart();
		UpdateRttChart();
		UpdateThroughputChart();
	}

	// Design Ref: §2 — ClientRttSeries는 TimedRttPoint(P50/P95/P99). 안전을 위해 P95만 시각화.
	private void UpdateRttChart()
	{
		var entries = _viewModel.ClientRttSeries
			.Select(p => new ChartEntry((float)p.P95Ms)
			{
				Label = string.Empty,
				ValueLabel = ((int)p.P95Ms).ToString(),
				Color = RttP95Color,
			})
			.ToArray();

		RttChartView.Chart = new LineChart
		{
			Entries = entries,
			LineMode = LineMode.Straight,
			LineSize = 2,
			PointMode = PointMode.None,
			BackgroundColor = SKColors.Transparent,
		};
	}

	// Design Ref: §3.2 (dashboard-throughput-chart) — UpdateRttChart mirror.
	private void UpdateThroughputChart()
	{
		var entries = _viewModel.ThroughputSeries
			.Select(p => new ChartEntry((float)p.Value)
			{
				Label = string.Empty,
				ValueLabel = ((long)p.Value).ToString(),
				Color = ThroughputLineColor,
			})
			.ToArray();

		ThroughputChartView.Chart = new LineChart
		{
			Entries = entries,
			LineMode = LineMode.Straight,
			LineSize = 2,
			PointMode = PointMode.None,
			BackgroundColor = SKColors.Transparent,
		};
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
