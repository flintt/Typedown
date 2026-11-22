using System;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Typedown.Core;
using Typedown.Core.Enums;
using Typedown.Core.Services;
using Typedown.Core.Utilities;
using Typedown.Core.ViewModels;
using Windows.Graphics;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;

namespace Typedown.Windows
{
    /// <summary>
    /// The scratchpad (the user's 33, 2026-11-22): a small window of its own with one text box for passages kept for a
    /// while - the same for every window and tab (its text is Services.Scratchpad.Shared, saved as it is typed). Over the
    /// other windows unless its pin is let go; where it was and its size kept. Closed, it is only hidden. It has no
    /// services of its own: the settings it reads and sets (the theme, its pin, its place) are those of a main window open,
    /// which every window shares.
    /// Without the system's frame and not in the taskbar or Alt+Tab (the user, 2026-11-22): its own bar drags it (a
    /// caption region), a strip along each edge sizes it, its x hides it. In the custom theme's colours (the user, the
    /// same day), else light or dark.
    /// </summary>
    public sealed class ScratchpadWindow : Window
    {
        private static ScratchpadWindow instance;

        private readonly Grid root;
        private readonly Grid bar;
        private readonly FrameworkElement buttons;
        private readonly TextBox box;
        private readonly ToggleButton pin;
        private readonly Button close;
        private readonly CompositeDisposable shownWith = new();
        private bool shown;
        private bool placed;
        private bool framed;

        // The colours, one brush each: a theme changed only changes their colour (the text box's template took its
        // resources once; new brushes would not reach it).
        private readonly SolidColorBrush paper = new(), ink = new(), faint = new(), line = new(), clear = new(Microsoft.UI.Colors.Transparent);

        private const string Layout = @"
<Grid xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
      xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
      AutomationProperties.AutomationId='ScratchpadWindow' BorderThickness='1'>
  <Grid.RowDefinitions>
    <RowDefinition Height='32'/>
    <RowDefinition Height='*'/>
  </Grid.RowDefinitions>
  <Grid x:Name='Bar' Background='Transparent'>
    <TextBlock x:Name='Caption' Margin='12,0,0,0' VerticalAlignment='Center' FontSize='12'/>
    <StackPanel x:Name='Buttons' Orientation='Horizontal' HorizontalAlignment='Right' Margin='0,0,4,0'>
      <ToggleButton x:Name='Pin' AutomationProperties.AutomationId='ScratchpadTopmost' Padding='8,4' Margin='0,2' Background='Transparent' BorderThickness='0'>
        <FontIcon x:Name='PinIcon' Glyph='&#xE718;' FontSize='13'/>
      </ToggleButton>
      <Button x:Name='Close' AutomationProperties.AutomationId='ScratchpadClose' Padding='8,4' Margin='0,2' Background='Transparent' BorderThickness='0'>
        <FontIcon x:Name='CloseIcon' Glyph='&#xE8BB;' FontSize='11'/>
      </Button>
    </StackPanel>
  </Grid>
  <TextBox x:Name='Box' Grid.Row='1' AutomationProperties.AutomationId='ScratchpadText' AcceptsReturn='True' TextWrapping='Wrap'
           IsSpellCheckEnabled='False' ScrollViewer.VerticalScrollBarVisibility='Auto' Margin='4,0,4,6' BorderThickness='0'/>
</Grid>";

