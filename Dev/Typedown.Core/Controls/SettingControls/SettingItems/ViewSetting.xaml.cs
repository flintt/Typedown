using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using Typedown.Core.Utilities;
using Typedown.Core.ViewModels;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Typedown.Core.Controls.SettingControls.SettingItems
{
    public sealed partial class ViewSetting : UserControl
    {
        public AppViewModel ViewModel => DataContext as AppViewModel;

        public SettingsViewModel Settings => ViewModel?.SettingsViewModel;

        /// <summary>The themes in the list, in the same order; the first entry is "no custom theme".</summary>
        private readonly List<CustomTheme> themes = new();

        private bool fillingThemes;

        public ViewSetting()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                FillThemes();
                if (Settings != null) Settings.PropertyChanged += OnSettingsChanged;
            };
            Unloaded += (_, _) =>
            {
                if (Settings != null) Settings.PropertyChanged -= OnSettingsChanged;
            };
        }

        private void OnSettingsChanged(object sender, PropertyChangedEventArgs e)
        {
            // Picking a built-in theme in the radio buttons turns the custom theme off, otherwise the two
            // disagree: the radio says "Light" while a custom theme is still painting everything.
            if (e.PropertyName != nameof(Settings.AppTheme) || applyingTheme || Settings == null) return;
            if (string.IsNullOrEmpty(Settings.CustomTheme)) return;
            Settings.CustomTheme = string.Empty;
            fillingThemes = true;
            CustomThemeBox.SelectedIndex = 0;
            fillingThemes = false;
        }

        private bool applyingTheme;

        private void FillThemes()
        {
            if (Settings == null) return;
            fillingThemes = true;
            try
            {
                ThemeFiles.EnsureFolder();
                themes.Clear();
                themes.AddRange(ThemeFiles.List());
                CustomThemeBox.Items.Clear();
                CustomThemeBox.Items.Add(Locale.GetString("View.CustomTheme.None"));
                foreach (var theme in themes) CustomThemeBox.Items.Add(theme.Name);
                var index = themes.FindIndex(x => x.Id == Settings.CustomTheme);
                CustomThemeBox.SelectedIndex = index < 0 ? 0 : index + 1;
            }
            catch (Exception ex)
            {
                Log.Debug($"fill themes: {ex.Message}");
            }
            finally
            {
                fillingThemes = false;
            }
        }

        private void OnCustomThemeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (fillingThemes || Settings == null) return;
            var index = CustomThemeBox.SelectedIndex - 1;
            if (index < 0 || index >= themes.Count)
            {
                Settings.CustomTheme = string.Empty;
                return;
            }
            var theme = themes[index];
            // The theme names the built-in theme it builds on, and that one also colours the window around it.
            // The flag keeps this assignment from being read as "the user picked a built-in theme".
            applyingTheme = true;
            try
            {
                Settings.AppTheme = theme.Base;
            }
            finally
            {
                applyingTheme = false;
            }
            Settings.CustomTheme = theme.Id;
        }

        private void OnOpenThemeFolder(object sender, RoutedEventArgs e)
        {
            try
            {
                ThemeFiles.EnsureFolder();
                Process.Start(new ProcessStartInfo(ThemeFiles.Folder) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Debug($"open theme folder: {ex.Message}");
            }
        }

        private void OnRefreshThemes(object sender, RoutedEventArgs e)
        {
            FillThemes();
            // re-apply, so edits to the file that is already selected show up
            Settings?.OnPropertyChanged(nameof(Settings.CustomTheme), null, Settings.CustomTheme);
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            Bindings?.StopTracking();
        }
    }
}
