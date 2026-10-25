using System;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Typedown.Core.Pages;
using Typedown.Core.Utilities;
using Typedown.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;

namespace Typedown.Core.Controls
{
    public sealed partial class RootControl : UserControl
    {
        public AppViewModel ViewModel => DataContext as AppViewModel;

        public SettingsViewModel Settings => ViewModel?.SettingsViewModel;

        private readonly CompositeDisposable disposables = new();

        public RootControl()
        {
            InitializeComponent();
        }

        /// <summary>Whether the theme-coloured window background shows (off while a backdrop shows through instead).</summary>
        public void ShowWindowBackground(bool show) => WindowBackground.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

        private void OnLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            ViewModel.XamlRoot = XamlRoot;
            // The last session's tabs and active document, while the editor's page starts: begun here, before the main
            // page is built - queued after it, it came 0.7 s after the window on w10. Its tabs go in before anything is
            // read (FileViewModel.RestoreSession), so the strip is there with the page's first frame.
            ViewModel.EditorViewModel.BeginStartupLoad();
            disposables.Add(ViewModel.NavigateCommand.OnExecute.Subscribe(args => Navigate(args)));
            RegisterSettingsShortcut();
            // The settings are the one page you are looking at when you change the language, so they are built
            // again straight away. The rest of the interface catches up by itself: leaving the settings
            // navigates to MainPage, and with no NavigationCacheMode that is a new page, so the menus and
            // everything else built from {u:LocaleString} are evaluated again in the new language. What does
            // not catch up is anything a view model holds as a finished string — the view models outlive the
            // navigation — which is why UIViewModel rebuilds the window title on a language change.
            disposables.Add(Settings.WhenPropertyChanged(nameof(Settings.Language)).Subscribe(_ => ReloadSettingsPage()));
            disposables.Add(Settings.WhenPropertyChanged(nameof(Settings.ShortcutSettings)).Subscribe(_ => RegisterSettingsShortcut()));
            // The controls' accent (settings, dialogs) in the custom theme's, as the editor and the side pane have it.
            disposables.Add(Settings.WhenPropertyChanged(nameof(Settings.CustomTheme))
                .StartWith(Settings.CustomTheme)
                .Subscribe(name => AppAccent.Apply(ThemeFiles.Find(name as string)?.Accent)));
            disposables.Add(Settings.WhenPropertyChanged(nameof(Settings.CustomTheme)).Subscribe(_ => UpdateCaptionTheme()));
            Frame.Navigated += OnFrameNavigated;
            Frame.Navigate(typeof(MainPage), null);
        }

        private Type shownPage;

        // The window opening, and back from the settings page (its shortcut, the back button): the reader is in the
        // document, keyboard included. The editor takes it now, and again once its page has loaded. A window just opened
        // gave it to nothing: the first letters went nowhere until a click in the text.
        private void OnFrameNavigated(object sender, NavigationEventArgs e)
        {
            var from = shownPage;
            shownPage = e.SourcePageType;
            UpdateCaptionTheme();
            if (e.SourcePageType != typeof(MainPage) || (from != null && from != typeof(SettingsPage))) return;
            ViewModel.EditorViewModel.FocusWhenLoaded = true;
            _ = DispatcherQueue.RunIdleAsync(_ => ViewModel.MarkdownEditor?.FocusEditor());
        }

        // The caption row in the custom theme's colours, as the page under it: the settings' own page colour there, the
        // menu row's panel colour over the main page. It was the window's white over a Sepia page.
        private void UpdateCaptionTheme()
        {
            if (Caption == null) return;
            try
            {
                var theme = ThemeFiles.Find(Settings?.CustomTheme);
                var background = shownPage == typeof(MainPage)
                    ? ThemeFiles.Brush(theme?.Surface) ?? ThemeFiles.Brush(theme?.Background)
                    : ThemeFiles.Brush(theme?.Background) ?? ThemeFiles.Brush(theme?.Surface);
                var foreground = ThemeFiles.Brush(theme?.Foreground) ?? ThemeFiles.Readable(theme?.Surface ?? theme?.Background);
                Caption.ApplyThemeBrushes(background, foreground);
            }
            catch (Exception ex)
            {
                Utilities.Log.Debug($"caption theme: {ex.Message}");
            }
        }

        // The caption is made again each time it comes back (x:Load), clear: it takes the colours as it loads.
        private void OnCaptionLoaded(object sender, RoutedEventArgs e) => UpdateCaptionTheme();

