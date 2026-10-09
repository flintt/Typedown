using Newtonsoft.Json.Linq;
using Typedown.Automation;
using Typedown.Core.Services;
using Typedown.Services.Automation;
using Typedown.WinUI.Windowing;
using Windows.ApplicationModel;

namespace Typedown.WinUI.Automation;

/// <summary>
/// Owns the process-wide local automation endpoint. During the M3 smoke gate it
/// advertises only application status; later milestones add document, settings,
/// and view methods as their WinUI hosts become functional.
/// </summary>
internal sealed class WinUIAutomationService : IDisposable
{
    internal const string SettingName = "AllowLocalAutomation";
    private const long MaxMessageBytes = 32L * 1024 * 1024;

    private readonly JsonSettingsStore settings;
    private readonly WindowManager windowManager;
    private readonly ServerInfo serverInfo;
    private readonly AutomationServer server;
    private readonly object applyGate = new();
    private Task applying = Task.CompletedTask;
    private int isDisposed;

    public WinUIAutomationService(
        JsonSettingsStore settings,
        WindowManager windowManager)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.windowManager = windowManager
            ?? throw new ArgumentNullException(nameof(windowManager));

        var sid = SecurePipeListener.CurrentUserSid();
        var endpoint = AutomationEndpoint.PipeName(BuildTypes.Application, sid);
        serverInfo = new ServerInfo
        {
            Version = GetAppVersion(),
            Platform = "windows",
            BuildType = BuildTypes.Application,
            MaxMessageBytes = MaxMessageBytes,
        };
        server = new AutomationServer(
            () => new SecurePipeListener(endpoint, sid),
            CreateSession,
            serverInfo.MaxMessageBytes);
        server.ListenerFailed += exception =>
            System.Diagnostics.Debug.WriteLine(
                $"Automation endpoint stopped listening: {exception}");

        settings.Changed += OnSettingsChanged;
        Apply();
    }

    private AutomationSession CreateSession()
    {
        var methods = new MethodTable(BuildTypes.Application);
        methods.Add(new MethodDescriptor(
            "app.getState",
            Scopes.AppRead,
            "app.getState/1",
            (_, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var snapshot = windowManager.GetAutomationSnapshot();
                return Task.FromResult<JToken?>(new JObject
                {
                    ["instanceId"] = serverInfo.InstanceId,
                    ["version"] = serverInfo.Version,
                    ["activeWindowId"] = snapshot.ActiveWindowId is { } id
                        ? id
                        : JValue.CreateNull(),
                    ["windowCount"] = snapshot.WindowCount,
                });
            }));
        return new AutomationSession(serverInfo, methods);
    }

    private void OnSettingsChanged(string? setting, object? origin)
    {
        if (setting is null || setting == SettingName)
        {
            Apply();
        }
    }

    private void Apply()
    {
        var enabled = settings.Get(SettingName, false);
        lock (applyGate)
        {
            applying = applying.ContinueWith(
                async _ =>
                {
                    if (Volatile.Read(ref isDisposed) != 0)
                    {
                        return;
                    }

                    if (enabled && !server.IsRunning)
                    {
                        server.Start();
                    }
                    else if (!enabled && server.IsRunning)
                    {
                        await server.StopAsync();
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default).Unwrap();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref isDisposed, 1) != 0)
        {
            return;
        }

        settings.Changed -= OnSettingsChanged;
        server.StopAsync().GetAwaiter().GetResult();
    }

    private static string GetAppVersion()
    {
        try
        {
            var version = Package.Current.Id.Version;
            return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
        }
        catch
        {
            return typeof(App).Assembly.GetName().Version?.ToString() ?? string.Empty;
        }
    }
}