        private ScratchpadWindow()
        {
            root = (Grid)XamlReader.Load(Layout);
            bar = (Grid)root.FindName("Bar");
            buttons = (FrameworkElement)root.FindName("Buttons");
            box = (TextBox)root.FindName("Box");
            pin = (ToggleButton)root.FindName("Pin");
            close = (Button)root.FindName("Close");
            // The colours through brushes of its own, whatever the state (pointer over, focused): set before the box's
            // template is applied, which reads them once.
            foreach (var key in new[] { "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused", "TextControlBackgroundDisabled",
                "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused", "TextControlBorderBrushDisabled" })
                box.Resources[key] = clear;
            foreach (var key in new[] { "TextControlForeground", "TextControlForegroundPointerOver", "TextControlForegroundFocused" })
                box.Resources[key] = ink;
            foreach (var key in new[] { "TextControlPlaceholderForeground", "TextControlPlaceholderForegroundPointerOver", "TextControlPlaceholderForegroundFocused" })
                box.Resources[key] = faint;
            root.Background = paper;
            root.BorderBrush = line;
            ((TextBlock)root.FindName("Caption")).Foreground = faint;
            ((FontIcon)root.FindName("PinIcon")).Foreground = ink;
            ((FontIcon)root.FindName("CloseIcon")).Foreground = ink;
            Content = root;
            UiLanguage.Set(root);
            Title = Locale.GetString("Scratchpad");
            ((TextBlock)root.FindName("Caption")).Text = Title;
            box.PlaceholderText = Locale.GetString("ScratchpadPlaceholder");
            var keepOnTop = Locale.GetString("ScratchpadTopmost");
            ToolTipService.SetToolTip(pin, keepOnTop);
            AutomationProperties.SetName(pin, keepOnTop);
            var closeName = Locale.GetString("Close");
            if (!string.IsNullOrEmpty(closeName)) AutomationProperties.SetName(close, closeName);
            box.Text = Scratchpad.Shared.Text;
            box.TextChanged += (s, e) => Scratchpad.Shared.SetText(box.Text, box);
            Scratchpad.Shared.Changed += OnStoreChanged;
            pin.Click += (s, e) =>
            {
                var settings = Settings();
                if (settings != null) settings.ScratchpadTopmost = pin.IsChecked == true;
                SetTopmost(pin.IsChecked == true);
            };
            close.Click += (s, e) => Hide();
            root.PreviewKeyDown += OnKeyDown;
            // No frame or title bar of the system's; not in the taskbar or Alt+Tab (its key brings it back).
            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
                presenter.IsResizable = true;
                presenter.PreferredMinimumWidth = 220;
                presenter.PreferredMinimumHeight = 160;
            }
            root.SizeChanged += (s, e) => UpdateRegions();
            bar.SizeChanged += (s, e) => UpdateRegions();
            root.ActualThemeChanged += (s, e) => ApplyColours();
            ThemeFiles.Changed += () => DispatcherQueue.TryEnqueueSafe(ApplyColours);
            // Closed some other way (Alt+F4): hidden, its text and place kept (only the app ending closes it).
            AppWindow.Closing += (s, e) =>
            {
                e.Cancel = true;
                Hide();
            };
            AppWindow.Changed += (s, e) => { if (shown && (e.DidPositionChange || e.DidSizeChange)) SavePlacementSoon(); };
        }

        private nint Handle => WinRT.Interop.WindowNative.GetWindowHandle(this);

        // Its bar, less its buttons, drags it; a strip along each edge sizes it - in the window's physical pixels.
        private void UpdateRegions()
        {
            if (root.XamlRoot == null) return;
            try
            {
                var scale = root.XamlRoot.RasterizationScale;
                int Px(double v) => (int)Math.Round(v * scale);
                int width = Px(root.ActualWidth), height = Px(root.ActualHeight), edge = Px(5);
                var source = InputNonClientPointerSource.GetForWindowId(AppWindow.Id);
                var caption = Px(bar.ActualWidth - buttons.ActualWidth - buttons.Margin.Right);
                source.SetRegionRects(NonClientRegionKind.Caption, new[] { new RectInt32(0, edge, Math.Max(0, caption), Math.Max(0, Px(bar.ActualHeight) - edge)) });
                source.SetRegionRects(NonClientRegionKind.TopBorder, new[] { new RectInt32(0, 0, width, edge) });
                source.SetRegionRects(NonClientRegionKind.BottomBorder, new[] { new RectInt32(0, height - edge, width, edge) });
                source.SetRegionRects(NonClientRegionKind.LeftBorder, new[] { new RectInt32(0, 0, edge, height) });
                source.SetRegionRects(NonClientRegionKind.RightBorder, new[] { new RectInt32(width - edge, 0, edge, height) });
            }
            catch (Exception ex)
            {
                Log.Debug($"scratchpad: regions: {ex.Message}");
            }
        }

        // A main window open (the last one used first): its settings are the app's, the theme and this window's own.
        private static SettingsViewModel Settings()
        {
            var window = MainWindow.LastActive is { IsGoingAway: false } last ? last : MainWindow.AllWindows.FirstOrDefault(w => !w.IsGoingAway);
            return window?.AppViewModel?.SettingsViewModel;
        }

        /// <summary>View > Scratchpad and its key: shown if hidden, hidden if shown.</summary>
        public static void Toggle()
        {
            if (instance is { shown: true }) instance.Hide();
            else ShowIt();
        }

        /// <summary>Shown and brought to the front (Put in Scratchpad: what was put in it is seen, at the end).</summary>
        public static void ShowIt()
        {
            try
            {
                instance ??= new ScratchpadWindow();
                instance.ShowNow();
            }
            catch (Exception ex)
            {
                Log.WriteLocal("Scratchpad", ex.ToString());
            }
        }

        /// <summary>The last main window closed: hidden, its text written (the app may end at once after).</summary>
        public static void OnLastMainWindowClosed()
        {
            if (instance is { shown: true }) instance.Hide();
        }

