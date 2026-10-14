using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using Typedown.Core.Interfaces;
using Typedown.Core.Models;
using Typedown.Core.Services;
using Typedown.Core.Utilities;
using Typedown.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Typedown.Core.Controls
{
    /// <summary>
    /// The home page laid over the editor (see <see cref="HomeViewModel"/>): New, Open file, Open folder; the favourites;
    /// the recent files with their folder and last change; the recent folders; a line of shortcuts and the way to the
    /// settings. No previews of the files: that would mean reading them (slow, and what is in them is nobody's business
    /// on a start page). The lists are filled as they come (the recent list is read from the database in the background),
    /// so the window never waits for them.
    /// </summary>
    public sealed partial class HomePage : UserControl
    {
        public AppViewModel ViewModel => DataContext as AppViewModel;

        private HomeViewModel Home => ViewModel?.HomeViewModel;

        public ObservableCollection<PathItem> FavoriteItems { get; } = new();

        public ObservableCollection<PathItem> RecentFiles { get; } = new();

        public ObservableCollection<PathItem> RecentFolders { get; } = new();

        /// <summary>How many recent files and folders the page lists.</summary>
        private const int FilesShown = 10, FoldersShown = 6;

        private readonly CompositeDisposable disposables = new();

        private bool refreshQueued;

        public HomePage()
        {
            InitializeComponent();
            // A letter typed while one of the page's buttons or lists has the keyboard: the page goes and the editor takes
            // the keys from now on (that letter itself is not typed: it went to the button).
            AddHandler(CharacterReceivedEvent, new TypedEventHandler<UIElement, CharacterReceivedRoutedEventArgs>(OnCharacterReceived), true);
            AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDown), true);
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            disposables.Clear();
            var home = Home;
            if (home == null) return;
            disposables.Add(home.WhenPropertyChanged(nameof(HomeViewModel.IsVisible)).Subscribe(_ => ApplyVisibility()));
            var history = this.GetService<AccessHistory>();
            if (history != null)
            {
                history.Changed += OnListsChanged;
                disposables.Add(Disposable.Create(() => history.Changed -= OnListsChanged));
            }
            var favorites = Favorites.Shared;
            favorites.Changed += OnListsChanged;
            disposables.Add(Disposable.Create(() => favorites.Changed -= OnListsChanged));
            var settings = ViewModel.SettingsViewModel;
            disposables.Add(settings.WhenPropertyChanged(nameof(SettingsViewModel.CustomTheme)).Subscribe(_ => ApplyTheme()));
            ApplyTheme();
            AddEditionSections();
            ApplyVisibility();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            disposables.Clear();
        }

        // Shown or hidden as the window's home page is. Hidden with the keyboard on it (a button clicked, a list entry
        // opened): the keyboard goes to the editor, which is what is on screen now.
        private void ApplyVisibility()
        {
            var visible = Home?.IsVisible == true;
            if (visible == (Visibility == Visibility.Visible)) return;
            if (visible)
            {
                Visibility = Visibility.Visible;
                Refresh();
                // The keyboard to the page itself: what is typed now is handed on to the editor once the blank document
                // is in it (see OnCharacterReceived). Left in the editor, keys typed before the page had the new document
                // went into the document shown before, or were replaced by the load (E2E HP05). At start-up the editor
                // takes the keyboard back once its page has loaded with the document, which is then safe.
                DispatcherQueue?.TryEnqueue(() =>
                {
                    if (Visibility == Visibility.Visible && !HasKeyboard()) Focus(FocusState.Programmatic);
                });
                return;
            }
            var hadKeyboard = HasKeyboard();
            Visibility = Visibility.Collapsed;
            if (hadKeyboard) ViewModel?.MarkdownEditor?.FocusEditor();
        }

        private bool HasKeyboard()
        {
            if (XamlRoot == null) return false;
            for (var node = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject; node != null; node = VisualTreeHelper.GetParent(node))
                if (node == this) return true;
            return false;
        }

        // Typing on the home page writes in its blank document: each character is kept, and once the editor has the
        // document (its load confirmed) the editor takes the keyboard and the characters are typed into it again, in
        // order. The first of them changes the document, which hides the page. A space on a button or a list entry is
        // that control's own.
        private void OnCharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs e)
        {
            if (char.IsControl(e.Character)) return;
            if (e.Character == ' ' && FocusManager.GetFocusedElement(XamlRoot) is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase or ListViewItem) return;
            e.Handled = true;
            pending.Append(e.Character);
            HandOn();
        }

        // Ctrl+V (Ctrl+Shift+V: as plain text) on the home page: pasted into its document, as in the editor. The
        // window's paste shortcut only acts with the keyboard in the editor.
        private void OnKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != Windows.System.VirtualKey.V) return;
            var ctrl = PInvoke.GetIsKeyDown(Windows.System.VirtualKey.Control);
            if (!ctrl) return;
            var shift = PInvoke.GetIsKeyDown(Windows.System.VirtualKey.Shift);
            e.Handled = true;
            _ = PasteIntoDocumentAsync(shift ? "pasteAsPlainText" : "normal");
        }

        private async Task PasteIntoDocumentAsync(string type)
        {
            if (ViewModel?.EditorViewModel is not { } editor) return;
            await editor.WaitForLoadAsync(3000);
            ViewModel?.MarkdownEditor?.FocusEditor();
            await Task.Delay(100);
            editor.PasteCommand.Execute(type);
        }

        private readonly System.Text.StringBuilder pending = new();

        private bool handingOn;

        private async void HandOn()
        {
            if (handingOn) return;
            handingOn = true;
            try
            {
                if (ViewModel?.EditorViewModel is { } editor && !await editor.WaitForLoadAsync(3000))
                    Log.Debug("home page: the editor did not confirm its document within 3 s; typing handed on anyway");
                ViewModel?.MarkdownEditor?.FocusEditor();
                // The web view takes the keyboard a moment after it is asked to.
                await Task.Delay(120);
                var text = pending.ToString();
                pending.Clear();
                if (text.Length > 0 && !TypeText(text)) Log.Debug($"home page: {text.Length} typed character(s) could not be handed on");
            }
            catch (Exception ex)
            {
                Log.Debug($"home page: typing not handed on: {ex.Message}");
            }
            finally
            {
                handingOn = false;
            }
            // Typed while this was waiting, with the page still holding the keyboard.
            if (pending.Length > 0) HandOn();
        }

        // Characters typed again as the keyboard types them (KEYEVENTF_UNICODE), into whatever has the keyboard now.
        private static bool TypeText(string text)
        {
            var inputs = new INPUT[text.Length * 2];
            for (var i = 0; i < text.Length; i++)
            {
                inputs[2 * i] = new INPUT { type = 1, u = new InputUnion { ki = new KEYBDINPUT { wScan = text[i], dwFlags = 0x0004 } } };
                inputs[2 * i + 1] = new INPUT { type = 1, u = new InputUnion { ki = new KEYBDINPUT { wScan = text[i], dwFlags = 0x0004 | 0x0002 } } };
            }
            return SendInput((uint)inputs.Length, inputs, System.Runtime.InteropServices.Marshal.SizeOf<INPUT>()) == inputs.Length;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct INPUT { public uint type; public InputUnion u; }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]
        private struct InputUnion
        {
            [System.Runtime.InteropServices.FieldOffset(0)] public MOUSEINPUT mi;
            [System.Runtime.InteropServices.FieldOffset(0)] public KEYBDINPUT ki;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint count, INPUT[] inputs, int size);

        private void OnListsChanged() => DispatcherQueue?.TryEnqueue(() =>
        {
            if (Visibility == Visibility.Visible) Refresh();
        });

        /// <summary>The lists made again from the recent files and folders and the favourites (only while shown).</summary>
        private async void Refresh()
        {
            if (refreshQueued) return;
            refreshQueued = true;
            try
            {
                await Task.Yield();
                var history = this.GetService<AccessHistory>();
                var favorites = Favorites.Shared;
                var favoritePaths = favorites.Items;
                FavoriteItems.Clear();
                foreach (var entry in favoritePaths) FavoriteItems.Add(new PathItem(entry.Path, entry.Folder) { IsFavorite = true });
                RecentFiles.Clear();
                foreach (var path in (history?.FileRecentlyOpened ?? Array.Empty<string>()).Take(FilesShown))
                    RecentFiles.Add(new PathItem(path, false) { IsFavorite = favorites.Contains(path) });
                RecentFolders.Clear();
                foreach (var path in (history?.FolderRecentlyOpened ?? Array.Empty<string>()).Take(FoldersShown))
                    RecentFolders.Add(new PathItem(path, true) { IsFavorite = favorites.Contains(path) });
                FavoritesSection.Visibility = FavoriteItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                NoRecentFilesText.Visibility = RecentFiles.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
                RecentFoldersSection.Visibility = RecentFolders.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                ShortcutHints.Text = Hints();
                refreshQueued = false;
                await FillDetailsAsync(FavoriteItems.Concat(RecentFiles).Concat(RecentFolders).ToList());
            }
            catch (Exception ex)
            {
                Log.Debug($"home page: {ex.Message}");
            }
            finally
            {
                refreshQueued = false;
            }
        }

        // Whether each one is there, and a file's last change: off the UI thread, given up on after a while (a share that
        // does not answer leaves its entry as it is).
        private static async Task FillDetailsAsync(List<PathItem> items)
        {
            var missing = await PathProbe.MissingAsync(items.Select(x => x.Path));
            foreach (var item in items) item.Missing = missing.Contains(item.Path);
            var files = items.Where(x => !x.Folder && !x.Missing).ToList();
            var times = Task.Run(() => files.Select(x =>
            {
                try { return (DateTime?)File.GetLastWriteTime(x.Path); }
                catch { return null; }
            }).ToList());
            if (await Task.WhenAny(times, Task.Delay(PathProbe.DefaultTimeout)) != times) return;
            var result = await times;
            for (var i = 0; i < files.Count; i++)
                if (result[i] is DateTime time && time.Year > 1601) files[i].Modified = Modified(time);
        }

        /// <summary>"14:05" today, "Mon 14:05" this week, the date before that.</summary>
        private static string Modified(DateTime time)
        {
            var now = DateTime.Now;
            if (time.Date == now.Date) return time.ToString("t");
            if ((now.Date - time.Date).TotalDays < 7) return time.ToString("ddd t");
            return time.ToString("d");
        }

        // The page's shortcuts, as they are set now.
        private string Hints()
        {
            var keys = this.GetService<IKeyboardAccelerator>();
            var settings = ViewModel?.SettingsViewModel;
            if (keys == null || settings == null) return string.Empty;
            var parts = new List<string>();
            void Add(ShortcutKey key, string name)
            {
                if (key == null) return;
                var text = keys.GetShortcutKeyText(key);
                if (!string.IsNullOrEmpty(text)) parts.Add($"{text} {name}");
            }
            Add(settings.ShortcutNewFile, Locale.GetString("HomePage.NewDocument"));
            Add(settings.ShortcutOpenFile, Locale.GetString("HomePage.OpenFile"));
            Add(settings.ShortcutHome, Locale.GetString("Home"));
            Add(settings.ShortcutSettings, Locale.GetString("Settings"));
            return string.Join("   ·   ", parts);
        }

        // A custom theme's colours, as the tab strip takes them (the page is native: the editor's CSS cannot reach it).
        private void ApplyTheme()
        {
            var theme = ThemeFiles.Find(ViewModel?.SettingsViewModel?.CustomTheme);
            ThemeLayer.Background = ThemeFiles.Brush(theme?.Background);
            var foreground = theme == null ? null : (ThemeFiles.Brush(theme.Foreground) ?? ThemeFiles.Readable(theme.Background));
            if (foreground != null) Foreground = foreground;
            else ClearValue(ForegroundProperty);
        }

        private bool editionSectionsAdded;

        // An edition's own sections (EditionHooks.AddHomeSections), once per page; there are none without an edition.
        private void AddEditionSections()
        {
            if (editionSectionsAdded || ViewModel == null) return;
            editionSectionsAdded = true;
            foreach (var section in EditionHooks.HomeSections(ViewModel))
            {
                var panel = new StackPanel();
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(panel, "HomeSection-" + section.Id);
                if (!string.IsNullOrEmpty(section.Title))
                    panel.Children.Add(new TextBlock { Text = section.Title, Style = (Style)Resources["SectionTitleStyle"] });
                panel.Children.Add(section.Element);
                EditionSections.Children.Add(panel);
            }
        }

        /// <summary>The ids of the edition's sections on the page (the automation test host).</summary>
        public IReadOnlyList<string> EditionSectionIds() =>
            EditionSections.Children.OfType<FrameworkElement>().Select(x => Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(x)).ToList();

        // ---- commands ----

        private void OnNewClick(object sender, RoutedEventArgs e) => Home?.NewDocumentCommand.Execute(default);

        private void OnOpenFileClick(object sender, RoutedEventArgs e) => ViewModel?.FileViewModel.OpenFileCommand.Execute(null);

        private void OnOpenFolderClick(object sender, RoutedEventArgs e) => ViewModel?.FileViewModel.OpenFolderCommand.Execute(null);

        private void OnSettingsClick(object sender, RoutedEventArgs e) => ViewModel?.NavigateCommand.Execute("Settings/General");

        // A file opens in this tab (it is blank, so it is taken over); a folder becomes the folder pane's root.
        private async void OnEntryClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is not PathItem item || ViewModel?.FileViewModel is not { } file) return;
            if (item.Folder) await file.OpenFolder(item.Path);
            else await file.OpenFile(item.Path);
        }

        private static PathItem ItemOf(object sender) => (sender as FrameworkElement)?.DataContext as PathItem;

        // The entry's menu, worded for it: into or out of the favourites.
        private void OnEntryFlyoutOpening(object sender, object e)
        {
            var flyout = (MenuFlyout)sender;
            var item = (flyout.Target as FrameworkElement)?.DataContext as PathItem;
            var favorite = flyout.Items.OfType<MenuFlyoutItem>().FirstOrDefault(x => "Favorite".Equals(x.Tag));
            if (item == null || favorite == null) return;
            var store = Favorites.Shared;
            var isFavorite = store.Contains(item.Path);
            favorite.Text = Locale.GetString(isFavorite ? "Favorites.Remove" : "Favorites.Add");
            favorite.IsEnabled = isFavorite || store.Items.Count < FavoritesStore.MaxCount;
        }

        private void OnToggleFavoriteClick(object sender, RoutedEventArgs e)
        {
            if (ItemOf(sender) is not { } item) return;
            var store = Favorites.Shared;
            if (!store.Remove(item.Path)) store.Add(item.Path, item.Folder);
        }

        private void OnRemoveFavoriteClick(object sender, RoutedEventArgs e)
        {
            if (ItemOf(sender) is { } item) Favorites.Shared.Remove(item.Path);
        }

        private void OnRemoveRecentClick(object sender, RoutedEventArgs e)
        {
            if (ItemOf(sender) is not { } item) return;
            var history = this.GetService<AccessHistory>();
            if (history == null) return;
            _ = item.Folder ? history.RemoveFolderHistory(item.Path) : history.RemoveFileHistory(item.Path);
        }

        private void OnLocationClick(object sender, RoutedEventArgs e)
        {
            if (ItemOf(sender) is { } item) Common.OpenFileLocation(item.Path);
        }

        /// <summary>What the page shows now (the automation test host): each list's paths and which are dimmed.</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<(string Path, bool Missing, string Modified)>> Lists() => new Dictionary<string, IReadOnlyList<(string, bool, string)>>
        {
            ["favorites"] = FavoriteItems.Select(x => (x.Path, x.Missing, x.Modified)).ToList(),
            ["files"] = RecentFiles.Select(x => (x.Path, x.Missing, x.Modified)).ToList(),
            ["folders"] = RecentFolders.Select(x => (x.Path, x.Missing, x.Modified)).ToList(),
        };
    }
}
