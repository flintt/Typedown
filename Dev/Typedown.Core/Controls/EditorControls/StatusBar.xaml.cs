using System;
using Typedown.Core.Utilities;
using Typedown.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Typedown.Core.Controls
{
    public sealed partial class StatusBar : UserControl
    {
        /// <summary>
        /// Paints this bar from a custom theme. Null puts the colours of the built-in theme back; the inner
        /// grid carries the visible background, so setting the control's own is not enough.
        /// </summary>
        public void ApplyThemeBrushes(Microsoft.UI.Xaml.Media.Brush background, Microsoft.UI.Xaml.Media.Brush foreground)
        {
            if (RootGrid != null)
            {
                if (background != null) RootGrid.Background = background;
                else RootGrid.ClearValue(Microsoft.UI.Xaml.Controls.Panel.BackgroundProperty);
            }
            if (foreground != null) Foreground = foreground;
            else ClearValue(ForegroundProperty);
        }

        public AppViewModel ViewModel => DataContext as AppViewModel;

        public SettingsViewModel Settings => ViewModel?.SettingsViewModel;

        public EditorViewModel Editor => ViewModel?.EditorViewModel;

        public StatusBar()
        {
            this.InitializeComponent();
            // Switching the status bar off and on again builds a new control, which knows nothing of the
            // theme that was applied to the old one — so it asks for the colours itself when it loads.
            Loaded += (_, _) => { ApplyCurrentTheme(); UpdateReadingTip(); };
        }

        private void ApplyCurrentTheme()
        {
            try
            {
                var theme = ThemeFiles.Find(Settings?.CustomTheme);
                ApplyThemeBrushes(
                    ThemeFiles.Brush(theme?.Surface) ?? ThemeFiles.Brush(theme?.Background),
                    ThemeFiles.Brush(theme?.Foreground) ?? ThemeFiles.Readable(theme?.Surface ?? theme?.Background));
            }
            catch (Exception ex)
            {
                Log.Debug($"status bar theme: {ex.Message}");
            }
        }

        /// <summary>The badge is also the way out: clicking it puts the document back into editing.</summary>
        private void OnLeaveReadOnlyClick(object sender, RoutedEventArgs e)
        {
            if (Settings != null)
                Settings.ReadOnly = false;
        }

        private void OnEnterReadOnlyClick(object sender, RoutedEventArgs e)
        {
            if (Settings != null)
                Settings.ReadOnly = true;
        }

        /// <summary>The icon of the mode the document is in: reading, source, or the visual editor.</summary>
        private string ModeGlyph(bool readOnly, bool sourceCode) => readOnly ? "\uE890" : sourceCode ? "\uE943" : "\uE70F";

        /// <summary>Focus and typewriter mode follow the caret: nothing to do in reading or source mode (as in the View menu).</summary>
        private bool CaretModes(bool readOnly, bool sourceCode) => !readOnly && !sourceCode;

        private Visibility WhenEditing(bool readOnly) => readOnly ? Visibility.Collapsed : Visibility.Visible;

        /// <summary>The modes' shortcuts beside them, as they are set now; the reading button says its own.</summary>
        private void OnModeMenuOpening(object sender, object e)
        {
            var keys = this.GetService<Interfaces.IKeyboardAccelerator>();
            if (keys == null || Settings == null) return;
            StatusSourceCodeItem.KeyboardAcceleratorTextOverride = keys.GetShortcutKeyText(Settings.ShortcutSourceCodeMode);
            StatusReadOnlyItem.KeyboardAcceleratorTextOverride = keys.GetShortcutKeyText(Settings.ShortcutReadOnlyMode);
            StatusFocusItem.KeyboardAcceleratorTextOverride = keys.GetShortcutKeyText(Settings.ShortcutFocusMode);
            StatusTypewriterItem.KeyboardAcceleratorTextOverride = keys.GetShortcutKeyText(Settings.ShortcutTypewriterMode);
        }

        private void UpdateReadingTip()
        {
            var keys = this.GetService<Interfaces.IKeyboardAccelerator>();
            var text = Locale.GetString("ReadOnlyMode");
            var shortcut = Settings == null ? null : keys?.GetShortcutKeyText(Settings.ShortcutReadOnlyMode);
            ToolTipService.SetToolTip(ReadingButton, string.IsNullOrEmpty(shortcut) ? text : $"{text} ({shortcut})");
        }

        /// <summary>The status bar's number: characters or words, as the reader chose ("1,234 characters").</summary>
        private string CountText(Models.ContentState state, int method)
        {
            var count = state?.WordCount;
            if (count == null) return "";
            return method == 1 ? $"{count.Word:N0} {WordUnit(count.Word)}" : $"{count.Character:N0} {CharacterUnit(count.Character)}";
        }

        /// <summary>The document's numbers as they are now, written into the popup each time it opens.</summary>
        private void OnStatsOpening(object sender, object e)
        {
            var count = Editor?.ContentState?.WordCount ?? new Models.WordCount();
            var minutes = count.ReadingMinutes;
            var reading = count.Character == 0 ? "-"
                : minutes < 1 ? Locale.GetString("Stats.UnderAMinute")
                : string.Format(Locale.GetString("Stats.AboutMinutes"), Math.Round(minutes).ToString("N0"));
            var rows = new (string Key, string Value)[]
            {
                ("Stats.Words", count.Word.ToString("N0")),
                ("Stats.CharactersNoSpaces", count.Character.ToString("N0")),
                ("Stats.CharactersWithSpaces", count.CharacterWithSpaces.ToString("N0")),
                ("Stats.CjkCharacters", count.Cjk.ToString("N0")),
                ("Stats.OtherWords", count.OtherWords.ToString("N0")),
                ("Stats.Paragraphs", count.Paragraph.ToString("N0")),
                ("Stats.Headings", count.Headings.ToString("N0")),
                ("Stats.Images", count.Images.ToString("N0")),
                ("Stats.Links", count.Links.ToString("N0")),
                ("Stats.ReadingTime", reading),
            };
            StatsGrid.Children.Clear();
            StatsGrid.RowDefinitions.Clear();
            for (var i = 0; i < rows.Length; i++)
            {
                StatsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var label = new TextBlock { Text = Locale.GetString(rows[i].Key), Style = (Style)Resources["StatsLabelStyle"] };
                var value = new TextBlock { Text = rows[i].Value, HorizontalAlignment = HorizontalAlignment.Right };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(value, rows[i].Key);
                Grid.SetRow(label, i);
                Grid.SetRow(value, i);
                Grid.SetColumn(value, 1);
                StatsGrid.Children.Add(label);
                StatsGrid.Children.Add(value);
            }
            settingCountMethod = true;
            ShowCharacters.IsChecked = Settings?.WordCountMethod != 1;
            ShowWords.IsChecked = Settings?.WordCountMethod == 1;
            settingCountMethod = false;
        }

        private bool settingCountMethod;

        private void OnCountMethodChecked(object sender, RoutedEventArgs e)
        {
            if (settingCountMethod || Settings == null) return;
            Settings.WordCountMethod = sender == ShowWords ? 1 : 0;
        }

        private string CharacterUnit(int number) => number != 1 ? Locale.GetString("Characters") : Locale.GetString("Character");

        private string WordUnit(int number) => number != 1 ? Locale.GetString("Words") : Locale.GetString("Word");

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            Bindings?.StopTracking();
        }
    }
}
