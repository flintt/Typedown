using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Typedown.WinUI.Services;
using Typedown.WinUI.Windowing;

namespace Typedown.WinUI;

public partial class App : Application
{
    private readonly ServiceProvider processServices;
    private readonly WindowManager windowManager;

    public App()
    {
        InitializeComponent();
        var appName = typeof(App).Assembly.GetName().Name
            ?? throw new InvalidOperationException("The application assembly has no name.");
        processServices = ServiceConfiguration.BuildProcessProvider(appName);
        windowManager = processServices.GetRequiredService<WindowManager>();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (windowManager.Count == 0)
        {
            windowManager.CreateWindow();
        }
    }
}