        private void ShowNow()
        {
            var settings = Settings();
            shownWith.Clear();
            if (settings != null)
            {
                shownWith.Add(settings.WhenPropertyChanged(nameof(SettingsViewModel.AppTheme))
                    .Merge(settings.WhenPropertyChanged(nameof(SettingsViewModel.CustomTheme)))
                    .Select(_ => settings.EffectiveTheme)
                    .StartWith(settings.EffectiveTheme)
                    .Subscribe(SetTheme));
                // Turned off in the settings: hidden (View > Scratchpad is gone too).
                shownWith.Add(settings.WhenPropertyChanged(nameof(SettingsViewModel.ScratchpadEnabled)).Cast<bool>()
                    .Subscribe(on => { if (!on) Hide(); }));
                pin.IsChecked = settings.ScratchpadTopmost;
            }
            if (!placed)
            {
                placed = true;
                Place(settings);
            }
            SetTopmost(pin.IsChecked == true);
            shown = true;
            Activate();
            // Its frame taken off once it is on the screen: taken off before, the window never showed (w10 job 702 - shown,
            // by what it said, with nothing on the screen where it was).
            if (!framed)
            {
                framed = true;
                if (AppWindow.Presenter is OverlappedPresenter presenter) presenter.SetBorderAndTitleBar(false, false);
                // The sizing frame it keeps taken off too: Windows 10 drew it as a white band along the top (w10 job 703's
                // shot). Its edges size it still - the border regions (UpdateRegions).
                const int WS_THICKFRAME = 0x00040000, WS_DLGFRAME = 0x00400000;
                var style = PInvoke.GetWindowLong(Handle, PInvoke.WindowLongFlags.GWL_STYLE);
                PInvoke.SetWindowLong(Handle, PInvoke.WindowLongFlags.GWL_STYLE, style & ~(WS_THICKFRAME | WS_DLGFRAME));
                PInvoke.SetWindowPos(Handle, 0, 0, 0, 0, 0, PInvoke.SetWindowPosFlags.SWP_FRAMECHANGED | PInvoke.SetWindowPosFlags.SWP_NOMOVE | PInvoke.SetWindowPosFlags.SWP_NOSIZE | PInvoke.SetWindowPosFlags.SWP_NOZORDER | PInvoke.SetWindowPosFlags.SWP_NOACTIVATE);
                AppWindow.IsShownInSwitchers = false;
            }
            PInvoke.SetForegroundWindow(Handle);
            box.Focus(FocusState.Programmatic);
            box.Select(box.Text.Length, 0);
            UpdateRegions();
            Log.Debug($"scratchpad: shown ({box.Text.Length} characters, on top {pin.IsChecked == true})");
        }

        // Where it was the last time; the first time, on the right of the main window's screen, a third of its height.
        private void Place(SettingsViewModel settings)
        {
            if (settings?.ScratchpadPlacement is { } saved)
            {
                saved.showCmd = PInvoke.ShowWindowCommand.Hide;
                PInvoke.SetWindowPlacement(Handle, ref saved);
                return;
            }
            var scale = Math.Max(96, PInvoke.GetDpiForWindow(Handle)) / 96.0;
            var owner = MainWindow.LastActive ?? MainWindow.AllWindows.FirstOrDefault();
            var area = (owner != null ? DisplayArea.GetFromWindowId(owner.AppWindow.Id, DisplayAreaFallback.Primary) : DisplayArea.Primary).WorkArea;
            var size = new SizeInt32((int)(360 * scale), (int)(440 * scale));
            var margin = (int)(24 * scale);
            AppWindow.MoveAndResize(new RectInt32(area.X + area.Width - size.Width - margin, area.Y + area.Height / 3 - size.Height / 3, size.Width, size.Height));
        }

        private void Hide()
        {
            if (!shown) return;
            shown = false;
            SavePlacement();
            shownWith.Clear();
            _ = Scratchpad.Shared.FlushAsync();
            AppWindow.Hide();
            Log.Debug("scratchpad: hidden");
            // The main window used last given the keyboard back (the system gave it to whichever window was behind).
            if (MainWindow.LastActive is { IsGoingAway: false } last) PInvoke.SetForegroundWindow(last.Handle);
        }

        private void SetTopmost(bool on)
        {
            if (AppWindow.Presenter is OverlappedPresenter presenter) presenter.IsAlwaysOnTop = on;
        }

        private void SetTheme(AppTheme theme)
        {
            root.RequestedTheme = theme switch
            {
                AppTheme.Light => ElementTheme.Light,
                AppTheme.Dark => ElementTheme.Dark,
                AppTheme.Black => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };
            ApplyColours();
        }

