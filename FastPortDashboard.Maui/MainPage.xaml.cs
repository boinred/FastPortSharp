// Design Ref: §10 Migration — content moved to JsonlPollingPage; routing moved to AppShell TabBar.
// 이 클래스는 backward-compat 용으로만 남겨둠 (현 routing에서는 미사용).
namespace FastPortDashboard.Maui;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
    }
}
