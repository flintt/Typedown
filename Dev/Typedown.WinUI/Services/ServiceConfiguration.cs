using Microsoft.Extensions.DependencyInjection;
using Typedown.Contracts.Editor;
using Typedown.Contracts.Platform;
using Typedown.WinUI.Editor;
using Typedown.WinUI.Windowing;

namespace Typedown.WinUI.Services;

internal static class ServiceConfiguration
{
    public static ServiceProvider BuildProcessProvider(string appName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appName);

        var services = new ServiceCollection();

        services.AddSingleton<IAppDataPathProvider>(
            _ => new WinUIAppDataPathProvider(appName));
        services.AddSingleton<WindowManager>(provider => new WindowManager(
            provider.GetRequiredService<IServiceScopeFactory>(),
            appName));
        services.AddSingleton<WinUIWebViewEnvironmentService>();

        services.AddScoped<WindowRegistration>();
        services.AddScoped<IWindowContext>(
            provider => provider.GetRequiredService<WindowRegistration>().Context);
        services.AddScoped<IUiDispatcher>(
            provider => provider.GetRequiredService<WindowRegistration>().Dispatcher);
        services.AddScoped<IDialogService>(provider => new WinUIDialogService(
            provider.GetRequiredService<WindowRegistration>().Window));
        services.AddScoped<IFilePickerService>(provider => new WinUIFilePickerService(
            provider.GetRequiredService<WindowRegistration>().Window));
        services.AddScoped<WinUIEditorHost>();
        services.AddScoped<IMarkdownEditorBridge>(
            provider => provider.GetRequiredService<WinUIEditorHost>());

        services.AddTransient<RootPage>();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
    }
}
