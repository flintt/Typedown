using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Typedown.Core.Controls;
using Typedown.Core.Utilities;
using Windows.Graphics;

namespace Typedown.Windows
{
    /// <summary>Offers to install the WebView2 runtime when it is missing: a small fixed window, centred.</summary>
    public class WebViewInstallWindow : Window
    {
        private readonly WebView2InstallControl webView2InstallControl = new();

        public WebViewInstallWindow()
        {
            Title = Locale.GetDialogString("WebView2RuntimeNotInstalledTitle");
            ExtendsContentIntoTitleBar = true;
            Content = webView2InstallControl;
            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
            }
            var scale = Math.Max(96, PInvoke.GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this))) / 96.0;
            var size = new SizeInt32((int)(500 * scale), (int)(300 * scale));
            var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
            AppWindow.MoveAndResize(new RectInt32(area.X + (area.Width - size.Width) / 2, area.Y + (area.Height - size.Height) / 2, size.Width, size.Height));
            webView2InstallControl.CloseButtonClick += (s, e) => Close();
            webView2InstallControl.InstallButtonClick += (s, e) => Install();
        }

        public static Task<bool> TryInstallWebView2()
        {
            var window = new WebViewInstallWindow();
            var task = new TaskCompletionSource<bool>();
            window.Closed += (s, e) =>
            {
                task.SetResult(EnvCheck.IsWebView2Installed());
            };
            window.Activate();
            return task.Task;
        }

        public async void Install()
        {
            AppWindow.Hide();
            var runDir = AppContext.BaseDirectory;
            try
            {
                var process = Process.Start(Path.Combine(runDir, "MicrosoftEdgeWebview2Setup.exe"));
                await Task.Run(() => process.WaitForExit());
            }
            catch (Exception ex)
            {
                Log.WriteLocal("WebViewInstall", ex.ToString());
            }
            Close();
        }
    }
}
