using System;
using System.Linq;
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
            Loaded += (_, _) => { ApplyCurrentTheme(); UpdateReadingTip(); FollowHeadings(Editor); WatchForLayout(); };
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

        private string FormatText(TextFileFormat format) => format == null ? "" : $"{format.EncodingLabel} \u00B7 {format.LineEndingLabel}";

        /// <summary>The line endings to write, converting to UTF-8, reading the file again in another encoding.</summary>
        private void OnFileFormatMenuOpening(object sender, object e)
        {
            FileFormatMenu.Items.Clear();
            AddFileFormatItems(FileFormatMenu.Items);
        }

        private void AddFileFormatItems(System.Collections.Generic.IList<MenuFlyoutItemBase> items)
        {
            var file = ViewModel?.FileViewModel;
            if (file == null) return;
            var format = file.FileFormat ?? TextFileFormat.Default;
            foreach (var (label, ending) in new[] { ("LF", "\n"), ("CRLF", "\r\n") })
            {
                var item = new RadioMenuFlyoutItem { Text = label, GroupName = "LineEnding", IsChecked = format.LineEnding == ending };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(item, "LineEnding" + label);
                item.Click += (_, _) => file.ChangeLineEnding(ending);
                items.Add(item);
            }
            items.Add(new MenuFlyoutSeparator());
            var convert = new MenuFlyoutItem { Text = Locale.GetString("Encoding.ConvertToUtf8"), IsEnabled = format.EncodingLabel != "UTF-8" || format.LossyDecode };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(convert, "ConvertToUtf8");
            convert.Click += (_, _) => file.ConvertToUtf8();
            items.Add(convert);
            // Another encoding only for a file on disk with no byte order mark (one with a mark says what it is), and
            // with nothing unsaved, which reading it again would throw away.
            var reopen = new MenuFlyoutSubItem
            {
                Text = Locale.GetString("Encoding.ReopenWith"),
                IsEnabled = file.FilePath != null && !format.HasByteOrderMark && Editor?.Saved == true,
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(reopen, "ReopenWithEncoding");
            foreach (var name in TextFileFormat.ReopenEncodings)
            {
                var item = new MenuFlyoutItem { Text = name.ToUpperInvariant() };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(item, "Reopen-" + name);
                item.Click += async (_, _) => await file.ReopenWithEncodingAsync(name);
                reopen.Items.Add(item);
            }
            items.Add(reopen);
        }

        // Saved; not saved yet; auto-save held back (the file would have been damaged, or writing it failed).
        private string SaveStateText(bool saved, string heldBack) =>
            Locale.GetString(saved ? "SaveState.Saved" : heldBack != null ? "SaveState.Paused" : "SaveState.Unsaved");

        private string SaveStateGlyph(bool saved, string heldBack) => saved ? "\uE73E" : heldBack != null ? "\uE7BA" : "\uE91F";

        private string SaveStateTip(bool saved, string heldBack)
        {
            if (saved) return Locale.GetString("SaveState.Saved");
            var keys = this.GetService<Interfaces.IKeyboardAccelerator>();
            var shortcut = Settings == null ? null : keys?.GetShortcutKeyText(Settings.ShortcutSave);
            var click = Locale.GetString("SaveState.ClickToSave") + (string.IsNullOrEmpty(shortcut) ? "" : $" ({shortcut})");
            return heldBack != null ? heldBack + "\n" + click : click;
        }

        private void OnSaveStateClick(object sender, RoutedEventArgs e)
        {
            ViewModel?.FileViewModel?.SaveCommand.Execute(System.Reactive.Unit.Default);
        }

        /// <summary>
        /// The status bar's number: characters or words, as the reader chose ("1,234 characters"); with a selection, the
        /// selection's of the whole ("120 of 1,234 characters selected").
        /// </summary>
        private string CountText(Models.ContentState state, int method, string selection)
        {
            var count = state?.WordCount;
            if (count == null) return "";
            var total = method == 1 ? count.Word : count.Character;
            var unit = method == 1 ? WordUnit(total) : CharacterUnit(total);
            var (words, characters) = TextCount.Count(selection);
            var selected = method == 1 ? words : characters;
            return selected > 0
                ? string.Format(Locale.GetString("Stats.SelectedOfTotal"), selected.ToString("N0"), total.ToString("N0"), unit)
                : $"{total:N0} {unit}";
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

        // The editor whose headings the bar follows (remembered: the data context is gone by the time the bar unloads).
        private EditorViewModel followed;

        private void FollowHeadings(EditorViewModel editor)
        {
            if (followed == editor) return;
            if (followed != null)
            {
                followed.OutlineHighlighted -= OnHeadingHighlighted;
                followed.OutlineUpdated -= OnOutlineUpdated;
            }
            followed = editor;
            if (followed != null)
            {
                followed.OutlineHighlighted += OnHeadingHighlighted;
                followed.OutlineUpdated += OnOutlineUpdated;
            }
        }

        private void OnHeadingHighlighted(string slug)
        {
            var heading = followed?.ContentState?.Toc?.FirstOrDefault(x => x.Slug == slug)?.Content;
            HeadingText.Text = string.IsNullOrWhiteSpace(heading) ? "" : "\u00A7 " + heading.Trim();
            headingAvailable = !string.IsNullOrWhiteSpace(heading);
            LayOutItemsSoon();
        }

        // A document with no headings (another tab, the last one deleted) shows none.
        private void OnOutlineUpdated()
        {
            if (followed?.ContentState?.Toc is not { Count: > 0 } && headingAvailable)
            {
                headingAvailable = false;
                LayOutItemsSoon();
            }
        }

        // ---- which items show: the reader's choice (right-click), then what the width leaves room for ----

        private static readonly string[] ItemNames = { "modes", "heading", "format", "save", "count" };

        // The first to go when the bar is too narrow; the count stays.
        private static readonly string[] SqueezeOrder = { "heading", "format", "save", "modes" };

        private readonly System.Collections.Generic.HashSet<string> squeezed = new();

        private bool headingAvailable;

        private FrameworkElement[] ElementsOf(string item) => item switch
        {
            "modes" => new FrameworkElement[] { ModeButton, ReadingButton },
            "heading" => new FrameworkElement[] { HeadingButton },
            "format" => new FrameworkElement[] { FileFormatButton },
            "save" => new FrameworkElement[] { SaveStateButton },
            _ => new FrameworkElement[] { WordCountButton },
        };

        private bool Chosen(string item) =>
            !(Settings?.StatusBarHiddenItems ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(item);

        private bool Wanted(string item) => Chosen(item) && (item != "heading" || headingAvailable);

        private System.ComponentModel.INotifyPropertyChanged watchedSettings, watchedEditor, watchedFile;

        // What changes an item's width or presence: the reader's choice, the modes, the count, the save state, the format.
        private void WatchForLayout()
        {
            void Watch(ref System.ComponentModel.INotifyPropertyChanged slot, System.ComponentModel.INotifyPropertyChanged source)
            {
                if (slot != null) slot.PropertyChanged -= OnLayoutSourceChanged;
                slot = source;
                if (slot != null) slot.PropertyChanged += OnLayoutSourceChanged;
            }
            Watch(ref watchedSettings, Settings);
            Watch(ref watchedEditor, Editor);
            Watch(ref watchedFile, ViewModel?.FileViewModel);
            LayOutItemsSoon();
        }

        private void OnLayoutSourceChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(SettingsViewModel.StatusBarHiddenItems) or nameof(SettingsViewModel.ReadOnly) or nameof(SettingsViewModel.WordCountMethod)
                or nameof(EditorViewModel.ContentState) or nameof(EditorViewModel.Saved) or nameof(EditorViewModel.AutoSaveHeldBack) or nameof(EditorViewModel.SelectionText)
                or nameof(FileViewModel.FileFormat))
                LayOutItemsSoon();
        }

        private void OnBarSizeChanged(object sender, SizeChangedEventArgs e) => LayOutItems();

        // After the bindings have put the new text in.
        private void LayOutItemsSoon() => DispatcherQueue?.TryEnqueue(LayOutItems);

        /// <summary>
        /// Every item the reader chose, then, while the two sides do not fit, the next of <see cref="SqueezeOrder"/>
        /// into the "..." menu.
        /// </summary>
        private void LayOutItems()
        {
            if (Settings == null || RootGrid == null) return;
            squeezed.Clear();
            foreach (var item in ItemNames)
                foreach (var element in ElementsOf(item))
                    element.Visibility = Wanted(item) && (element != ReadingButton || !Settings.ReadOnly) ? Visibility.Visible : Visibility.Collapsed;
            OverflowButton.Visibility = Visibility.Collapsed;
            var available = RootGrid.ActualWidth - RootGrid.Padding.Left - RootGrid.Padding.Right;
            if (available <= 0) return;
            double Width(UIElement element)
            {
                element.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
                return element.DesiredSize.Width;
            }
            foreach (var item in SqueezeOrder)
            {
                if (Width(LeftItems) + Width(RightItems) + 16 <= available) break;
                if (!Wanted(item)) continue;
                squeezed.Add(item);
                foreach (var element in ElementsOf(item)) element.Visibility = Visibility.Collapsed;
                OverflowButton.Visibility = Visibility.Visible;
            }
        }

        /// <summary>The items the width left out, as menu entries doing what a click on them does.</summary>
        private void OnOverflowMenuOpening(object sender, object e)
        {
            OverflowMenu.Items.Clear();
            MenuFlyoutItem Entry(string id, string text, Action click)
            {
                var item = new MenuFlyoutItem { Text = text };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(item, id);
                item.Click += (_, _) => click();
                return item;
            }
            if (squeezed.Contains("modes") && Settings != null)
            {
                var modes = new MenuFlyoutSubItem { Text = Locale.GetString("View") };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(modes, "OverflowModes");
                var caretModes = !Settings.ReadOnly && !Settings.SourceCode;
                foreach (var (key, on, enabled, set) in new (string, bool, bool, Action<bool>)[]
                {
                    ("SourceCodeMode", Settings.SourceCode, true, v => Settings.SourceCode = v),
                    ("ReadOnlyMode", Settings.ReadOnly, true, v => Settings.ReadOnly = v),
                    ("FocusMode", Settings.FocusMode, caretModes, v => Settings.FocusMode = v),
                    ("TypewriterMode", Settings.Typewriter, caretModes, v => Settings.Typewriter = v),
                })
                {
                    var toggle = new ToggleMenuFlyoutItem { Text = Locale.GetString(key), IsChecked = on, IsEnabled = enabled };
                    toggle.Click += (s, _) => set(((ToggleMenuFlyoutItem)s).IsChecked);
                    modes.Items.Add(toggle);
                }
                OverflowMenu.Items.Add(modes);
            }
            if (squeezed.Contains("save") && Editor != null)
                OverflowMenu.Items.Add(Entry("OverflowSave", SaveStateText(Editor.Saved, Editor.AutoSaveHeldBack), () => OnSaveStateClick(this, null)));
            if (squeezed.Contains("format") && ViewModel?.FileViewModel != null)
            {
                var format = new MenuFlyoutSubItem { Text = FormatText(ViewModel.FileViewModel.FileFormat) };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(format, "OverflowFormat");
                AddFileFormatItems(format.Items);
                OverflowMenu.Items.Add(format);
            }
            if (squeezed.Contains("heading"))
                OverflowMenu.Items.Add(Entry("OverflowHeading", HeadingText.Text, () => OnHeadingClick(this, null)));
        }

        /// <summary>Right-click on the bar: each item, ticked when it shows.</summary>
        private void OnItemsMenuOpening(object sender, object e)
        {
            ItemsMenu.Items.Clear();
            foreach (var (item, key) in new[] { ("modes", "View"), ("heading", "StatusBarItem.CurrentHeading"), ("format", "Encoding.Tip"), ("save", "StatusBarItem.SaveState"), ("count", "WordCount") })
            {
                var toggle = new ToggleMenuFlyoutItem { Text = Locale.GetString(key), IsChecked = Chosen(item) };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(toggle, "StatusItem-" + item);
                toggle.Click += (s, _) =>
                {
                    if (Settings == null) return;
                    var hidden = ItemNames.Where(x => x == item ? !((ToggleMenuFlyoutItem)s).IsChecked : !Chosen(x));
                    Settings.StatusBarHiddenItems = string.Join(",", hidden);
                };
                ItemsMenu.Items.Add(toggle);
            }
        }

        private void OnHeadingClick(object sender, RoutedEventArgs e)
        {
            if (Settings == null) return;
            Settings.SidePaneIndex = 1;
            Settings.SidePaneOpen = true;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            FollowHeadings(null);
            foreach (var source in new[] { watchedSettings, watchedEditor, watchedFile })
                if (source != null) source.PropertyChanged -= OnLayoutSourceChanged;
            watchedSettings = watchedEditor = watchedFile = null;
            Bindings?.StopTracking();
        }
    }
}
