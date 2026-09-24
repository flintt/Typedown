using System;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Typedown.Core.Utilities;
using Typedown.Core.ViewModels;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

namespace Typedown.Core.Pages
{
    public sealed partial class MainPage : Page
    {
        public AppViewModel AppViewModel => this.GetService<AppViewModel>();

        private readonly CompositeDisposable disposables = new();

        // Full screen hides the menu bar; hovering the top edge shows it until the pointer leaves and no menu is open.
        private bool menuBarRevealed;

        private readonly DispatcherTimer hideMenuBarTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };

        public MainPage()
        {
            InitializeComponent();
            hideMenuBarTimer.Tick += OnHideMenuBarTimerTick;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var uiViewModel = AppViewModel.UIViewModel;
            disposables.Add(uiViewModel.WhenPropertyChanged(nameof(UIViewModel.IsFullScreen)).Cast<bool>().StartWith(uiViewModel.IsFullScreen).Subscribe(OnFullScreenChanged));
            var settings = AppViewModel.SettingsViewModel;
            disposables.Add(settings.WhenPropertyChanged(nameof(settings.CustomTheme)).StartWith(settings.CustomTheme).Subscribe(_ => UpdateThemeColours()));
            disposables.Add(settings.WhenPropertyChanged(nameof(settings.Language)).Subscribe(_ => RebuildLocalizedChrome()));
        }

        /// <summary>
        /// Builds the parts that hold text again after a language change. Every label in the XAML is resolved
        /// once, when the control is created, so the menu bar and the status bar keep the language they were
        /// built in — the way to change them is to build them again. Menus, dialogs and the context menu are
        /// created when they are opened and come up in the new language on their own.
        /// </summary>
        private void RebuildLocalizedChrome()
        {
            _ = Dispatcher.TryRunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                try
                {
                    var grid = MenuBarHost?.Parent as Grid;
                    if (grid == null) return;
                    var index = grid.Children.IndexOf(MenuBarHost);
                    var row = Grid.GetRow(MenuBarHost);
                    var alignment = MenuBarHost.VerticalAlignment;
                    var background = MenuBarHost.Background;
                    var visibility = MenuBarHost.Visibility;
                    grid.Children.RemoveAt(index);
                    var menuBar = new Controls.MenuBar { DataContext = DataContext, Visibility = visibility, VerticalAlignment = alignment, Background = background };
                    menuBar.PointerEntered += OnMenuBarPointerEntered;
                    menuBar.PointerExited += OnMenuBarPointerExited;
                    Grid.SetRow(menuBar, row);
                    grid.Children.Insert(index, menuBar);
                    MenuBarHost = menuBar;
                    // The status bar is created by its x:Load binding, so turning it off and on again is what
                    // builds it afresh; it paints itself from the theme when it loads.
                    var settings = AppViewModel.SettingsViewModel;
                    if (settings.StatusBarOpen)
                    {
                        settings.StatusBarOpen = false;
                        _ = Dispatcher.RunIdleAsync(_ => settings.StatusBarOpen = true);
                    }
                    UpdateThemeColours();
                }
                catch (Exception ex)
                {
                    Log.Debug($"rebuild after language change: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// A custom theme colours the strips around the editor as well: the menu bar and the status bar. What
        /// the theme does not name keeps the colours of the built-in theme it builds on, and a panel painted
        /// without a text colour gets one that can be read on it.
        /// </summary>
        private void UpdateThemeColours()
        {
            try
            {
                var theme = ThemeFiles.Find(AppViewModel.SettingsViewModel.CustomTheme);
                var surface = ThemeFiles.Brush(theme?.Surface) ?? ThemeFiles.Brush(theme?.Background);
                var foreground = ThemeFiles.Brush(theme?.Foreground) ?? ThemeFiles.Readable(theme?.Surface ?? theme?.Background);
                MenuBarHost?.ApplyThemeBrushes(surface, foreground);
                StatusBar?.ApplyThemeBrushes(surface, foreground);
            }
            catch (Exception ex)
            {
                Log.Debug($"theme colours: {ex.Message}");
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            hideMenuBarTimer.Stop();
            disposables.Clear();
            Bindings?.StopTracking();
        }

        private void OnFullScreenChanged(bool isFullScreen)
        {
            menuBarRevealed = false;
            hideMenuBarTimer.Stop();
            UpdateMenuBarVisibility();
        }

        private void UpdateMenuBarVisibility()
        {
            var isFullScreen = AppViewModel?.UIViewModel?.IsFullScreen ?? false;
            MenuBarHost.Visibility = !isFullScreen || menuBarRevealed ? Visibility.Visible : Visibility.Collapsed;
            // Revealed in full screen it floats over the editor instead of pushing the content down.
            Grid.SetRow(MenuBarHost, isFullScreen ? 1 : 0);
            MenuBarHost.VerticalAlignment = isFullScreen ? VerticalAlignment.Top : VerticalAlignment.Stretch;
            MenuBarHost.Background = isFullScreen ? GetOpaqueBackground() : null;
        }

        private Brush GetOpaqueBackground()
        {
            if (Application.Current.Resources.TryGetValue("SolidBackgroundFillColorBaseBrush", out var brush) && brush is Brush solid)
                return solid;
            return Application.Current.Resources["ApplicationPageBackgroundThemeBrush"] as Brush;
        }

        private void OnRevealStripPointerEntered(object sender, PointerRoutedEventArgs e)
        {
            menuBarRevealed = true;
            hideMenuBarTimer.Stop();
            UpdateMenuBarVisibility();
        }

        private void OnMenuBarPointerEntered(object sender, PointerRoutedEventArgs e)
        {
            hideMenuBarTimer.Stop();
        }

        private void OnMenuBarPointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (menuBarRevealed)
                hideMenuBarTimer.Start();
        }

        private void OnHideMenuBarTimerTick(object sender, object e)
        {
            if (!menuBarRevealed) { hideMenuBarTimer.Stop(); return; }
            // A menu flyout is open (pointer is in the popup): keep the bar until it closes.
            if (XamlRoot != null && VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot).Any())
                return;
            hideMenuBarTimer.Stop();
            menuBarRevealed = false;
            UpdateMenuBarVisibility();
        }
    }
}
