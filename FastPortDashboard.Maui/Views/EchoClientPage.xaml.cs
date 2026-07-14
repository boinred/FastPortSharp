// Design Ref: §5 — Echo Client tab page. Plan SC: FR-02, FR-04, FR-05, FR-07.
// LineChartDrawable single-series 재사용 (직전 cycle dashboard-chart-graphicsview-migration parity).
using FastPortDashboard.Maui.EchoClient;
using FastPortDashboard.Maui.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Maui.Graphics;

namespace FastPortDashboard.Maui.Views;

public partial class EchoClientPage : ContentPage
{
    private static readonly Color RttLineColor = Color.FromArgb("#2196F3");

    private readonly EchoClientViewModel _viewModel;
    private readonly LineChartDrawable _rttDrawable;

    public EchoClientPage()
    {
        InitializeComponent();

        // Design Ref: §11.2 — UI thread marshal: MAUI Dispatcher.Dispatch.
        var stats = new EchoClientStats();
        var connector = new EchoClientConnector(NullLoggerFactory.Instance);
        _viewModel = new EchoClientViewModel(connector, stats, postToUi: action =>
        {
            if (Dispatcher.IsDispatchRequired) Dispatcher.Dispatch(action);
            else action();
        });
        BindingContext = _viewModel;

        _rttDrawable = new LineChartDrawable
        {
            LineColor = RttLineColor,
            ValueFormat = "F2",
        };
        RttChartView.Drawable = _rttDrawable;

        _viewModel.RttSeries.CollectionChanged += (_, _) => UpdateRttChart();
        UpdateRttChart();
    }

    private void UpdateRttChart()
    {
        _rttDrawable.Values = _viewModel.RttSeries
            .Select(s => s.RttMs)
            .ToArray();
        RttChartView.Invalidate();
    }
}
