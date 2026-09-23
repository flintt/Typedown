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
            Settings.PropertyChanged -= OnSettingsChanged; // OnRegisterShortcut runs again on every load
            Settings.PropertyChanged += OnSettingsChanged;
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
            if (e.PropertyName is nameof(Settings.ReadOnly) or nameof(Settings.SourceCode))
                UpdateModeAvailability();
        }

        private void UpdateModeAvailability()
        {
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
