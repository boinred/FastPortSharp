namespace FastPortDashboard.Maui;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			// Design Ref: §2 (dashboard-chart-graphicsview-migration) — Microcharts/Skia 제거.
			// 차트는 LineChartDrawable + GraphicsView로 대체, 추가 handler 등록 불필요.
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		return builder.Build();
	}
}
