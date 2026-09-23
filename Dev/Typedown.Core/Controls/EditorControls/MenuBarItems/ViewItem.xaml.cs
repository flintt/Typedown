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
            if (Settings != null)
            {
                Settings.PropertyChanged -= OnSettingsChanged; // OnRegisterShortcut runs again on every load
                Settings.PropertyChanged += OnSettingsChanged;
            }
            RegisterWindowShortcut(Settings.ShortcutSidePane, SidePaneItem);
            RegisterWindowShortcut(Settings.ShortcutSourceCodeMode, SourceCodeModeItem);
            RegisterWindowShortcut(Settings.ShortcutFocusMode, FocusModeItem);
            RegisterWindowShortcut(Settings.ShortcutTypewriterMode, TypewriterModeItem);
            RegisterWindowShortcut(Settings.ShortcutStatusBar, StatusBarItem);
            RegisterWindowShortcut(Settings.ShortcutFullScreen, FullScreenItem);
            RegisterWindowShortcut(Settings.ShortcutReadOnlyMode, ReadOnlyModeItem);
            RegisterWindowShortcut(Settings.ShortcutNextTab, NextTabItem);
            RegisterWindowShortcut(Settings.ShortcutPreviousTab, PreviousTabItem);
        }

        private void OnSettingsChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // The menu bar can be torn down while the settings object lives on, and then this handler would run
            // against a control whose DataContext is gone: drop the subscription instead.
            if (Settings == null)
            {
                if (sender is ViewModels.SettingsViewModel settings)
                    settings.PropertyChanged -= OnSettingsChanged;
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
            if (Settings != null) Settings.PropertyChanged -= OnSettingsChanged;
            Bindings?.StopTracking();
        }
    }
}
