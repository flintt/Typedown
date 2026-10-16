using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Typedown.Core.Utilities;

namespace Typedown.Utilities
{
    /// <summary>
    /// A WebView2 for the editor page and the print preview: WinUI 3's WebView2 control in a host panel, on the one
    /// browser environment the app shares (its data folder and arguments). On XAML Islands this was a composition-hosted
    /// CoreWebView2 with the pointer, focus and scale forwarded by hand; the control does all of that itself.
    /// </summary>
    public class WebViewController : IDisposable
    {
        private static Task<CoreWebView2Environment> environmentTask;

        public FrameworkElement Container { get; private set; }

        public WebView2 WebView { get; private set; }

        public CoreWebView2 CoreWebView2 => WebView?.CoreWebView2;

        /// <summary>
        /// The web view put first in <paramref name="host"/> (under anything laid over it), its core ready. Its own colour
        /// is the theme's page colour: WinUI 3's WebView2 cannot be transparent and filled what the page had not painted
        /// yet with a light grey of its own - a light frame in a dark window as the editor came up, and a light page
        /// under dark text after its browser process had ended and come back.
        /// </summary>
        public async Task<bool> InitializeAsync(Panel host, global::Windows.UI.Color background)
        {
            try
            {
                Container = host;
                var environment = await EnsureCreateEnvironment();
                WebView = new WebView2 { DefaultBackgroundColor = background };
                host.Children.Insert(0, WebView);
                await WebView.EnsureCoreWebView2Async(environment);
                StartupTiming.Reached("web view ready");
                return WebView.CoreWebView2 != null;
            }
            catch (Exception ex)
            {
                Log.WriteLocal("WebViewInitializeException", ex.ToString());
                _ = Log.Report("WebViewInitializeException", ex.ToString());
                return false;
            }
        }

        public static async Task<CoreWebView2Environment> EnsureCreateEnvironment()
        {
            var task = environmentTask;
            if (task == null)
            {
                var commandLineArgs = Core.Config.WebView2Args.ToList();
#if DEBUG
                commandLineArgs.Add("--remote-debugging-port=9222");
#endif
                var options = new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = string.Join(" ", commandLineArgs) };
                var userDataFolder = Path.Combine(Core.Config.LocalAppDataFolder, "WebView2");
                Directory.CreateDirectory(userDataFolder);
                environmentTask = task = CoreWebView2Environment.CreateWithOptionsAsync(null, userDataFolder, options).AsTask();
                var environment = await task;
                StartupTiming.Reached("web view environment");
                environment.BrowserProcessExited += OnEnvironmentBrowserProcessExited;
                return environment;
            }
            return await task;
        }

        // The browser gone (crashed, updated): the next web view starts a new one.
        private static void OnEnvironmentBrowserProcessExited(CoreWebView2Environment sender, CoreWebView2BrowserProcessExitedEventArgs e) => Forget(sender);

        /// <summary>
        /// The environment whose browser is gone let go of, so the next web view starts a new browser - also when a web
        /// view hears of it (ProcessFailed) before the environment does.
        /// </summary>
        public static void Forget(CoreWebView2Environment sender)
        {
            if (sender == null) return;
            var task = environmentTask;
            if (task != null && task.IsCompletedSuccessfully && task.Result == sender)
            {
                environmentTask = null;
                sender.BrowserProcessExited -= OnEnvironmentBrowserProcessExited;
            }
        }

        /// <summary>A web view drawn nowhere (exports render pages in one), parented to a window of its own.</summary>
        public static async Task<CoreWebView2Controller> CreateOffscreenController(nint parent)
        {
            var environment = await EnsureCreateEnvironment();
            return await environment.CreateCoreWebView2ControllerAsync(CoreWebView2ControllerWindowReference.CreateFromWindowHandle((ulong)parent));
        }

        public void EnsureFocus()
        {
            WebView?.Focus(FocusState.Programmatic);
        }

        public void Dispose()
        {
            try
            {
                if (WebView != null)
                {
                    (Container as Panel)?.Children.Remove(WebView);
                    WebView.Close();
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"webview: dispose: {ex.Message}");
            }
            WebView = null;
            Container = null;
        }
    }
}
