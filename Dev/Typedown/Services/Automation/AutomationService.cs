using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Typedown.Automation;
using Typedown.Core;
using Typedown.Core.Services;
using Typedown.Core.Utilities;

namespace Typedown.Services.Automation
{
    /// <summary>
    /// Local automation for the application (docs/automation-api-spec.md). Off by default: the "Allow local automation"
    /// setting starts the endpoint and turning it off closes the endpoint and every connection. While a client is
    /// connected every window says so in its title, and a write names the client and the document there briefly -
    /// neither can be turned off by a client.
    /// </summary>
    public static class AutomationService
    {
        public const string SettingName = "AllowLocalAutomation";
        private const int NoticeMs = 4000;

        private static AutomationServer server;
        private static JsonSettingsStore settings;
        private static readonly object gate = new();
        private static Task applying = Task.CompletedTask;

        public static void Initialize()
        {
            if (server != null) return;
            var sid = SecurePipeListener.CurrentUserSid();
            var info = new ServerInfo
            {
                Version = Core.Controls.AboutApp.GetAppVersion(),
                Commit = Config.TestBuild ?? "",
                Platform = "windows",
                BuildType = BuildTypes.Application,
                MaxMessageBytes = 32L * 1024 * 1024,
            };
            var host = new WindowsAutomationHost(info.Version);
            var name = AutomationEndpoint.PipeName(BuildTypes.Application, sid);
            server = new AutomationServer(
                () => new SecurePipeListener(name, sid),
                () => new AutomationSession(info, DocumentMethods.AddTo(new MethodTable(BuildTypes.Application), host, info.InstanceId, OnWrite)),
                info.MaxMessageBytes);
            server.ActivityChanged += OnActivityChanged;
            server.ListenerFailed += e => Log.Debug($"automation: the endpoint could not listen: {e.Message}");

            settings = JsonSettingsStore.Shared(Path.Combine(Config.GetLocalFolderPath(), "Settings.json"));
            settings.Changed += (setting, _) => { if (setting == null || setting == SettingName) Apply(); };
            Apply();
        }

        private static void Apply()
        {
            var on = settings.Get(SettingName, false);
            lock (gate)
                applying = applying.ContinueWith(async _ =>
                {
                    try
                    {
                        if (on && !server.IsRunning) { server.Start(); Log.Debug("automation: listening"); }
                        else if (!on && server.IsRunning) { await server.StopAsync(); Log.Debug("automation: stopped"); }
                    }
                    catch (Exception e) { Log.Debug($"automation: {e.Message}"); }
                }, TaskScheduler.Default).Unwrap();
        }

        public static Task ShutdownAsync() => server?.StopAsync() ?? Task.CompletedTask;

        private static void OnActivityChanged(AutomationActivity activity)
        {
            var connected = activity.ConnectionCount > 0;
            foreach (var window in AutomationWindows.Registry.Snapshot())
                _ = AutomationWindows.Registry.OnWindowAsync(window.WindowId, app => app.UIViewModel.AutomationConnected = connected)
                    .ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        }

        private static async void OnWrite(string client, string documentId)
        {
            try
            {
                var found = await AutomationWindows.FindDocumentAsync(documentId);
                if (found == null) return;
                var (window, tab) = found.Value;
                await AutomationWindows.Registry.OnWindowAsync(window.WindowId, app =>
                {
                    var notice = string.Format(Locale.GetString("AutomationWrote"), client, tab.Title);
                    app.UIViewModel.AutomationNotice = notice;
                    _ = Task.Delay(NoticeMs).ContinueWith(_ =>
                        AutomationWindows.Registry.OnWindowAsync(window.WindowId, a => { if (a.UIViewModel.AutomationNotice == notice) a.UIViewModel.AutomationNotice = null; return true; }),
                        TaskScheduler.Default);
                    return true;
                });
            }
            catch (Exception e)
            {
                Log.Debug($"automation: write notice: {e.Message}");
            }
        }
    }
}
