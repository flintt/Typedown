using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Typedown.Core.Models;
using Typedown.Core.Utilities;
using Typedown.Core.ViewModels;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using muxc = Microsoft.UI.Xaml.Controls;

namespace Typedown.Core.Controls
{
    /// <summary>Tab strip above the editor; one tab per open document (see <see cref="TabsViewModel"/>).</summary>
    public sealed partial class DocumentTabBar : UserControl
    {
        public AppViewModel ViewModel => DataContext as AppViewModel;

        public TabsViewModel Tabs => ViewModel?.TabsViewModel;

        private readonly CompositeDisposable disposables = new();

        public DocumentTabBar()
        {
            InitializeComponent();
            // handledEventsToo: once there are more tabs than fit, the strip's own scroll viewer takes the
            // wheel and marks it handled, so a plain handler never sees it and the wheel only slid the strip
            // sideways instead of moving between tabs.
            AddHandler(PointerWheelChangedEvent, new PointerEventHandler(OnPointerWheelChanged), true);
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Bindings.Update();
            var settings = ViewModel?.SettingsViewModel;
            if (settings != null)
                disposables.Add(settings.WhenPropertyChanged(nameof(settings.CustomTheme)).StartWith(settings.CustomTheme).Subscribe(_ => UpdateThemeColours()));
        }

        /// <summary>
        /// The editor CSS runs in WebView and cannot style this native WinUI control. Mirror the custom theme's
        /// shell colours here: inactive tabs use the panel surface, while the selected tab joins the editor
        /// background. The remaining states are derived so hover, press, close and add buttons stay legible.
        /// </summary>
        private void UpdateThemeColours()
        {
            var theme = ThemeFiles.Find(ViewModel?.SettingsViewModel?.CustomTheme);
            var editor = ThemeFiles.Brush(theme?.Background);
            var surface = ThemeFiles.Brush(theme?.Surface) ?? editor;
            var foreground = ThemeFiles.Brush(theme?.Foreground) ?? ThemeFiles.Readable(theme?.Surface ?? theme?.Background);
            var border = ThemeFiles.Brush(theme?.Border);
            var accent = ThemeFiles.Brush(theme?.Accent);

            TabView.Background = surface ?? new SolidColorBrush(Colors.Transparent);

            var inactiveForeground = WithOpacity(foreground, .68);
            var disabledForeground = WithOpacity(foreground, .40);
            var hover = Blend(surface, foreground, .08);
            var pressed = Blend(surface, foreground, .14);
            var buttonHover = WithOpacity(foreground, .10);
            var buttonPressed = WithOpacity(foreground, .16);

            SetBrush("TabViewItemHeaderBackground", surface);
            SetBrush("TabViewItemHeaderBackgroundSelected", editor ?? surface);
            SetBrush("TabViewItemHeaderBackgroundPointerOver", hover);
            SetBrush("TabViewItemHeaderBackgroundPressed", pressed);
            SetBrush("TabViewItemHeaderBackgroundDisabled", surface);
            SetBrush("TabViewItemHeaderForeground", inactiveForeground);
            SetBrush("TabViewItemHeaderForegroundSelected", foreground);
            SetBrush("TabViewItemHeaderForegroundPointerOver", foreground);
            SetBrush("TabViewItemHeaderForegroundPressed", foreground);
            SetBrush("TabViewItemHeaderForegroundDisabled", disabledForeground);

            SetBrush("TabViewItemIconForeground", inactiveForeground);
            SetBrush("TabViewItemIconForegroundSelected", foreground);
            SetBrush("TabViewItemIconForegroundPointerOver", foreground);
            SetBrush("TabViewItemIconForegroundPressed", foreground);
            SetBrush("TabViewItemSeparator", border);
            SetBrush("TabViewBorderBrush", border);
            SetBrush("TabViewItemBorderBrush", border);
            SetBrush("TabViewSelectedItemBorderBrush", accent ?? border);
            SetResource("TabViewSelectedItemBorderThickness", accent == null ? null : new Thickness(0, 2, 0, 0));

            SetButtonBrushes("TabViewButton", surface, hover, pressed, foreground, disabledForeground, border);
            SetButtonBrushes("TabViewScrollButton", surface, hover, pressed, foreground, disabledForeground, border);

            SetBrush("TabViewItemHeaderCloseButtonForeground", inactiveForeground);
            SetBrush("TabViewItemHeaderCloseButtonForegroundPointerOver", foreground);
            SetBrush("TabViewItemHeaderCloseButtonForegroundPressed", foreground);
            SetBrush("TabViewItemHeaderPointerOverCloseButtonForeground", foreground);
            SetBrush("TabViewItemHeaderPressedCloseButtonForeground", foreground);
            SetBrush("TabViewItemHeaderSelectedCloseButtonForeground", foreground);
            SetBrush("TabViewItemHeaderDisabledCloseButtonForeground", disabledForeground);
            SetBrush("TabViewItemHeaderCloseButtonBackground", new SolidColorBrush(Colors.Transparent), surface != null);
            SetBrush("TabViewItemHeaderPointerOverCloseButtonBackground", buttonHover);
            SetBrush("TabViewItemHeaderPressedCloseButtonBackground", buttonPressed);
            SetBrush("TabViewItemHeaderSelectedCloseButtonBackground", new SolidColorBrush(Colors.Transparent), surface != null);
            SetBrush("TabViewItemHeaderDisabledCloseButtonBackground", new SolidColorBrush(Colors.Transparent), surface != null);
        }

