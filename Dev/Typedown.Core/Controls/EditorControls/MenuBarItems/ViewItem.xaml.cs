using System.Collections.Generic;
using System.Reflection;
using Typedown.Core.Utilities;
using Windows.UI.Xaml.Controls;
using muxc = Microsoft.UI.Xaml.Controls;

namespace Typedown.Core.Controls.EditorControls.MenuBarItems
{
    public sealed partial class ViewItem : MenuBarItemBase
    {
        public ViewItem()
        {
            InitializeComponent();
        }

        /// <summary>The theme entries, so a change can tick the right one without rebuilding the open menu.</summary>
        private readonly List<(muxc.RadioMenuFlyoutItem Item, string CustomId, Enums.AppTheme? BuiltIn)> themeItems = new();

        private void BuildThemeMenu()
        {
            if (Settings == null || ThemeSubMenu == null) return;
            ThemeSubMenu.Items.Clear();
            themeItems.Clear();
            foreach (var value in Enums.Enumerable.AppThemes)
            {
                var theme = value;
                var item = new muxc.RadioMenuFlyoutItem { Text = EnumName(theme), GroupName = "AppTheme" };
                item.Click += (_, _) => { Settings.CustomTheme = string.Empty; Settings.AppTheme = theme; };
                themeItems.Add((item, null, theme));
                ThemeSubMenu.Items.Add(item);
            }
            var customThemes = ThemeFiles.List();
            if (customThemes.Count > 0)
            {
                ThemeSubMenu.Items.Add(new MenuFlyoutSeparator());
                foreach (var custom in customThemes)
                {
                    var id = custom.Id;
                    var item = new muxc.RadioMenuFlyoutItem { Text = ThemeFiles.DisplayName(custom, customThemes), GroupName = "AppTheme" };
                    item.Click += (_, _) => Settings.CustomTheme = id;
                    themeItems.Add((item, id, null));
                    ThemeSubMenu.Items.Add(item);
                }
            }
            ThemeSubMenu.Items.Add(new MenuFlyoutSeparator());
            // Picks up a theme that was just added or edited without restarting. The rebuild waits for the click
            // to be over: the menu would otherwise be torn down while it is still on screen.
            var reload = new MenuFlyoutItem { Text = Locale.GetString("View.CustomTheme.Refresh") };
            reload.Click += (_, _) => _ = Dispatcher.RunIdleAsync(_ => BuildThemeMenu());
            ThemeSubMenu.Items.Add(reload);
            UpdateThemeChecks();
        }

        /// <summary>The name a theme goes by in the menu, from the enum's own locale attribute.</summary>
        private static string EnumName(Enums.AppTheme theme)
        {
            var field = typeof(Enums.AppTheme).GetField(theme.ToString());
            var attribute = field?.GetCustomAttribute(typeof(LocaleAttribute)) as LocaleAttribute;
            return attribute?.Text ?? theme.ToString();
        }

        private void UpdateThemeChecks()
        {
            if (Settings == null) return;
            var custom = Settings.CustomTheme;
            foreach (var (item, id, builtIn) in themeItems)
                item.IsChecked = id != null
                    ? id == custom
                    : string.IsNullOrEmpty(custom) && builtIn == Settings.AppTheme;
        }

        protected override void OnRegisterShortcut()
        {
            BuildThemeMenu();
            // Focus and typewriter mode both follow the caret, so they mean nothing in reading mode (which has
            // no caret) or in source mode (which is a plain text editor): grey them out instead of letting them
            // look enabled while doing nothing. The settings themselves are kept for when the mode is left.
            UpdateModeAvailability();
            Subscribe(Settings);
            RegisterWindowShortcut(Settings.ShortcutSidePane, SidePaneItem);
            RegisterWindowShortcut(Settings.ShortcutSourceCodeMode, SourceCodeModeItem);
            RegisterWindowShortcut(Settings.ShortcutFocusMode, FocusModeItem);
            RegisterWindowShortcut(Settings.ShortcutTypewriterMode, TypewriterModeItem);
            RegisterWindowShortcut(Settings.ShortcutStatusBar, StatusBarItem);
            RegisterWindowShortcut(Settings.ShortcutFullScreen, FullScreenItem);
            RegisterWindowShortcut(Settings.ShortcutReadOnlyMode, ReadOnlyModeItem);
            RegisterWindowShortcut(Settings.ShortcutNextTab, NextTabItem);
            RegisterWindowShortcut(Settings.ShortcutPreviousTab, PreviousTabItem);
            RegisterTabNumberShortcuts();
            RegisterWindowShortcut(new Models.ShortcutKey(Windows.System.VirtualKeyModifiers.Menu, Windows.System.VirtualKey.Number0),
                () => ViewModel?.TabsViewModel?.LastUsedTabCommand.Execute(default));
        }

        /// <summary>
        /// Alt+1..9 switch to a tab by position, 9 being the last one however many there are. Alt rather than
        /// Ctrl because Ctrl+1..6 already sets the heading level. They are fixed, not bindable, so they have no
        /// menu entry.
        /// </summary>
        private void RegisterTabNumberShortcuts()
        {
            var tabs = ViewModel?.TabsViewModel;
            if (tabs == null) return;
            for (var n = 1; n <= 9; n++)
            {
                var index = n == 9 ? int.MaxValue : n - 1;
                var key = new Models.ShortcutKey(Windows.System.VirtualKeyModifiers.Menu,
                    (Windows.System.VirtualKey)((int)Windows.System.VirtualKey.Number0 + n));
                RegisterWindowShortcut(key, () => tabs.SwitchTabIndexCommand.Execute(index));
            }
        }

        /// <summary>
        /// The settings outlive this control, so the subscription is remembered rather than looked up again on
        /// the way out: the data context is already gone by the time a torn-down menu bar unloads, and a
        /// subscription that cannot be found is a subscription that is never removed — the stale handler then
        /// runs on the next mode switch with no data context and takes the app down.
        /// </summary>
        private ViewModels.SettingsViewModel subscribed;

        private void Subscribe(ViewModels.SettingsViewModel settings)
        {
            if (subscribed == settings) return;
            Unsubscribe();
            subscribed = settings;
            if (subscribed != null)
                subscribed.PropertyChanged += OnSettingsChanged;
        }

        private void Unsubscribe()
        {
            if (subscribed == null) return;
            subscribed.PropertyChanged -= OnSettingsChanged;
            subscribed = null;
        }

        private void OnSettingsChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (Settings == null)
            {
                Unsubscribe();
                return;
            }
            if (e.PropertyName is nameof(Settings.ReadOnly) or nameof(Settings.SourceCode))
                UpdateModeAvailability();
            if (e.PropertyName is nameof(Settings.AppTheme) or nameof(Settings.CustomTheme))
                UpdateThemeChecks();
        }

        private void UpdateModeAvailability()
        {
            if (Settings == null || FocusModeItem == null || TypewriterModeItem == null)
                return;
            var caretModes = !Settings.ReadOnly && !Settings.SourceCode;
            FocusModeItem.IsEnabled = caretModes;
            TypewriterModeItem.IsEnabled = caretModes;
        }

        private void OnUnloaded(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            Unsubscribe();
            Bindings?.StopTracking();
        }
    }
}
