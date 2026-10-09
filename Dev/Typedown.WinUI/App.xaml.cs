using Microsoft.UI.Xaml;
using Typedown.WinUI.Windowing;

namespace Typedown.WinUI;

public partial class App : Application
{
    private readonly WindowManager windowManager;

    public App()
    {
        InitializeComponent();
        windowManager = new WindowManager(
            _ => new RootPage(),
            typeof(App).Assembly.GetName().Name ?? string.Empty);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (windowManager.Count == 0)
        {
            windowManager.CreateWindow();
        }
    }
}
