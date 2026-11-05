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

        /// <summary>The section shown now, by its route name (null when it has none).</summary>
        public string ShownSection => Route.GetSettingsPageName(ContentFrame.SourcePageType);

        /// <summary>What scrolls the section shown (the test host reads where it is).</summary>
        public ScrollViewer Scroller => SettingsScroller;

        // Where the keyboard focus goes on this page and what sent it there, in the log. And a click on what takes no focus
        // (a row's title or text, the space between rows) moves none: with nothing focused, WinUI gave the focus to the
        // section's first setting and scrolled up to it (a reader, 2026-11-05; hp job 199: the title of "Spell check"
        // clicked at the bottom, the font size's box focused, the page at 53 of 1499). The focus may go to what is under the
        // pointer or holds it (a switch clicked anywhere on it, its knob or its On/Off text), within the control of the row
        // under the pointer (a number box's spin buttons and its text), or out of the list (a list's drop-down) - not
        // elsewhere in the list (FocusRule). What is under the pointer is looked up where the cursor is as the focus moves:
        // not from the press, which a switch's own focus comes before (hp job 214), nor from the last move over the list,
        // which a pointer put there at once (a test's, or a touch) may not have made (hp job 221: the title clicked before
        // was taken for the switch's click).
        //
        // Not only a click: whatever does not come from the keyboard (Tab, the arrows) - the click, and the focus XAML gives
        // when the window gets it back from the editor's web view, which keeps the window's focus in a window of its own -
        // goes into the list only to what is under the pointer (or holds it); else to the section picked in the pane, at the
        // top left. And such a focus never scrolls the page (OnBringIntoViewRequested): wherever it lands, the page stays.
        private void OnGettingFocus(UIElement sender, Microsoft.UI.Xaml.Input.GettingFocusEventArgs args)
        {
            focusByKeyboard = args.FocusState == FocusState.Keyboard;
            focusAt = Environment.TickCount64;
            // Only the focus WinUI hands to the page's first setting on its own (a click on what takes no focus, the
            // window had again): a focus set on a setting of its own choosing - UI Automation's SetFocus, a screen
            // reader - goes where it was set; sent to the section picked, typing into the font box went nowhere
            // (FL01 on both PCs, the full run after 1.4.1.22).
            if (!focusByKeyboard && args.NewFocusedElement is DependencyObject next && Within(next, SettingsScroller)
                && (args.FocusState != FocusState.Programmatic || IsFirstSetting(next)))
            {
                var under = CursorInRoot() is { } at
                    ? Microsoft.UI.Xaml.Media.VisualTreeHelper.FindElementsInHostCoordinates(at, SettingsScroller).Cast<DependencyObject>().ToList()
                    : new System.Collections.Generic.List<DependencyObject>();
                var top = under.FirstOrDefault();
                if (under.Count > 0 && FocusRule.FollowsClick(next, under, Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent, ActionOf))
                    Log.Debug($"settings: focus to {Describe(next)} goes with the pointer on {Chain(top)}");
                else if (NavigationView.SelectedItem is DependencyObject item && args.TrySetNewFocusedElement(item))
                {
                    Log.Debug($"settings: focus to {Describe(next)} ({args.FocusState}) sent to the section picked - the pointer is on {Chain(top)}");
                    return;
                }
                else if (args.TryCancel())
                {
                    Log.Debug($"settings: focus to {Describe(next)} ({args.FocusState}) kept back - the pointer is on {Chain(top)}");
                    return;
                }
            }
            Log.Debug($"settings: focus to {Describe(args.NewFocusedElement)} from {Describe(args.OldFocusedElement)} ({args.FocusState}, {args.InputDevice}, {args.Direction})");
        }

        // The first control of the page that takes the focus, or one inside it (a number box's text): where WinUI puts a
        // focus nobody chose.
        private bool IsFirstSetting(DependencyObject element) =>
            Microsoft.UI.Xaml.Input.FocusManager.FindFirstFocusableElement(SettingsScroller) is DependencyObject first
            && (element == first || Within(element, first) || Within(first, element));

        private bool focusByKeyboard = true;
        private long focusAt;

        // A focus that is not the keyboard's scrolls nothing: the page jumped to the top when the first setting was given
        // the focus (above). A move of the keyboard (Tab) still brings its control into view.
        private void OnBringIntoViewRequested(UIElement sender, BringIntoViewRequestedEventArgs args)
        {
            if (focusByKeyboard || Environment.TickCount64 - focusAt > 500) return;
            args.Handled = true;
            Log.Debug($"settings: no scroll to {Describe(args.TargetElement)} - its focus was not the keyboard's");
        }

        // The cursor where XAML lays the page out (the window's client area in view pixels), or null.
        private global::Windows.Foundation.Point? CursorInRoot()
        {
            var window = ViewModel?.MainWindow ?? IntPtr.Zero;
            if (window == IntPtr.Zero || XamlRoot == null || !PInvoke.GetCursorPos(out var point) || !PInvoke.ScreenToClient(window, ref point)) return null;
            var scale = XamlRoot.RasterizationScale;
            return new global::Windows.Foundation.Point(point.X / scale, point.Y / scale);
        }

        // An element and what holds it, up to its setting row, for the log.
        private static string Chain(DependencyObject element)
        {
            var names = new System.Collections.Generic.List<string>();
            for (var node = element; node != null && names.Count < 8; node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node))
            {
                names.Add(node.GetType().Name);
                if (node is Controls.NormalSettingItem || node is Controls.ButtonSettingItem) break;
            }
            return names.Count == 0 ? "nothing" : $"{string.Join(" < ", names)}{(RowOf(element) is { } row ? $" in '{row}'" : "")}";
        }

        /// <summary>The control of the setting row (its switch, list, box) an element is in, or null when it is not in one.</summary>
        private static DependencyObject ActionOf(DependencyObject element)
        {
            for (var node = element; node != null; node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node))
            {
                var action = node switch
                {
                    Controls.NormalSettingItem row => row.Action as DependencyObject,
                    Controls.ButtonSettingItem row => row.Action as DependencyObject,
                    _ => null,
                };
                if (node is Controls.NormalSettingItem || node is Controls.ButtonSettingItem)
                    return action != null && Within(element, action) ? action : null;
            }
            return null;
        }

        /// <summary>Whether element is container or below it in the visual tree.</summary>
        private static bool Within(DependencyObject element, DependencyObject container)
        {
            for (var node = element; node != null; node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node))
                if (node == container) return true;
            return false;
        }

        /// <summary>An element named for the log: its type, its automation id if any, and the setting row it is in.</summary>
        public static string Describe(DependencyObject element)
        {
            if (element == null) return "nothing";
            var name = element.GetType().Name;
            var id = element is UIElement ui ? Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(ui) : null;
            if (!string.IsNullOrEmpty(id)) name += "#" + id;
            var row = RowOf(element);
            return row != null ? $"{name} in '{row}'" : name;
        }

        /// <summary>The title of the setting row an element is in, or null.</summary>
        public static string RowOf(DependencyObject element)
        {
            for (var node = element; node != null; node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node))
                if (node is Controls.NormalSettingItem || node is Controls.ButtonSettingItem || node is Controls.ExpanderSettingItem)
                    return node.GetType().GetProperty("Title")?.GetValue(node)?.ToString();
            return null;
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
            // Another section starts at its top. It did by chance: WinUI focused its first setting and brought it into view,
            // which a focus not of the keyboard no longer does (OnBringIntoViewRequested) - the section opened where the last
            // one was scrolled to (hp job 229: General shown at Editor's bottom).
            SettingsScroller.ChangeView(null, 0, null, true);
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
            GettingFocus += OnGettingFocus;
            disposables.Add(Disposable.Create(() => GettingFocus -= OnGettingFocus));
            // On what the scroller holds: a bring-into-view comes up from the element through it to the scroller, which
            // scrolls unless one below has handled it.
            if (SettingsScroller.Content is UIElement scrolled)
            {
                scrolled.BringIntoViewRequested += OnBringIntoViewRequested;
                disposables.Add(Disposable.Create(() => scrolled.BringIntoViewRequested -= OnBringIntoViewRequested));
            }
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
