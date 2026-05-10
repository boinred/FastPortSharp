namespace FastPortDashboard.Maui;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
	}

	// .NET 10 MAUI: MainPage = ... is deprecated; use CreateWindow override.
	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new AppShell());
	}
}
