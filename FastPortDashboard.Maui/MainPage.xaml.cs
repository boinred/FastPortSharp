using FastPortDashboard.Maui.ViewModels;
using Microcharts;
using SkiaSharp;

namespace FastPortDashboard.Maui;

public partial class MainPage : ContentPage
{
	private readonly DashboardViewModel _viewModel;
	private static readonly SKColor RttLineColor = SKColor.Parse("#2196F3");
	// Design Ref: §3.2 (dashboard-throughput-chart) — RTT 파란색과 시각 구분 (Material green).
	private static readonly SKColor ThroughputLineColor = SKColor.Parse("#4CAF50");

	public MainPage()
	{
		InitializeComponent();

		_viewModel = new DashboardViewModel();
		BindingContext = _viewModel;

		// Design Ref: §3.6 (dashboard-rtt-chart) — code-behind에서 ChartEntry 변환.
		// Core ViewModel은 도메인 데이터(TimedDoublePoint) 유지, Microcharts 의존은 Maui 측에만.
		_viewModel.ClientRttSeries.CollectionChanged += (_, _) => UpdateRttChart();
		_viewModel.ThroughputSeries.CollectionChanged += (_, _) => UpdateThroughputChart();
		UpdateRttChart();
		UpdateThroughputChart();
	}

	private void UpdateRttChart()
	{
		var entries = _viewModel.ClientRttSeries
			.Select(p => new ChartEntry((float)p.Value)
			{
				Label = string.Empty,
				ValueLabel = ((int)p.Value).ToString(),
				Color = RttLineColor,
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
