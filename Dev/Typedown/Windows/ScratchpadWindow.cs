using System;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Typedown.Core;
using Typedown.Core.Enums;
using Typedown.Core.Services;
using Typedown.Core.Utilities;
using Typedown.Core.ViewModels;
using Windows.Graphics;
using Windows.System;
using Windows.UI.Core;

namespace Typedown.Windows
{
    /// <summary>
    /// The scratchpad (the user's 33, 2026-11-22): a small window of its own with one text box for passages kept for a
    /// while - the same for every window and tab (its text is Services.Scratchpad.Shared, saved as it is typed). Over the
    /// other windows unless its pin is let go; where it was and its size kept. Closed, it is only hidden. It has no
    /// services of its own: the settings it reads and sets (the theme, its pin, its place) are those of a main window open,
    /// which every window shares.
    /// </summary>
    public sealed class ScratchpadWindow : Window
    {
        private static ScratchpadWindow instance;

        private readonly Grid root;
        private readonly TextBox box;
        private readonly ToggleButton pin;
        private readonly CompositeDisposable shownWith = new();
        private bool shown;
        private bool placed;

        private const string Layout = @"
<Grid xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
      xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
      AutomationProperties.AutomationId='ScratchpadWindow'
      Background='{ThemeResource SolidBackgroundFillColorBaseBrush}'>
  <Grid.RowDefinitions>
    <RowDefinition Height='Auto'/>
    <RowDefinition Height='*'/>
  </Grid.RowDefinitions>
  <ToggleButton x:Name='Pin' AutomationProperties.AutomationId='ScratchpadTopmost' HorizontalAlignment='Right' Margin='0,6,8,4'
                Padding='8,4' Background='Transparent' BorderThickness='0'>
    <FontIcon Glyph='&#xE718;' FontSize='14'/>
  </ToggleButton>
  <TextBox x:Name='Box' Grid.Row='1' AutomationProperties.AutomationId='ScratchpadText' AcceptsReturn='True' TextWrapping='Wrap'
           IsSpellCheckEnabled='False' ScrollViewer.VerticalScrollBarVisibility='Auto' Margin='8,0,8,8'/>
</Grid>";

        private ScratchpadWindow()
        {
            root = (Grid)XamlReader.Load(Layout);
            box = (TextBox)root.FindName("Box");
            pin = (ToggleButton)root.FindName("Pin");
            Content = root;
            UiLanguage.Set(root);
            Title = Locale.GetString("Scratchpad");
            box.PlaceholderText = Locale.GetString("ScratchpadPlaceholder");
            var keepOnTop = Locale.GetString("ScratchpadTopmost");
            ToolTipService.SetToolTip(pin, keepOnTop);
            AutomationProperties.SetName(pin, keepOnTop);
            box.Text = Scratchpad.Shared.Text;
            box.TextChanged += (s, e) => Scratchpad.Shared.SetText(box.Text, box);
            Scratchpad.Shared.Changed += OnStoreChanged;
            pin.Click += (s, e) =>
            {
                var settings = Settings();
                if (settings != null) settings.ScratchpadTopmost = pin.IsChecked == true;
                SetTopmost(pin.IsChecked == true);
            };
            root.PreviewKeyDown += OnKeyDown;
            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
                presenter.PreferredMinimumWidth = 220;
                presenter.PreferredMinimumHeight = 160;
            }
            // Closed by its X: hidden, its text and place kept (only the app ending closes it).
            AppWindow.Closing += (s, e) =>
            {
                e.Cancel = true;
                Hide();
            };
            AppWindow.Changed += (s, e) => { if (shown && (e.DidPositionChange || e.DidSizeChange)) SavePlacementSoon(); };
        }

        private nint Handle => WinRT.Interop.WindowNative.GetWindowHandle(this);

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
            PInvoke.SetForegroundWindow(Handle);
            box.Focus(FocusState.Programmatic);
            box.Select(box.Text.Length, 0);
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
            var dark = root.ActualTheme == ElementTheme.Dark;
            uint darkFrame = dark ? 1u : 0u;
            PInvoke.DwmSetWindowAttribute(Handle, Config.WindowsBuild >= 18985 ? PInvoke.DwmWindowAttribute.DWMWA_USE_IMMERSIVE_DARK_MODE : (PInvoke.DwmWindowAttribute)19, ref darkFrame, sizeof(uint));
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
                if (box.Text == text) return;
                var caret = box.SelectionStart;
                box.Text = text;
                box.Select(Math.Min(caret, text.Length), 0);
                // Something put at the end: the end shown.
                if (text.Length > caret) box.Select(text.Length, 0);
            }
            if (DispatcherQueue.HasThreadAccess) Apply();
            else DispatcherQueue.TryEnqueueSafe(Apply);
        }

        /// <summary>For the test host: shown, its window, on top, the text.</summary>
        public static object State() => new
        {
            shown = instance?.shown == true,
            hwnd = instance == null ? 0L : (long)instance.Handle,
            topmost = instance?.AppWindow.Presenter is OverlappedPresenter p && p.IsAlwaysOnTop,
            text = Scratchpad.Shared.Text,
        };
    }
}