        private void ReloadSettingsPage()
        {
            if (Frame.SourcePageType != typeof(SettingsPage)) return;
            // Built again on the section the reader is in, not the one the settings were opened on (Help > About,
            // then General: the language changed there went back to About).
            var shown = (Frame.Content as SettingsPage)?.ShownSection;
            var route = shown != null ? "Settings/" + shown : currentRoute;
            _ = DispatcherQueue.TryRunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                try
                {
                    var path = route?.TrimStart('/').Split('/');
                    var depth = Frame.BackStackDepth;
                    Frame.Navigate(typeof(SettingsPage), path != null ? string.Join('/', path.Skip(1)) : null, new SuppressNavigationTransitionInfo());
                    // Building the page again is not somewhere the user navigated to, so it leaves no entry
                    // behind: otherwise every language change added one more press to the way back out.
                    while (Frame.BackStackDepth > depth && Frame.BackStack.Count > 0)
                        Frame.BackStack.RemoveAt(Frame.BackStack.Count - 1);
                }
                catch (Exception ex)
                {
                    Utilities.Log.Debug($"reload settings after language change: {ex.Message}");
                }
            });
        }

        private IDisposable settingsShortcut;

        /// <summary>
        /// The way out of the settings. The menu entry takes care of opening them, but the menu bar belongs to
        /// the main page and is gone while the settings are up, so the shortcut that opened them would do
        /// nothing — this one lives above the frame and stays registered.
        /// </summary>
        private void RegisterSettingsShortcut()
        {
            settingsShortcut?.Dispose();
            settingsShortcut = null;
            var accelerator = this.GetService<Interfaces.IKeyboardAccelerator>();
            var key = Settings?.ShortcutSettings;
            if (accelerator == null || key == null) return;
            settingsShortcut = accelerator.Register(key, (s, e) =>
            {
                if (Frame.SourcePageType != typeof(SettingsPage)) return;
                if (Utilities.PInvoke.GetForegroundWindow() != ViewModel.MainWindow) return;
                e.Handled = true;
                // Held, the key comes again and again: once per press, or the settings went in and out until it hung.
                if (e.IsRepeat) return;
                _ = DispatcherQueue.TryRunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () => Navigate("Main"));
            });
            disposables.Add(settingsShortcut);
        }

        private void OnUnloaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            disposables.Clear();
            Bindings?.StopTracking();
        }

        /// <summary>Where the frame is now, so that asking for it again can be read as "close this".</summary>
        private string currentRoute;

        private void Navigate(string args)
        {
            var path = args?.TrimStart('/').Split('/');
            if (path == null || !path.Any())
                return;
            var type = Route.GetRootPageType(path.First());
            // Navigating to the page that is already open, by the same route, closes it: the shortcut that
            // opens the settings is then the one that leaves them again. Another route into the same page (the
            // image settings from the image menu, say) still just moves to that section.
            if (type == Frame.SourcePageType)
            {
                if (type == typeof(MainPage) || args != currentRoute)
                    return;
                currentRoute = "Main";
                Frame.Navigate(typeof(MainPage), null, GetTransition());
                return;
            }
            currentRoute = args;
            Frame.Navigate(type, string.Join('/', path.Skip(1)), GetTransition());
        }

        public NavigationTransitionInfo GetTransition() => Settings?.AnimationEnable ?? false ? new SlideNavigationTransitionInfo()
        {
            Effect = SlideNavigationTransitionEffect.FromRight
        } : new SuppressNavigationTransitionInfo();

        // The 4 px the compact main page's menu bar covers; the compact settings start at the window's top, where the strip
        // showed the window's own colour over the theme's.
        public static double GetCaptionRowMinHeight(bool isFullScreen, bool compactMode, Type currentPage) =>
            isFullScreen || (compactMode && currentPage != typeof(MainPage)) ? 0 : 4;

        // No caption in compact mode, on any page: the main page's menu bar is its top row, and the settings have their
        // back button in their pane and their empty top as the caption (SettingsPage) - a caption of their own was a
        // title bar the main page does not have, in the window's colours.
        public static bool GetCaptionIsLoad(bool compactMode, Type currentPage, bool isFullScreen)
        {
            if (isFullScreen && currentPage == typeof(MainPage))
                return false;
            return !compactMode;
        }

        private void OnClosePrintPreviewClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            ViewModel?.UIViewModel?.ClosePrintPreview();
        }
    }
}
