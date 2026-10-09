using Microsoft.Web.WebView2.Core;
using Typedown.Contracts.Platform;

namespace Typedown.WinUI.Editor;

/// <summary>
/// Owns the process-wide WebView2 environment. Every window uses the same user
/// data directory and browser switches, as required by WebView2.
/// </summary>
public sealed class WinUIWebViewEnvironmentService
{
    private static readonly string[] BrowserArguments =
    {
        // The current Muya bundle resolves local images to file URLs. Keep the
        // stable host behavior until image requests move to a scoped handler.
        "--disable-web-security",
        "--allow-file-access-from-files",
#if MODE_DEBUG
        "--remote-debugging-port=9222",
#endif
        "--flag-switches-begin",
        "--enable-features=msOverlayScrollbarWinStyle",
        "--flag-switches-end",
    };

    private readonly object environmentLock = new();
    private readonly IAppDataPathProvider pathProvider;
    private Task<CoreWebView2Environment>? environmentTask;

    public WinUIWebViewEnvironmentService(IAppDataPathProvider pathProvider)
    {
        this.pathProvider = pathProvider
            ?? throw new ArgumentNullException(nameof(pathProvider));
    }

    public Task<CoreWebView2Environment> GetEnvironmentAsync()
    {
        lock (environmentLock)
        {
            environmentTask ??= CreateEnvironmentAsync();
            return environmentTask;
        }
    }

    private async Task<CoreWebView2Environment> CreateEnvironmentAsync()
    {
        var userDataFolder = Path.Combine(pathProvider.CacheDirectory, "WebView2");
        Directory.CreateDirectory(userDataFolder);
        var options = new CoreWebView2EnvironmentOptions
        {
            AdditionalBrowserArguments = string.Join(' ', BrowserArguments),
        };
        var environment = await CoreWebView2Environment.CreateWithOptionsAsync(
            browserExecutableFolder: null,
            userDataFolder: userDataFolder,
            options: options);
        environment.BrowserProcessExited += OnBrowserProcessExited;
        return environment;
    }

    private void OnBrowserProcessExited(
        object? sender,
        CoreWebView2BrowserProcessExitedEventArgs args)
    {
        lock (environmentLock)
        {
            if (environmentTask?.IsCompletedSuccessfully != true
                || !ReferenceEquals(environmentTask.Result, sender))
            {
                return;
            }

            environmentTask.Result.BrowserProcessExited -= OnBrowserProcessExited;
            environmentTask = null;
        }
    }
}
