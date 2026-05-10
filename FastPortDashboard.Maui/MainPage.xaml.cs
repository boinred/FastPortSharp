using FastPortDashboard.Maui.ViewModels;

namespace FastPortDashboard.Maui;

public partial class MainPage : ContentPage
{
	private readonly DashboardViewModel _viewModel;

	public MainPage()
	{
		InitializeComponent();

		_viewModel = new DashboardViewModel();
		BindingContext = _viewModel;
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
