using Microsoft.UI.Xaml;

namespace Typedown.WinUI;

public partial class App : Application
{
    private Window? window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        window = new Window
        {
            Title = typeof(App).Assembly.GetName().Name ?? string.Empty,
            Content = new RootPage()
        };
        window.Activate();
    }
}
