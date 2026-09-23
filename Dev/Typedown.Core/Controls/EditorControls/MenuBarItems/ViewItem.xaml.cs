namespace Typedown.Core.Controls.EditorControls.MenuBarItems
{
    public sealed partial class ViewItem : MenuBarItemBase
    {
        public ViewItem()
        {
            InitializeComponent();
        }

        protected override void OnRegisterShortcut()
        {
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
