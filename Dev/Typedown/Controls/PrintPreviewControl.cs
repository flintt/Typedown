using Microsoft.Web.WebView2.Core;
using System;
using System.IO;
using System.Threading.Tasks;
using Typedown.Utilities;
using Typedown.XamlUI;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace Typedown.Controls
{
    public class PrintPreviewControl : UserControl, IDisposable
    {
        private readonly string html;

        private readonly string documentName;

        private WebViewController webViewController;

        private string tempFile;

        private bool disposed;

        /// <summary>
        /// The print dialog has been closed, printed or not. The preview used to stay on screen after it,
        /// showing the document as a plain page until its own close button was found; the page is told by
        /// the browser when the dialog goes (afterprint), and that is passed on here.
        /// </summary>
        public event Action PrintDismissed;

        public PrintPreviewControl(string html, string documentName)
        {
            this.html = html;
            this.documentName = documentName;
            Content = new Grid { Background = new SolidColorBrush(Colors.White) };
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (webViewController != null)
                    return;
                var controller = new WebViewController();
                var parentHWnd = XamlWindow.GetWindow(this).XamlSourceHandle;
                if (!await controller.InitializeAsync(this, parentHWnd))
                {
                    controller.Dispose();
                    return;
                }
                if (disposed)
                {
                    controller.Dispose();
                    return;
                }
                webViewController = controller;
                var coreWebView2 = webViewController.CoreWebView2;
                coreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                coreWebView2.Settings.AreDevToolsEnabled = false;
                coreWebView2.Settings.IsStatusBarEnabled = false;
                var navigationCompletedTaskSource = new TaskCompletionSource<bool>();
                coreWebView2.NavigationCompleted += (s, args) => navigationCompletedTaskSource.TrySetResult(args.IsSuccess);
                await coreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("window.addEventListener('afterprint', () => window.chrome.webview.postMessage('afterprint'))");
                coreWebView2.WebMessageReceived += (s, args) =>
                {
                    string message = null;
                    try { message = args.TryGetWebMessageAsString(); } catch { }
                    // Not from inside the browser's own callback: closing the preview disposes this web view.
                    if (message == "afterprint") _ = Dispatcher.RunIdleAsync(_ => PrintDismissed?.Invoke());
                };
                tempFile = Core.Utilities.Common.GetTempFileName(".html");
                File.WriteAllText(tempFile, html);
                coreWebView2.Navigate(tempFile);
                await navigationCompletedTaskSource.Task;
                if (disposed)
                    return;
                coreWebView2.ShowPrintUI(CoreWebView2PrintDialogKind.Browser);
            }
            catch
            {
                // Ignore
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            Dispose();
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            webViewController?.Dispose();
            webViewController = null;
            try
            {
                if (!string.IsNullOrEmpty(tempFile) && File.Exists(tempFile))
                    File.Delete(tempFile);
            }
            catch
            {
                // Ignore
            }
        }
    }
}