        // The custom theme's colours, as the dialogs take them (its panels' colour, else its window's; its text; its
        // separators; its accent for the selection); what it does not set, light or dark.
        private void ApplyColours()
        {
            try
            {
                var dark = root.ActualTheme == ElementTheme.Dark;
                var theme = ThemeFiles.Find(Settings()?.CustomTheme);
                static Color? Of(Brush brush) => (brush as SolidColorBrush)?.Color;
                paper.Color = Of(ThemeFiles.Brush(theme?.Surface)) ?? Of(ThemeFiles.Brush(theme?.Background)) ?? (dark ? Color.FromArgb(0xFF, 0x20, 0x20, 0x20) : Color.FromArgb(0xFF, 0xFA, 0xFA, 0xFA));
                var text = Of(ThemeFiles.Brush(theme?.Foreground)) ?? (theme != null ? Of(ThemeFiles.Readable(theme.Surface ?? theme.Background)) : null) ?? (dark ? Microsoft.UI.Colors.White : Color.FromArgb(0xE4, 0, 0, 0));
                ink.Color = text;
                faint.Color = Color.FromArgb(0x99, text.R, text.G, text.B);
                line.Color = Of(ThemeFiles.Brush(theme?.Border)) ?? (dark ? Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x26, 0, 0, 0));
                if (ThemeFiles.Brush(theme?.Accent) is SolidColorBrush accent) box.SelectionHighlightColor = accent;
                else box.ClearValue(TextBox.SelectionHighlightColorProperty);
            }
            catch (Exception ex)
            {
                Log.Debug($"scratchpad: colours: {ex.Message}");
            }
        }

        private void SavePlacement()
        {
            var settings = Settings();
            if (settings == null || Handle == 0) return;
            PInvoke.GetWindowPlacement(Handle, out var placement);
            settings.ScratchpadPlacement = placement;
        }

        private Microsoft.UI.Dispatching.DispatcherQueueTimer saveSoon;

        // Moved or resized: saved a moment after it stops (not every step of a drag).
        private void SavePlacementSoon()
        {
            if (saveSoon == null)
            {
                saveSoon = DispatcherQueue.CreateTimer();
                saveSoon.Interval = TimeSpan.FromMilliseconds(600);
                saveSoon.IsRepeating = false;
                saveSoon.Tick += (s, e) => SavePlacement();
            }
            saveSoon.Stop();
            saveSoon.Start();
        }

        // Its key (View > Scratchpad's, changeable) or Esc: hidden. Its key reaches no main window from here (they take
        // keys only when in front).
        private void OnKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Escape)
            {
                e.Handled = true;
                Hide();
                return;
            }
            var key = Settings()?.ShortcutScratchpad;
            if (key == null || e.Key != key.Key || e.KeyStatus.WasKeyDown) return;
            var mods = VirtualKeyModifiers.None;
            if (Down(VirtualKey.Control)) mods |= VirtualKeyModifiers.Control;
            if (Down(VirtualKey.Shift)) mods |= VirtualKeyModifiers.Shift;
            if (Down(VirtualKey.Menu)) mods |= VirtualKeyModifiers.Menu;
            if (Down(VirtualKey.LeftWindows) || Down(VirtualKey.RightWindows)) mods |= VirtualKeyModifiers.Windows;
            if (mods != key.Modifiers) return;
            e.Handled = true;
            Hide();
        }

        private static bool Down(VirtualKey key) => InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

        // Changed elsewhere (Put in Scratchpad, an automation): the box shows it, the caret kept where it can be.
        private void OnStoreChanged(object source)
        {
            if (source == box) return;
            void Apply()
            {
                var text = Scratchpad.Shared.Text;
                if (ScratchpadStore.Lines(box.Text) == text) return;
                var caret = box.SelectionStart;
                box.Text = text;
                box.Select(Math.Min(caret, text.Length), 0);
                // Something put at the end: the end shown.
                if (text.Length > caret) box.Select(text.Length, 0);
            }
            if (DispatcherQueue.HasThreadAccess) Apply();
            else DispatcherQueue.TryEnqueueSafe(Apply);
        }

        /// <summary>For the test host: shown, its window, on top, in the taskbar, its colours, the text.</summary>
        public static object State() => new
        {
            shown = instance?.shown == true,
            hwnd = instance == null ? 0L : (long)instance.Handle,
            topmost = instance?.AppWindow.Presenter is OverlappedPresenter p && p.IsAlwaysOnTop,
            inSwitchers = instance?.AppWindow.IsShownInSwitchers ?? true,
            visible = instance != null && PInvoke.IsWindowVisible(instance.Handle),
            bounds = instance == null ? null : $"{instance.AppWindow.Position.X},{instance.AppWindow.Position.Y} {instance.AppWindow.Size.Width}x{instance.AppWindow.Size.Height}",
            background = instance == null ? null : Hex(instance.paper.Color),
            foreground = instance == null ? null : Hex(instance.ink.Color),
            text = Scratchpad.Shared.Text,
        };

        private static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }
}
