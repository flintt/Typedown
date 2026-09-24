using System;
using Typedown.Core.Enums;
using Typedown.Core.Utilities;
using Typedown.Core.ViewModels;
using Windows.Globalization;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Typedown.Core.Controls.SettingControls.SettingItems
{
    public sealed partial class GeneralSetting : UserControl
    {
        public AppViewModel ViewModel => DataContext as AppViewModel;

        public SettingsViewModel Settings => ViewModel?.SettingsViewModel;

        public GeneralSetting()
        {
            InitializeComponent();
        }

        public static bool IsStartupOpenFolderItemLoad(FolderStartupAction action)
        {
            return action == FolderStartupAction.OpenFolder;
        }

        /// <summary>
        /// Whether the interface is still in the language it was built with. It used to be answered from
        /// ApplicationLanguages.PrimaryLanguageOverride, which only exists in the packaged build and threw in
        /// the installer one — so the notice never appeared there and the language seemed to change nothing.
        /// </summary>
        private bool IsLangChanged(string settingLang)
        {
            try
            {
                var wanted = Locale.SupportedLangs.ContainsKey(settingLang ?? "") ? settingLang : string.Empty;
                return !string.Equals(wanted, Locale.AppliedLanguage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Starts the program again and closes this one. Every window goes through its usual close, so a
        /// document with unsaved changes still asks — and if that ask is cancelled, nothing is restarted.
        /// </summary>
        private void OnRestartClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var path = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(path)) return;
                Config.RestartOnExit = path;
                ViewModel?.FileViewModel?.ExitCommand.Execute(default);
            }
            catch (Exception ex)
            {
                Log.Debug($"restart: {ex.Message}");
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            Bindings?.StopTracking();
        }
    }
}
