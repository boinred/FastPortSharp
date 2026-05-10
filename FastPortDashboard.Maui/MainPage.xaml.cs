using FastPortDashboard.Maui.ViewModels;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace FastPortDashboard.Maui;

public partial class MainPage : ContentPage
{
	private readonly DashboardViewModel _viewModel;
	private readonly ObservableCollection<ObservablePoint> _throughputPoints = new();

	public MainPage()
	{
		InitializeComponent();

		_viewModel = new DashboardViewModel();
		BindingContext = _viewModel;

		// Bridge ViewModel.ThroughputSeries (TimedDoublePoint) ↔ chart points (ObservablePoint).
		_viewModel.ThroughputSeries.CollectionChanged += OnThroughputSeriesChanged;

		ThroughputChart.Series = new ISeries[]
		{
			new LineSeries<ObservablePoint>
			{
				Values = _throughputPoints,
				GeometrySize = 0,
				LineSmoothness = 0.3,
			}
		};

		ThroughputChart.XAxes = new[]
		{
			new Axis
			{
				Labeler = (value) => DateTimeOffset
					.FromUnixTimeMilliseconds((long)value)
					.LocalDateTime
					.ToString("HH:mm:ss"),
				LabelsRotation = 0,
			}
		};

		ThroughputChart.YAxes = new[]
		{
			new Axis
			{
				Labeler = (value) => $"{value:N0} B/s",
				MinLimit = 0,
			}
		};
	}

	private void OnThroughputSeriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		// Marshal back to UI thread (chart 갱신은 UI thread만 안전).
		MainThread.BeginInvokeOnMainThread(() =>
		{
			if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is not null)
			{
				foreach (TimedDoublePoint p in e.NewItems)
				{
					_throughputPoints.Add(new ObservablePoint(p.TimestampUnixMs, p.Value));
				}
			}
			else if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems is not null)
			{
				for (int i = 0; i < e.OldItems.Count && _throughputPoints.Count > 0; i++)
				{
					_throughputPoints.RemoveAt(0);
				}
			}
			else if (e.Action == NotifyCollectionChangedAction.Reset)
			{
				_throughputPoints.Clear();
			}
		});
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
