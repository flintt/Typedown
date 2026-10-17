using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Typedown.Core.Pages.SettingPages;
using Typedown.Core.Utilities;
using Typedown.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using muxc = Microsoft.UI.Xaml.Controls;

namespace Typedown.Core.Pages
{
    public sealed partial class SettingsPage : Page
    {
        public AppViewModel ViewModel => DataContext as AppViewModel;

        public SettingsViewModel Settings => ViewModel?.SettingsViewModel;

        public ObservableCollection<SettingsBreadcrumbBarItem> BreadcrumbBarItems { get; } = new();

        private readonly CompositeDisposable disposables = new();

        public SettingsPage()
        {
            InitializeComponent();
            ContentFrame.Navigated += OnNavigated;
        }

        private void OnNavigationViewSelectionChanged(muxc.NavigationView sender, muxc.NavigationViewSelectionChangedEventArgs args)
        {
            var pageName = (sender.SelectedItem as muxc.NavigationViewItem).Tag as string;
            var pageType = Route.GetSettingsPageType(pageName);
            if (pageType != ContentFrame.SourcePageType)
            {
                var transition = Settings.AnimationEnable ? args.RecommendedNavigationTransitionInfo : new SuppressNavigationTransitionInfo();
                BreadcrumbBarItems.Clear();
                ContentFrame.Navigate(Route.GetSettingsPageType(pageName), null, transition);
                ContentFrame.BackStack.Clear();
            }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            var pageType = Route.GetSettingsPageType(e.Parameter as string) ?? typeof(GeneralPage);
            ContentFrame.Navigate(pageType);
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            VisualStateManager.GoToState(this, ActualWidth.GetBreakPointValue("Large", "Medium", "Small"), false);
            ApplyCompact();
        }

        // In compact mode the window has no caption (RootControl): the settings start at its top, in their own colours,
        // as the main page starts with its menu bar. Their way back is the pane's back button; the strip along the top, as
        // high as that menu row, is where the window is dragged, the page's title below it. The strip leaves out the
        // pane's buttons at its left: the back button, and narrow, the menu button beside it.
        private void ApplyCompact()
        {
            var compact = Settings?.AppCompactMode ?? false;
            NavigationView.IsBackButtonVisible = compact ? muxc.NavigationViewBackButtonVisible.Visible : muxc.NavigationViewBackButtonVisible.Collapsed;
            TopDrag.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
            TopDrag.Margin = new Thickness(ActualWidth.GetBreakPointValue(48, 48, 88), 0, 0, 0);
            WindowChrome.SetDrag(TopDrag, compact);
            ContentFrameWrapper.Padding = new Thickness(0, compact ? TopDrag.Height : 0, 0, 0);
        }

        private void OnBackRequested(muxc.NavigationView sender, muxc.NavigationViewBackRequestedEventArgs args)
        {
            if (ViewModel?.GoBackCommand.IsExecutable == true) ViewModel.GoBackCommand.Execute(null);
        }

        private void OnNavigated(object sender, NavigationEventArgs e)
        {
            var item = NavigationView.MenuItems.OfType<muxc.NavigationViewItem>().Where(x => Route.GetSettingsPageType(x.Tag as string) == ContentFrame.SourcePageType).FirstOrDefault();
            if (item != null) NavigationView.SelectedItem = item;
            switch (e.NavigationMode)
            {
                case NavigationMode.Forward:
                case NavigationMode.New:
                    BreadcrumbBarItems.Add(new(e.Content as Page));
                    break;
                case NavigationMode.Back:
                    BreadcrumbBarItems.RemoveAt(BreadcrumbBarItems.Count - 1);
                    break;
            }
        }

        private void Navigate(string args)
        {
            var path = args?.Split('?')[0].TrimStart('/').Split('/');
            if (path != null && path.Length > 1)
            {
                var type = Route.GetSettingsPageType(path[1]);
                var query = args.Contains("?") ? args.Substring(args.IndexOf("?") + 1) : "";
                if (type != Frame.SourcePageType)
                    ContentFrame.Navigate(type, query, GetTransition());
            }
        }

        public NavigationTransitionInfo GetTransition() => Settings?.AnimationEnable ?? false ? new SlideNavigationTransitionInfo()
        {
            Effect = SlideNavigationTransitionEffect.FromRight
        } : new SuppressNavigationTransitionInfo();

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            disposables.Add(ViewModel.NavigateCommand.OnExecute.Subscribe(args => Navigate(args)));
            // The settings are a page of their own, so they have to take the theme from the settings as well:
            // opening them under a custom theme would otherwise drop back to the built-in colours.
            disposables.Add(Settings.WhenPropertyChanged(nameof(Settings.CustomTheme)).StartWith(Settings.CustomTheme).Subscribe(_ => UpdateThemeColours()));
            disposables.Add(Settings.WhenPropertyChanged(nameof(Settings.AppCompactMode)).Subscribe(_ => ApplyCompact()));
            disposables.Add(ViewModel.GoBackCommand.WhenPropertyChanged(nameof(ViewModel.GoBackCommand.IsExecutable))
                .StartWith(ViewModel.GoBackCommand.IsExecutable)
                .Subscribe(_ => NavigationView.IsBackEnabled = ViewModel.GoBackCommand.IsExecutable));
            ApplyCompact();
        }

        /// <summary>
        /// Paints the page from a custom theme; what the theme does not name keeps the built-in colour, and
        /// clearing the value puts that colour back.
        /// </summary>
        private void UpdateThemeColours()
        {
            try
            {
                var theme = ThemeFiles.Find(Settings?.CustomTheme);
                var background = ThemeFiles.Brush(theme?.Background) ?? ThemeFiles.Brush(theme?.Surface);
                var foreground = ThemeFiles.Brush(theme?.Foreground) ?? ThemeFiles.Readable(theme?.Surface ?? theme?.Background);
                if (background != null) NavigationView.Background = background;
                else NavigationView.ClearValue(BackgroundProperty);
                if (foreground != null) Foreground = foreground;
                else ClearValue(ForegroundProperty);
            }
            catch (Exception ex)
            {
                Log.Debug($"settings theme: {ex.Message}");
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            disposables.Clear();
             Bindings?.StopTracking();
        }

        private void OnBreadcrumbBarItemClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
        {
            var count = BreadcrumbBarItems.Count - args.Index - 1;
            for (int i = 0; i < count; i++)
                ContentFrame.GoBack();
        }

        public void SetPageTitle(Page page, string title)
        {
            var item = BreadcrumbBarItems.Where(x => x.Page == page).FirstOrDefault();
            if (item != null)
                item.Title = title;
        }
    }

    public partial class SettingsBreadcrumbBarItem : INotifyPropertyChanged
    {
        public Page Page { get; }

        public Type PageType { get; }

        public string Title { get; set; }

        public SettingsBreadcrumbBarItem(Page page, string title = null)
        {
            Page = page;
            PageType = Page.GetType();
            Title = title ?? Locale.GetTypeString(PageType);
        }
    }
}
