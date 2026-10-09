using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Typedown.Contracts.Editor;

namespace Typedown.WinUI.Editor;

/// <summary>
/// Hosts the existing Muya page and carries its wire messages unchanged. The
/// window-scoped protocol session subscribes through <see cref="IMarkdownEditorBridge"/>.
/// </summary>
public sealed class WinUIEditorHost : UserControl, IMarkdownEditorBridge
{
    private readonly WinUIWebViewEnvironmentService environmentService;
    private readonly WebView2 webView;
    private Task? initializationTask;
    private Uri? editorUri;
    private bool coreEventsAttached;
    private int isDisposed;

    public WinUIEditorHost(WinUIWebViewEnvironmentService environmentService)
    {
        this.environmentService = environmentService
            ?? throw new ArgumentNullException(nameof(environmentService));
        webView = new WebView2
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        Content = webView;
        Loaded += OnLoaded;
    }

    public EditorBridgeState State { get; private set; }

    public event EventHandler<EditorRawMessageReceivedEventArgs>? RawMessageReceived;

    public event EventHandler<EditorBridgeStateChangedEventArgs>? StateChanged;

    public bool TryPostJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || Volatile.Read(ref isDisposed) != 0)
        {
            return false;
        }

        try
        {
            var core = webView.CoreWebView2;
            if (core is null)
            {
                return false;
            }

            core.PostWebMessageAsString(json);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Focus()
    {
        if (Volatile.Read(ref isDisposed) == 0)
        {
            webView.Focus(FocusState.Programmatic);
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (Volatile.Read(ref isDisposed) != 0)
        {
            return;
        }

        try
        {
            initializationTask ??= InitializeAsync();
            await initializationTask;
        }
        catch (Exception exception)
        {
            initializationTask = null;
            SetState(EditorBridgeState.Faulted, exception.Message);
            System.Diagnostics.Debug.WriteLine(
                $"WinUI editor initialization failed: {exception}");
        }
    }

    private async Task InitializeAsync()
    {
        SetState(EditorBridgeState.Initializing);
        var indexPath = Path.Combine(
            AppContext.BaseDirectory,
            "Resources", "Statics", "index.html");
        if (!File.Exists(indexPath))
        {
            throw new FileNotFoundException(
                "The packaged editor bundle is missing.",
                indexPath);
        }

        editorUri = new Uri(indexPath);
        var environment = await environmentService.GetEnvironmentAsync();
        await webView.EnsureCoreWebView2Async(environment);
        AttachCoreEvents();
        ConfigureCore(webView.CoreWebView2);
        SetState(EditorBridgeState.Navigating);
        webView.CoreWebView2.Navigate(new Uri(indexPath).AbsoluteUri);
    }

    private static void ConfigureCore(CoreWebView2 core)
    {
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDefaultScriptDialogsEnabled = false;
#if MODE_DEBUG
        core.Settings.AreDevToolsEnabled = true;
#else
        core.Settings.AreDevToolsEnabled = false;
#endif
        core.Settings.IsBuiltInErrorPageEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.IsPinchZoomEnabled = true;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsSwipeNavigationEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
    }

    private void AttachCoreEvents()
    {
        if (coreEventsAttached)
        {
            return;
        }

        var core = webView.CoreWebView2;
        core.WebMessageReceived += OnWebMessageReceived;
        core.NavigationStarting += OnNavigationStarting;
        core.NavigationCompleted += OnNavigationCompleted;
        core.ProcessFailed += OnProcessFailed;
        core.NewWindowRequested += OnNewWindowRequested;
        coreEventsAttached = true;
    }

    private void DetachCoreEvents()
    {
        if (!coreEventsAttached || webView.CoreWebView2 is not { } core)
        {
            return;
        }

        core.WebMessageReceived -= OnWebMessageReceived;
        core.NavigationStarting -= OnNavigationStarting;
        core.NavigationCompleted -= OnNavigationCompleted;
        core.ProcessFailed -= OnProcessFailed;
        core.NewWindowRequested -= OnNewWindowRequested;
        coreEventsAttached = false;
    }

    private void OnWebMessageReceived(
        CoreWebView2 sender,
        CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            var json = args.TryGetWebMessageAsString();
            if (!string.IsNullOrWhiteSpace(json))
            {
                RawMessageReceived?.Invoke(
                    this,
                    new EditorRawMessageReceivedEventArgs(json));
            }
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"WinUI editor message was dropped: {exception.Message}");
        }
    }

    private void OnNavigationStarting(
        CoreWebView2 sender,
        CoreWebView2NavigationStartingEventArgs args)
    {
        if (editorUri is null
            || !Uri.TryCreate(args.Uri, UriKind.Absolute, out var destination)
            || !destination.IsFile
            || !string.Equals(
                Path.GetFullPath(destination.LocalPath),
                Path.GetFullPath(editorUri.LocalPath),
                StringComparison.OrdinalIgnoreCase))
        {
            args.Cancel = true;
            return;
        }

        SetState(EditorBridgeState.Navigating);
    }

    private void OnNavigationCompleted(
        CoreWebView2 sender,
        CoreWebView2NavigationCompletedEventArgs args)
    {
        if (args.IsSuccess)
        {
            SetState(EditorBridgeState.Ready);
        }
        else
        {
            SetState(
                EditorBridgeState.Faulted,
                $"Editor navigation failed: {args.WebErrorStatus}");
        }
    }

    private void OnProcessFailed(
        CoreWebView2 sender,
        CoreWebView2ProcessFailedEventArgs args)
    {
        SetState(
            EditorBridgeState.Faulted,
            $"WebView2 process failed: {args.ProcessFailedKind} ({args.Reason})");
    }

    private static void OnNewWindowRequested(
        CoreWebView2 sender,
        CoreWebView2NewWindowRequestedEventArgs args)
    {
        // External navigation is routed by the protocol session once it is attached.
        args.Handled = true;
    }

    private void SetState(EditorBridgeState state, string? error = null)
    {
        if (State == state && error is null)
        {
            return;
        }

        var previous = State;
        State = state;
        StateChanged?.Invoke(
            this,
            new EditorBridgeStateChangedEventArgs(previous, state, error));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref isDisposed, 1) != 0)
        {
            return;
        }

        Loaded -= OnLoaded;
        DetachCoreEvents();
        SetState(EditorBridgeState.Disposed);
        GC.SuppressFinalize(this);
    }
}
