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
