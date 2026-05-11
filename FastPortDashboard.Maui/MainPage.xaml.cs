using FastPortDashboard.Maui.ViewModels;
using Microcharts;
using SkiaSharp;
using SkiaSharp.Views.Maui;

namespace FastPortDashboard.Maui;

public partial class MainPage : ContentPage
{
	private readonly DashboardViewModel _viewModel;
	// Design Ref: §3.3 (dashboard-multi-rtt-overlay) — RTT 3 percentile color-coded.
	// Material warning gradient: 정상(blue) → 주의(orange) → 위험(red).
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

		// Design Ref: §3.4 (dashboard-multi-rtt-overlay) — SKCanvasView invalidate-on-change.
		_viewModel.ClientRttSeries.CollectionChanged += (_, _) => RttCanvasView.InvalidateSurface();
		_viewModel.ThroughputSeries.CollectionChanged += (_, _) => UpdateThroughputChart();
		UpdateThroughputChart();
	}

	// Design Ref: §3.4 (dashboard-multi-rtt-overlay) — 3-line direct Skia draw + legend.
	private void OnRttCanvasPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
	{
		var canvas = e.Surface.Canvas;
		var info = e.Info;
		canvas.Clear(SKColors.Transparent);

		var series = _viewModel.ClientRttSeries;
		if (series.Count < 2) { return; }

		// Y축 max — P99 series 기준 (가장 위 line).
		float maxY = 0f;
		foreach (var p in series)
		{
			if (p.P99Ms > maxY) { maxY = (float)p.P99Ms; }
		}
		if (maxY <= 0f) { maxY = 1f; }
		maxY *= 1.1f;

		// Layout
		const float padLeft = 8f;
		const float padRight = 80f;
		const float padTop = 8f;
		const float padBottom = 8f;
		float chartW = info.Width - padLeft - padRight;
		float chartH = info.Height - padTop - padBottom;
		if (chartW <= 0 || chartH <= 0) { return; }

		float xStep = chartW / Math.Max(1, series.Count - 1);

		using var paint = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = 2f,
		};

		void DrawLine(Func<TimedRttPoint, double> getValue, SKColor color)
		{
			paint.Color = color;
			using var path = new SKPath();
			for (int i = 0; i < series.Count; i++)
			{
				float x = padLeft + i * xStep;
				float y = padTop + chartH - (float)(getValue(series[i]) / maxY) * chartH;
				if (i == 0) { path.MoveTo(x, y); }
				else { path.LineTo(x, y); }
			}
			canvas.DrawPath(path, paint);
		}

		DrawLine(p => p.P50Ms, RttP50Color);
		DrawLine(p => p.P95Ms, RttP95Color);
		DrawLine(p => p.P99Ms, RttP99Color);

		DrawLegend(canvas, info);
	}

	private static void DrawLegend(SKCanvas canvas, SKImageInfo info)
	{
		float legendX = info.Width - 76f;
		float legendY = 4f;
		const float boxSize = 10f;
		const float rowH = 16f;

		using var fillPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
		using var textPaint = new SKPaint
		{
			IsAntialias = true,
			Color = SKColors.Black,
			TextSize = 11f,
		};

		void Row(int idx, SKColor color, string label)
		{
			float y = legendY + idx * rowH;
			fillPaint.Color = color;
			canvas.DrawRect(legendX, y, boxSize, boxSize, fillPaint);
			canvas.DrawText(label, legendX + boxSize + 4f, y + boxSize - 1f, textPaint);
		}

		Row(0, RttP50Color, "P50");
		Row(1, RttP95Color, "P95");
		Row(2, RttP99Color, "P99");
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
