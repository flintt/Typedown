using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Web;
using Typedown.Core.Controls;
using Typedown.Core.Interfaces;
using Typedown.Core.Services;
using Typedown.Core.Utilities;
using Typedown.Core.ViewModels;
using Typedown.Services;
using Typedown.Utilities;
using Windows.Foundation;
using Windows.System;
using Windows.UI;
using Windows.UI.ViewManagement;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Typedown.Controls
{
    public partial class MarkdownEditor : UserControl, IMarkdownEditor, IDisposable, INotifyPropertyChanged
    {
        public WebViewController WebViewController { get; private set; }

        public CoreWebView2 CoreWebView2 => WebViewController?.CoreWebView2;

        public EventCenter EventCenter => ServiceProvider.GetService<EventCenter>();

        public RemoteInvoke RemoteInvoke => ServiceProvider.GetService<RemoteInvoke>();

        public AppViewModel AppViewModel => ServiceProvider.GetService<AppViewModel>();

        public Transport Transport => ServiceProvider.GetService<Transport>();

        public bool IsEditorLoadFailed { get; set; }

        public bool IsEditorLoaded { get; set; }

        public IServiceProvider ServiceProvider { get; }

        private readonly Rectangle dummyRectangle = new();

        // Laid over the web view for anchoring flyouts at a point of the page (dummyRectangle); not hit-tested: the web view
        // under it takes the pointer itself.
        private readonly Canvas canvas = new() { IsHitTestVisible = false };

        private readonly Grid root = new();

        private readonly UISettings uiSettings = new();

        // Over the page while a long document is being drawn (EditorViewModel.LongLoadPending): the theme's colour, a ring
        // and "Opening…", instead of an empty page for a second or more.
        private readonly Microsoft.UI.Xaml.Controls.Grid opening = new() { Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        private readonly Microsoft.UI.Xaml.Controls.TextBlock openingText = new() { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) };
        private Microsoft.UI.Dispatching.DispatcherQueueTimer openingDelay;
        private readonly StackPanel openingContent;
        // The cover over a very long document being left (EditorViewModel.LeavingCover): the same layer, its colour only.
        private bool covering;
        private Microsoft.UI.Dispatching.DispatcherQueueTimer coverLimit;

        private readonly CompositeDisposable disposables = new();

        public MarkdownEditor(IServiceProvider serviceProvider)
        {
            ServiceProvider = serviceProvider;
            Loaded += OnLoaded;
            canvas.Children.Add(dummyRectangle);
            root.Children.Add(canvas);
            openingContent = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            openingContent.Children.Add(new Microsoft.UI.Xaml.Controls.ProgressRing { IsActive = true, Width = 28, Height = 28 });
            openingContent.Children.Add(openingText);
            opening.Children.Add(openingContent);
            root.Children.Add(opening);
            // Above the web view, which is added to the same grid later.
            Canvas.SetZIndex(opening, 10);
            Content = root;
            IsTabStop = true;
            // Where the keyboard goes when it leaves or comes to the editor, in the log: a key lost in between shows up
            // there (E2E TL01).
            LosingFocus += (_, e) => Core.Utilities.Log.Debug($"focus: from the editor ({e.OldFocusedElement?.GetType().Name}) to {e.NewFocusedElement?.GetType().Name} '{(e.NewFocusedElement as FrameworkElement)?.Name}', {e.FocusState}");
            GettingFocus += (_, e) => Core.Utilities.Log.Debug($"focus: to the editor ({e.NewFocusedElement?.GetType().Name}) from {e.OldFocusedElement?.GetType().Name} '{(e.OldFocusedElement as FrameworkElement)?.Name}', {e.FocusState}");
            // Characters that reach this control instead of the page: the keyboard was given to the web view, but it takes
            // it only through the window's thread, busy for a while after a tab with a long document was clicked; typed
            // meanwhile, they came here and were lost (E2E TL01). Passed on in order to the page, which keeps them for
            // the document shown. Once the page has the keyboard, typing goes to it directly and never comes here.
            CharacterReceived += (_, e) =>
            {
                var ch = e.Character;
                if (!IsEditorLoaded || (ch != '\r' && char.IsControl(ch))) return;
                e.Handled = true;
                PostMessage("TypedElsewhere", new { text = ch == '\r' ? "\n" : ch.ToString() });
            };
            disposables.Add(AppViewModel.EditorViewModel.WhenPropertyChanged(nameof(EditorViewModel.LongLoadPending)).Subscribe(_ => UpdateOpening()));
            disposables.Add(AppViewModel.EditorViewModel.WhenPropertyChanged(nameof(EditorViewModel.LeavingCover)).Subscribe(_ => UpdateCover()));
            disposables.Add(RemoteInvoke.Handle("ContentLoaded", OnContentLoaded));
            disposables.Add(RemoteInvoke.Handle<string>("UnhandledException", OnUnhandledException));
            disposables.Add(RemoteInvoke.Handle("GetCurrentTheme", () => ServiceProvider.GetCurrentTheme()));
            disposables.Add(RemoteInvoke.Handle<string>("OpenNewWindow", OpenNewWindow));
            disposables.Add(AppViewModel.UIViewModel.WhenPropertyChanged(nameof(UIViewModel.ActualTheme))
                .Merge(AppViewModel.SettingsViewModel.WhenPropertyChanged(nameof(SettingsViewModel.UseEditorMicaEffect)))
                .Merge(AppViewModel.SettingsViewModel.WhenPropertyChanged(nameof(SettingsViewModel.UseMicaEffect)))
                .Merge(AppViewModel.SettingsViewModel.WhenPropertyChanged(nameof(SettingsViewModel.AppTheme)))
                .Merge(AppViewModel.SettingsViewModel.WhenPropertyChanged(nameof(SettingsViewModel.CustomTheme)))
                .Merge(uiSettings.GetColorValuesObservable())
                .Subscribe(_ => OnThemeChanged()));
            // The editor holds a few strings of its own (the placeholders in footnotes, code fences and images),
            // fetched once when it starts. A language change is the one thing that leaves them behind.
            disposables.Add(AppViewModel.SettingsViewModel.WhenPropertyChanged(nameof(SettingsViewModel.Language))
                .Subscribe(_ => _ = DispatcherQueue.RunIdleAsync(() => PostMessage("LanguageChanged", null))));
        }

        private static readonly HashSet<ulong> reported = new();

        // When the page fell over: three times in a minute is a document the visual editor cannot draw, and reloading it
        // again only fell over again, without end.
        private readonly Queue<DateTime> crashes = new();

        private void OnUnhandledException(string error)
        {
            var now = DateTime.UtcNow;
            crashes.Enqueue(now);
            while (crashes.Count > 0 && now - crashes.Peek() > TimeSpan.FromMinutes(1)) crashes.Dequeue();
            if (crashes.Count >= 3 && AppViewModel?.SettingsViewModel is { SourceCode: false } settings)
            {
                // Source mode does not go through what fell over: the document stays there to be edited.
                Log.Debug($"editor page: fell over {crashes.Count} times in a minute; reloading in source mode");
                crashes.Clear();
                settings.SourceCode = true;
            }
            CoreWebView2.Reload();
            try
            {
                var hash = Core.Utilities.Common.SimpleHash(error);
                if (!reported.Contains(hash))
                {
                    reported.Add(hash);
                    Log.Report("WebViewUnhandledException", error);
                }
            }
            catch
            {
                // Ignore
            }
        }

        // The control's own colour under a custom theme is the theme's page colour: while the page is hidden - loading, or
        // its document still being drawn - that is what shows, not the window's light grey behind it. Under the app's own
        // themes it stays clear, showing the window's colour as before: read from the app's light or dark state, it was
        // light for a dark window just opened, whose state had not caught up yet (E2E SF01).
        private void UpdateBackdrop()
        {
            var custom = ThemeFiles.Find(AppViewModel.SettingsViewModel.CustomTheme);
            if (ThemeFiles.Brush(custom?.Background) is Microsoft.UI.Xaml.Media.SolidColorBrush brush) root.Background = brush;
            else root.ClearValue(Microsoft.UI.Xaml.Controls.Panel.BackgroundProperty);
            // What the web view shows where its page has painted nothing yet - the moment it is shown, before the page's
            // first frame - is its default colour: the page's own. Transparent, it showed black or its own white there,
            // between the theme's colour before and the document after (a Sepia start-up went Sepia, black, document).
            if (WebViewController?.WebView is { } webView) webView.DefaultBackgroundColor = printing ? Microsoft.UI.Colors.White : ServiceProvider.GetPageBackground();
        }

        // Printed: the print preview's margins are the web view's own colour (the theme's, above) - a dark theme's around
        // the white page. White while the page is printed, the theme's again after.
        private bool printing;

        public void SetPrinting(bool printing)
        {
            if (this.printing == printing) return;
            this.printing = printing;
            Core.Utilities.Log.Debug($"editor: printing {(printing ? "starts" : "ends")}");
            UpdateBackdrop();
        }

        // Shown once a long load has taken a moment (a short one never flashes it), gone when the page confirms it, and in
        // any case after ten seconds.
        private void UpdateOpening()
        {
            openingDelay?.Stop();
            if (!AppViewModel.EditorViewModel.LongLoadPending)
            {
                // The cover over a document left stays until the next one is on the page.
                if (!covering) opening.Visibility = Visibility.Collapsed;
                else openingContent.Visibility = Visibility.Collapsed;
                return;
            }
            openingDelay ??= DispatcherQueue.CreateTimer();
            openingDelay.IsRepeating = false;
            openingDelay.Interval = TimeSpan.FromMilliseconds(AppViewModel.EditorViewModel.LongLoadDelay);
            openingDelay.Tick += OnOpeningDelay;
            openingDelay.Start();
        }

        // At once, in the page's colour, nothing on it ("Opening…" follows as for any long load); gone when the next document
        // is on the page, and in any case after ten seconds.
        private void UpdateCover()
        {
            if (AppViewModel.EditorViewModel.LeavingCover)
            {
                covering = true;
                if (opening.Visibility != Visibility.Visible)
                {
                    openingContent.Visibility = Visibility.Collapsed;
                    opening.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(ServiceProvider.GetPageBackground());
                    opening.Visibility = Visibility.Visible;
                }
                coverLimit ??= DispatcherQueue.CreateTimer();
                coverLimit.IsRepeating = false;
                coverLimit.Interval = TimeSpan.FromSeconds(10);
                coverLimit.Tick -= OnCoverLimit;
                coverLimit.Tick += OnCoverLimit;
                coverLimit.Start();
                return;
            }
            coverLimit?.Stop();
            if (!covering) return;
            covering = false;
            // "Opening…" taken over meanwhile (a long document coming): it stays, with its ring and text.
            if (openingContent.Visibility != Visibility.Visible) opening.Visibility = Visibility.Collapsed;
        }

        private void OnCoverLimit(Microsoft.UI.Dispatching.DispatcherQueueTimer timer, object args)
        {
            if (!covering) return;
            covering = false;
            if (openingContent.Visibility != Visibility.Visible) opening.Visibility = Visibility.Collapsed;
        }

        private DateTime openingShownAt;

        private void OnOpeningDelay(Microsoft.UI.Dispatching.DispatcherQueueTimer timer, object args)
        {
            timer.Tick -= OnOpeningDelay;
            if (!AppViewModel.EditorViewModel.LongLoadPending) return;
            if (opening.Visibility == Visibility.Visible && DateTime.UtcNow - openingShownAt > TimeSpan.FromSeconds(10))
            {
                opening.Visibility = Visibility.Collapsed;
                return;
            }
            if (opening.Visibility != Visibility.Visible || openingContent.Visibility != Visibility.Visible)
            {
                openingContent.Visibility = Visibility.Visible;
                openingShownAt = DateTime.UtcNow;
                AppViewModel.EditorViewModel.OpeningShown++;
                Core.Utilities.Log.Debug($"editor: \"Opening…\" shown for load {AppViewModel.EditorViewModel.LoadId}");
                openingText.Text = Locale.GetString("EditorOpening");
                var theme = ThemeFiles.Find(AppViewModel.SettingsViewModel.CustomTheme);
                var foreground = theme == null ? null : (ThemeFiles.Brush(theme.Foreground) ?? ThemeFiles.Readable(theme.Background));
                if (foreground != null) openingText.Foreground = foreground; else openingText.ClearValue(Microsoft.UI.Xaml.Controls.TextBlock.ForegroundProperty);
                // The page's own colour under every theme: the control's is set under a custom theme only, and a clear one
                // showed the page beneath - the document being left, or nothing yet.
                opening.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(ServiceProvider.GetPageBackground());
                opening.Visibility = Visibility.Visible;
            }
            // Looked at again until the load is confirmed, so the ten seconds can end it.
            timer.Interval = TimeSpan.FromMilliseconds(500);
            timer.Tick += OnOpeningDelay;
            timer.Start();
        }

        // The page hidden or shown: only the web view, over the control's own colour (UpdateBackdrop). Hiding the whole
        // control showed what lay behind it. Collapsed, not transparent: WinUI 3's web view fills what its page has not
        // painted yet with a light grey of its own, and at opacity 0 that still came through (E2E SF01, 83% light).
        private void ShowPage(bool shown)
        {
            if (WebViewController?.WebView is { } webView) webView.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        }

        [SuppressPropertyChangedWarnings]
        private void OnThemeChanged()
        {
            // Windows' colour changes come on a thread of their own: the control is touched on the UI thread only. Set from
            // there, it threw, and the throw ended the subscription - the page never heard of a theme change again (TH04-06).
            DispatcherQueue.TryEnqueue(UpdateBackdrop);
            _ = DispatcherQueue.RunIdleAsync(() => PostMessage("ThemeChanged", ServiceProvider.GetCurrentTheme()));
        }

        private void OnContentLoaded()
        {
            Core.Utilities.StartupTiming.Reached("editor page loaded");
            ShowPage(true);
            IsEditorLoaded = true;
            if (AppViewModel.EditorViewModel.FocusWhenLoaded)
            {
                AppViewModel.EditorViewModel.FocusWhenLoaded = false;
                FocusEditor();
            }
        }

        public void FocusEditor()
        {
            Core.Utilities.Log.Debug($"focus: FocusEditor, web view {WebViewController?.WebView?.FocusState}");
            Focus(FocusState.Programmatic);
            WebViewController?.EnsureFocus();
        }

        public void FocusEditorInPlace()
        {
            if (IsEditorLoaded && XamlRoot != null && WebViewController?.WebView is { } webView
                && Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot) == webView)
            {
                Core.Utilities.Log.Debug("focus: the web view has the keyboard; the page gives it to its editor");
                PostMessage("TakeKeyboard", null);
                return;
            }
            FocusEditor();
        }

        protected override void OnPointerPressed(PointerRoutedEventArgs e)
        {
            base.OnPointerPressed(e);
            Focus(FocusState.Pointer);
        }

        private async void OnLoaded(object sender, RoutedEventArgs e) => await StartAsync();

        // The web view made (again, when the one there lost its browser) and the editor page loaded in it.
        private async Task StartAsync()
        {
            // Back in a window with its page as it was (leaving the settings rebuilds the main page, which takes the editor
            // out and puts it back): nothing to load again. Loading the page again drew the whole document once more - on a
            // long one a second or more over a light page - and lost the documents it kept for a switch back. Settings
            // changed meanwhile reached the page as they were made (SettingsChanged, LanguageChanged, ThemeChanged).
            if (IsEditorLoaded && WebViewController?.CoreWebView2 != null)
            {
                Core.Utilities.Log.Debug("editor: back in a window, the page kept");
                // Hidden and shown again a moment later: the browser hears the page is shown, paints it and goes on drawing
                // a long document. The settings' slide in and out did that by itself; without the window animations (off by
                // default) a long document scrolled down came back blank and stayed so (SF02 after the cases before it).
                ShowPage(false);
                DispatcherQueue.TryEnqueue(() => { if (IsLoaded) ShowPage(true); });
                if (AppViewModel.EditorViewModel.FocusWhenLoaded)
                {
                    AppViewModel.EditorViewModel.FocusWhenLoaded = false;
                    FocusEditor();
                }
                return;
            }
            // The web view is made only now, once the window's content is laid out (todo #16).
            Core.Utilities.StartupTiming.Reached("editor control loaded");
            UpdateBackdrop();
            ShowPage(false);
            IsEditorLoaded = false;
            IsEditorLoadFailed = false;
            // A web view whose browser process is gone has no core any more: it cannot load a page, only be replaced.
            if (WebViewController != null && WebViewController.CoreWebView2 == null) DropWebView();
            if (WebViewController == null)
            {
                var webViewController = new WebViewController();
                if (!await webViewController.InitializeAsync(root))
                {
                    webViewController.Dispose();
                    IsEditorLoadFailed = true;
                    return;
                }
                if (disposables.IsDisposed)
                {
                    webViewController.Dispose();
                    IsEditorLoadFailed = true;
                    return;
                }
                try
                {
                    WebViewController = webViewController;
                    UpdateBackdrop();
                    ShowPage(false);
                    OnCoreWebView2Initialized();
                }
                catch
                {
                    webViewController.Dispose();
                    WebViewController = null;
                    IsEditorLoadFailed = true;
                    return;
                }
            }
            try
            {
                LoadStaticResources();
            }
            catch
            {
                IsEditorLoadFailed = true;
            }
        }

        private void DropWebView()
        {
            var old = WebViewController;
            WebViewController = null;
            old?.Dispose();
        }

        // The browser process gone: its web view is dead for good (a reload threw, and every later load of the page),
        // so a new one is made - with a new browser, the environment having let the old one go - and the page loaded
        // in it brings the document back through the usual start-up handshake.
        private async void RestartAfterBrowserExited(WebViewController failed)
        {
            try
            {
                if (disposables.IsDisposed || WebViewController != failed) return;
                Core.Utilities.Log.Debug("editor: web view browser gone, making a new web view");
                AppViewModel.EditorViewModel.OnEditorPageNavigating();
                DropWebView();
                await StartAsync();
                Core.Utilities.Log.Debug($"editor: new web view {(IsEditorLoadFailed ? "failed" : "made, page loading")}");
            }
            catch (Exception ex)
            {
                IsEditorLoadFailed = true;
                Core.Utilities.Log.Debug($"editor: new web view after the browser exited failed: {ex.Message}");
            }
        }

        private void OnCoreWebView2Initialized()
        {
            CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
            CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
            CoreWebView2.Settings.AreDevToolsEnabled = false;
            CoreWebView2.Settings.IsBuiltInErrorPageEnabled = false;
            CoreWebView2.Settings.IsGeneralAutofillEnabled = false;
            CoreWebView2.Settings.IsPinchZoomEnabled = true;
            CoreWebView2.Settings.IsStatusBarEnabled = false;
            CoreWebView2.Settings.IsSwipeNavigationEnabled = false;
            CoreWebView2.Settings.IsZoomControlEnabled = false;
            CoreWebView2.ScriptDialogOpening += OnScriptDialogOpening;
            CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            CoreWebView2.NewWindowRequested += OnNewWindowRequested;
            // The editor page is loaded again (leaving the settings page, a page error, a crashed web process): until
            // the new page confirms a load, what the host knows the old page confirmed says nothing about it.
            // Anywhere else, never: a link whose click the page did not take for itself (raw HTML in reading mode) took
            // the whole editor to the site, over every tab of the window, with no way back. A link the reader clicked
            // opens as a link from the page does; anything else (a script, a refresh in pasted HTML) is only refused.
            CoreWebView2.NavigationStarting += (s, args) =>
            {
                if (IsEditorPage(args.Uri))
                {
                    AppViewModel.EditorViewModel.OnEditorPageNavigating();
                    return;
                }
                args.Cancel = true;
                Core.Utilities.Log.Debug($"editor: navigation away from the editor page refused ({(args.IsUserInitiated ? "a click" : "not the reader's")}): {Core.Utilities.LogText.Address(args.Uri)}");
                if (args.IsUserInitiated) OpenNewWindow(args.Uri);
            };
            // The browser process can die under the editor (a GPU driver, a renderer bug). Left alone that is a
            // blank editor until the app is restarted; reloading the page brings the document back through the
            // usual start-up handshake, and the log says what happened.
            var controller = WebViewController;
            var environment = CoreWebView2.Environment;
            CoreWebView2.ProcessFailed += (s, args) =>
            {
                Core.Utilities.Log.Debug($"editor: web view process failed: {args.ProcessFailedKind} {args.Reason} exit={args.ExitCode}");
                // The whole browser gone takes the web view with it: only a new one brings the editor back.
                if (args.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited)
                {
                    WebViewController.Forget(environment);
                    DispatcherQueue.TryEnqueue(() => RestartAfterBrowserExited(controller));
                }
                else if (args.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessExited || args.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
                    _ = DispatcherQueue.RunIdleAsync(_ => { try { CoreWebView2?.Reload(); } catch (Exception ex) { Core.Utilities.Log.Debug($"editor: reload after process failure failed: {ex.Message}"); } });
            };
#if DEBUG
            CoreWebView2.AddWebResourceRequestedFilter("http://local-file-access/*", CoreWebView2WebResourceContext.All);
            CoreWebView2.WebResourceRequested += OnWebResourceRequested;
            CoreWebView2.OpenDevToolsWindow();
#endif
        }

        private static bool IsEditorPage(string uri)
        {
#if DEBUG
            if (uri != null && uri.StartsWith("http://localhost:3000", StringComparison.OrdinalIgnoreCase)) return true;
#endif
            var index = new Uri(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Statics", "index.html")).AbsoluteUri;
            return uri != null && uri.StartsWith(index, StringComparison.OrdinalIgnoreCase);
        }

        private void LoadStaticResources()
        {
            // At once, not only when the web view raises NavigationStarting a moment later: a read in between would
            // still trust the page this navigation replaces.
            AppViewModel.EditorViewModel.OnEditorPageNavigating();
# if DEBUG
            WebViewController.CoreWebView2.Navigate("http://localhost:3000");
            // var staticsFolder = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Statics");
            // WebViewController.CoreWebView2.Navigate($"file:///{staticsFolder}/index.html");
#else
            var staticsFolder = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Statics");
            var htmlPath = System.IO.Path.Combine(staticsFolder, "index.html");
            WebViewController.CoreWebView2.Navigate(new Uri(htmlPath).AbsoluteUri);
#endif
        }

        private async void OnWebResourceRequested(object sender, CoreWebView2WebResourceRequestedEventArgs args)
        {
            var deferral = args.GetDeferral();
            try
            {
                var uri = new Uri(args.Request.Uri);
                var query = HttpUtility.ParseQueryString(uri.Query);
                var src = HttpUtility.UrlDecode(query["src"]);
                UriHelper.TryGetLocalPath(src, out var path);
                path = System.IO.Path.Combine(AppViewModel.FileViewModel.ImageBasePath, path);
                var stream = await Task.Run(() => new MemoryStream(File.ReadAllBytes(path)));
                args.Response = CoreWebView2.Environment.CreateWebResourceResponse(stream.AsRandomAccessStream(), 200, "OK", null);
            }
            catch (Exception)
            {
                args.Response = CoreWebView2.Environment.CreateWebResourceResponse(null, 404, "Not found", null);
            }
            finally
            {
                deferral.Complete();
            }
        }

        private async void OnScriptDialogOpening(object sender, CoreWebView2ScriptDialogOpeningEventArgs args)
        {
            try
            {
                await AppContentDialog.Create("Message", args.Message, Locale.GetString("Ok")).ShowAsync();
            }
            catch (Exception ex)
            {
                Log.WriteLocal("ScriptDialog", ex.ToString());
            }
        }

        public bool PostMessage(string name, object args)
        {
            try
            {
                CoreWebView2?.PostWebMessageAsString(JsonConvert.SerializeObject(new { name, args }, Core.Config.EditorJsonSerializerSettings));
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void OnWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            Transport.EmitWebViewMessage(this, e.TryGetWebMessageAsString());
        }

        private void OnNewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs args)
        {
            args.Handled = true;
            if (PInvoke.GetIsKeyDown(VirtualKey.Control))
                Core.Utilities.Common.OpenUrl(args.Uri);
        }

        private async void OpenNewWindow(string uri)
        {
            try
            {
                if (!UriHelper.IsWebUrl(uri) && UriHelper.TryGetLocalPath(uri, out var path))
                {
                    var filePath = AppViewModel.FileViewModel.FilePath;
                    if (!string.IsNullOrEmpty(filePath))
                    {
                        var fullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(filePath), path));
                        if (File.Exists(fullPath))
                        {
                            // As a file opened from Explorer: a tab of this window or a window of its own, as the settings
                            // say ("open files in a new tab"), or the tab it is open in already. The New Window command
                            // always makes a window, and a link went through it.
                            if (FileTypeHelper.IsMarkdownFile(fullPath))
                                Utilities.Common.OpenNewWindow(new[] { fullPath });
                            else
                                Core.Utilities.Common.OpenUrl(fullPath);
                            return;
                        }
                    }
                }
                Core.Utilities.Common.OpenUrl(uri);
            }
            catch (Exception ex)
            {
                await AppContentDialog.Create(Locale.GetString("Error"), ex.Message, Locale.GetDialogString("Ok")).ShowAsync(XamlRoot);
            }
        }

        public void Dispose()
        {
            WebViewController?.Dispose();
            canvas?.Children.Clear();
            disposables.Dispose();
        }

        public Rectangle GetDummyRectangle(Rect rect)
        {
            dummyRectangle.Width = rect.Width;
            dummyRectangle.Height = rect.Height;
            Canvas.SetLeft(dummyRectangle, rect.Left);
            Canvas.SetTop(dummyRectangle, rect.Top);
            return dummyRectangle;
        }

        public Rectangle MoveDummyRectangle(Point offset)
        {
            Canvas.SetLeft(dummyRectangle, Canvas.GetLeft(dummyRectangle) + offset.X);
            Canvas.SetTop(dummyRectangle, Canvas.GetTop(dummyRectangle) + offset.Y);
            return dummyRectangle;
        }
    }
}