        private void SetButtonBrushes(string prefix, Brush background, Brush hover, Brush pressed, Brush foreground, Brush disabledForeground, Brush border)
        {
            SetBrush(prefix + "Background", background);
            SetBrush(prefix + "BackgroundPointerOver", hover);
            SetBrush(prefix + "BackgroundPressed", pressed);
            SetBrush(prefix + "BackgroundDisabled", background);
            SetBrush(prefix + "Foreground", foreground);
            SetBrush(prefix + "ForegroundPointerOver", foreground);
            SetBrush(prefix + "ForegroundPressed", foreground);
            SetBrush(prefix + "ForegroundDisabled", disabledForeground);
            SetBrush(prefix + "BorderBrush", border);
            SetBrush(prefix + "BorderBrushPointerOver", border);
            SetBrush(prefix + "BorderBrushPressed", border);
            SetBrush(prefix + "BorderBrushDisabled", border);
        }

        private void SetBrush(string key, Brush brush, bool apply = true) => SetResource(key, apply ? brush : null);

        private void SetResource(string key, object value)
        {
            if (value == null)
                TabView.Resources.Remove(key);
            else
                TabView.Resources[key] = value;
        }

        private static Brush WithOpacity(Brush brush, double opacity)
        {
            if (!(brush is SolidColorBrush solid)) return brush;
            var colour = solid.Color;
            return new SolidColorBrush(Color.FromArgb((byte)(255 * opacity), colour.R, colour.G, colour.B));
        }

        private static Brush Blend(Brush background, Brush foreground, double amount)
        {
            if (!(background is SolidColorBrush bg) || !(foreground is SolidColorBrush fg)) return background;
            byte Mix(byte from, byte to) => (byte)Math.Round(from + (to - from) * amount);
            return new SolidColorBrush(Color.FromArgb(255, Mix(bg.Color.R, fg.Color.R), Mix(bg.Color.G, fg.Color.G), Mix(bg.Color.B, fg.Color.B)));
        }

        /// <summary>
        /// The wheel over the tab strip moves between tabs: up goes left, down goes right. The strip scrolls
        /// itself when there are more tabs than fit, which is never what the wheel is wanted for here.
        /// </summary>
        private void OnPointerWheelChanged(object sender, Windows.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (Tabs == null) return;
            var delta = e.GetCurrentPoint(this).Properties.MouseWheelDelta;
            if (delta == 0) return;
            e.Handled = true;
            (delta > 0 ? Tabs.PreviousTabCommand : Tabs.NextTabCommand).Execute(default);
            // The strip may have scrolled itself before this ran; following the selection puts it right.
            TabView.SelectedItem = Tabs.ActiveTab;
        }

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Tabs == null) return;
            if (TabView.SelectedItem is DocumentTab tab && tab != Tabs.ActiveTab)
                Tabs.SwitchTabCommand.Execute(tab);
        }

        private void OnAddTabButtonClick(muxc.TabView sender, object args)
        {
            ViewModel?.FileViewModel.NewFileCommand.Execute(default);
        }

        private void OnTabCloseRequested(muxc.TabView sender, muxc.TabViewTabCloseRequestedEventArgs args)
        {
            if (args.Item is DocumentTab tab)
                Tabs?.CloseTabCommand.Execute(tab);
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            disposables.Clear();
            Bindings?.StopTracking();
        }
    }
}
