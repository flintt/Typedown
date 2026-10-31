using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Typedown.Core.Controls;
using Typedown.Core.Utilities;
using Typedown.Services.Automation;
using Windows.Graphics;

namespace Typedown.Windows
{
    /// <summary>
    /// Offers to install the WebView2 runtime when it is missing: a small fixed window, centred. It stays on screen while
    /// the setup runs, saying so (it was hidden and shown again; on Windows 10 it was found blank, E2E WV02/WV03). A setup that did
    /// not install the runtime (offline, elevation refused) leaves the window saying so, with Microsoft's download page;
    /// one that did lets the start go on to the main window.
    /// </summary>
    public class WebViewInstallWindow : Window
    {
        private readonly WebView2InstallControl webView2InstallControl = new();

        private readonly TaskCompletionSource<bool> result = new(TaskCreationOptions.RunContinuationsAsynchronously);

        // After a setup that installed the runtime the window stays until the main window is up: the last window closed
        // ends the app (the dispatcher shuts down with it), and the start would end with nothing on screen.
        private static WebViewInstallWindow kept;

        public WebViewInstallWindow()
        {
            Title = Locale.GetDialogString("WebView2RuntimeNotInstalledTitle");
            ExtendsContentIntoTitleBar = true;
            Content = webView2InstallControl;
            // The interface's language tag, where one was applied already (UiLanguage); else the system's default stays.
            UiLanguage.Set(webView2InstallControl);
            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
            }
            var scale = Math.Max(96, PInvoke.GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this))) / 96.0;
            var size = new SizeInt32((int)(500 * scale), (int)(340 * scale));
            var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
            AppWindow.MoveAndResize(new RectInt32(area.X + (area.Width - size.Width) / 2, area.Y + (area.Height - size.Height) / 2, size.Width, size.Height));
            webView2InstallControl.CloseButtonClick += (s, e) => Close();
            webView2InstallControl.InstallButtonClick += (s, e) => Install();
            webView2InstallControl.Loaded += (s, e) => Log.Debug($"webview2 setup: window content loaded, {webView2InstallControl.ActualWidth:0}x{webView2InstallControl.ActualHeight:0}");
            Closed += (s, e) =>
            {
                closed = true;
                result.TrySetResult(EnvCheck.IsWebView2Installed());
            };
        }

        /// <summary>True once the runtime is there (the start goes on), false when the window was closed without it.</summary>
        public static Task<bool> TryInstallWebView2()
        {
            var window = new WebViewInstallWindow();
            window.Activate();
            Log.Debug("webview2 setup: window shown");
            return window.result.Task;
        }

        /// <summary>The window kept after a setup that installed the runtime, closed once the main window is up.</summary>
        public static void CloseKept()
        {
            var window = kept;
            kept = null;
            try { window?.Close(); }
            catch (Exception ex) { Log.Debug($"webview2 setup: closing its window: {ex.Message}"); }
        }

        private bool closed;

        public async void Install()
        {
            if (webView2InstallControl.IsInstalling) return;
            Log.Debug("webview2 setup: install pressed");
            webView2InstallControl.IsInstallFailedShown = false;
            webView2InstallControl.IsInstalling = true;
            var dispatcher = DispatcherQueue;
            // The setup waited for on the thread pool; what follows is put back on the window's thread explicitly.
            var outcome = await Task.Run(() => RunSetup());
            dispatcher.TryEnqueue(() => OnSetupEnded(outcome));
        }

        private static string RunSetup()
        {
            try
            {
                // Through the shell: a setup that asks for elevation gets the prompt instead of failing to start.
                var start = TestHostHooks.WebView2SetupStandIn()
                    ?? new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "MicrosoftEdgeWebview2Setup.exe")) { UseShellExecute = true };
                using var process = Process.Start(start);
                process.WaitForExit();
                return $"exit code {process.ExitCode}";
            }
            catch (Exception ex)
            {
                // Elevation refused, the setup missing from the folder: the window says so and offers the download.
                Log.WriteLocal("WebViewInstall", ex.ToString());
                return ex.Message;
            }
        }

        private void OnSetupEnded(string outcome)
        {
            TestHostHooks.OnWebView2SetupExited();
            // Closed while the setup ran: the start already went on (or ended) with what the runtime was then.
            if (closed) return;
            webView2InstallControl.IsInstalling = false;
            if (EnvCheck.IsWebView2Installed())
            {
                Log.Debug($"webview2 setup: {outcome}, the runtime is installed");
                kept = this;
                result.TrySetResult(true);
                return;
            }
            Log.Debug($"webview2 setup: {outcome}, the runtime is still not installed");
            webView2InstallControl.IsInstallFailedShown = true;
        }
    }
}
