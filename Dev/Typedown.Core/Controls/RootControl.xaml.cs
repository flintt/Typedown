using System;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Typedown.Core.Pages;
using Typedown.Core.ViewModels;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Animation;

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

        private void OnLoaded(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            ViewModel.XamlRoot = XamlRoot;
            disposables.Add(ViewModel.NavigateCommand.OnExecute.Subscribe(args => Navigate(args)));
            Frame.Navigate(typeof(MainPage), null);
        }

        private void OnUnloaded(object sender, Windows.UI.Xaml.RoutedEventArgs e)
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

        public static bool GetCaptionIsLoad(bool compactMode, Type currentPage, bool isFullScreen)
        {
            if (isFullScreen && currentPage == typeof(MainPage))
                return false;
            return !compactMode || currentPage != typeof(MainPage);
        }

        private void OnClosePrintPreviewClick(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            ViewModel?.UIViewModel?.ClosePrintPreview();
        }
    }
}
