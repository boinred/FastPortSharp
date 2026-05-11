namespace FastPortDashboard.Maui.ViewModels;

// Chart x축은 unix epoch ms, y축은 double value.
public readonly record struct TimedDoublePoint(double TimestampUnixMs, double Value);
