using System;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Typedown.Core.Utilities;
using Typedown.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using muxc = Microsoft.UI.Xaml.Controls;

namespace Typedown.Core.Controls
{
    public sealed partial class LeftPane : UserControl
    {
        public static readonly DependencyProperty IsSearchPaneOpenProperty = DependencyProperty.Register(nameof(IsSearchPaneOpen), typeof(bool), typeof(LeftPane), new(false));
        public bool IsSearchPaneOpen { get => (bool)GetValue(IsSearchPaneOpenProperty); set => SetValue(IsSearchPaneOpenProperty, value); }

        public AppViewModel ViewModel => DataContext as AppViewModel;

        public SettingsViewModel Settings => ViewModel?.SettingsViewModel;

        private readonly CompositeDisposable disposables = new();

        public LeftPane()
        {
            InitializeComponent();
            // One brush for the marks, there before any is drawn: a new accent is a new colour of it and reaches every
            // mark at once. The bar under Files/Outline finds it here; the rows of the outline and the folder tree in the
            // application's resources - a row WinUI builds (or recycles) away from the pane looks up only those, and one
            // outline row kept the system accent under a custom theme. Only these two trees use that key.
            Resources[NavigationAccentKey] = AccentBrush;
            Application.Current.Resources[TreeAccentKey] = AccentBrush;
            Root.ActualThemeChanged += (_, _) => UpdateAccent();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            disposables.Add(Settings.WhenPropertyChanged(nameof(SettingsViewModel.SidePaneIndex))
                .Cast<int>()
                .StartWith(Settings.SidePaneIndex)
                .Subscribe(UpdateSelectedItem));
            var uiViewModel = ViewModel.UIViewModel;
            disposables.Add(uiViewModel.WhenPropertyChanged(nameof(UIViewModel.FolderSearchOpen))
                .Cast<bool>()
                .StartWith(uiViewModel.FolderSearchOpen)
                .Subscribe(open => IsSearchPaneOpen = open));
            // The tabs read their text when the pane loads. Changing the language on the settings page builds a new
            // main page; a change from another window or the automation API leaves this one, so the tabs follow it
            // here, once the language has been applied.
            disposables.Add(Settings.WhenPropertyChanged(nameof(SettingsViewModel.Language))
                .Subscribe(language => _ = DispatcherQueue.RunIdleAsync(args =>
                {
                    Folder.Content = Locale.GetString("Files");
                    Toc.Content = Locale.GetString("Outline");
                })));
            disposables.Add(Settings.WhenPropertyChanged(nameof(SettingsViewModel.CustomTheme))
                .StartWith(Settings.CustomTheme)
                .Subscribe(_ => UpdateAccent()));
            // The system's accent changed (Windows settings): raised off the UI thread.
            disposables.Add(uiSettings.GetColorValuesObservable()
                .Subscribe(_ => DispatcherQueue.TryEnqueue(UpdateAccent)));
        }

        // The marks of what is chosen here - the bar under Files/Outline, the pill beside the current heading in the
        // outline and beside the open file in the folder tree - are drawn in the accent colour, which WinUI takes
        // from Windows, not from the custom theme: with any theme they stayed the system blue. A theme with an
        // accent of its own has it put here, where the Files/Outline bar, the outline and the folder tree all look it up; a
        // theme without one leaves the system accent. Each window has its own pane and so its own brush.
        private const string NavigationAccentKey = "NavigationViewSelectionIndicatorForeground";

        private const string TreeAccentKey = "TreeViewItemSelectionIndicatorForeground";

        // Shared by the windows' panes: the custom theme is one setting for all of them, and they share the UI thread.
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush AccentBrush = new();

        private readonly global::Windows.UI.ViewManagement.UISettings uiSettings = new();

        private void UpdateAccent()
        {
            var custom = ThemeFiles.Brush(ThemeFiles.Find(Settings?.CustomTheme)?.Accent) as Microsoft.UI.Xaml.Media.SolidColorBrush;
            // Without one, the system accent in the shade WinUI gives these marks (AccentFillColorDefault): darker on a
            // light theme, lighter on a dark one.
            AccentBrush.Color = custom?.Color ?? uiSettings.GetColorValue(Root.ActualTheme == ElementTheme.Dark
                ? global::Windows.UI.ViewManagement.UIColorType.AccentLight2
                : global::Windows.UI.ViewManagement.UIColorType.AccentDark1);
        }

        private void UpdateSelectedItem(int index)
        {
            NavigationView.SelectedItem = NavigationView.MenuItems[index];
        }

        private void OnSelectionChanged(muxc.NavigationView sender, muxc.NavigationViewSelectionChangedEventArgs args)
        {
            var pageName = (args.SelectedItem as muxc.NavigationViewItem).Tag as string;
            var pageType = SidePaneControls.Pages.Route.GetSidePanePageType(pageName);
            var animation = Settings.AnimationEnable && Frame.SourcePageType != null;
            var transition = animation ? args.RecommendedNavigationTransitionInfo : new SuppressNavigationTransitionInfo();
            Frame.Navigate(pageType, null, transition);
        }

        private void OnSearchButtonClick(object sender, RoutedEventArgs e)
        {
            if (ViewModel?.UIViewModel != null) ViewModel.UIViewModel.FolderSearchOpen = true;
            IsSearchPaneOpen = true;
        }

        private void OnSearchPaneClose(object sender, EventArgs e)
        {
            if (ViewModel?.UIViewModel != null) ViewModel.UIViewModel.FolderSearchOpen = false;
            IsSearchPaneOpen = false;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            disposables.Clear();
            Bindings?.StopTracking();
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            FrameClip.Rect = new(0, 0, Frame.ActualWidth, Frame.ActualHeight);
        }
    }
}
