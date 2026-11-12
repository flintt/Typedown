using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Typedown.Core.Interfaces;
using Typedown.Core.Models;
using Typedown.Core.Services;
using Typedown.Core.Utilities;
using Typedown.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace Typedown.Core.Controls.EditorControls.MenuBarItems
{
    public sealed partial class FileItem : MenuBarItemBase
    {
        public FileViewModel File => ViewModel?.FileViewModel;

        public AccessHistory FileHistory => this.GetService<AccessHistory>();

        public IFileExport FileExport => this.GetService<IFileExport>();

        public FileItem()
        {
            InitializeComponent();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _ = UpdateOpenRecentItem();
            UpdateExportItem();
        }

        protected override void OnRegisterShortcut()
        {
            RegisterWindowShortcut(Settings.ShortcutNewFile, NewFileItem);
            RegisterWindowShortcut(Settings.ShortcutNewWindow, NewWindowItem);
            RegisterWindowShortcut(Settings.ShortcutHome, HomeItem);
            RegisterWindowShortcut(Settings.ShortcutOpenFile, OpenFileItem);
            RegisterWindowShortcut(Settings.ShortcutOpenFolder, OpenFolderItem);
            RegisterWindowShortcut(Settings.ShortcutClearRecentFiles, ClearRecentFilesItem);
            RegisterWindowShortcut(Settings.ShortcutSave, SaveItem);
            RegisterWindowShortcut(Settings.ShortcutSaveAs, SaveAsItem);
            RegisterWindowShortcut(Settings.ShortcutExportSettings, ExportSettingsItem);
            RegisterWindowShortcut(Settings.ShortcutPrint, PrintItem);
            RegisterWindowShortcut(Settings.ShortcutSettings, SettingItem, once: true);
            RegisterWindowShortcut(Settings.ShortcutClose, CloseItem);
            AddEditionItems();
        }

        private bool editionItemsAdded;

        // Marks an edition's entries in the Export submenu, which the export configurations' rebuild leaves in place.
        private static readonly object EditionEntry = new();

        // An edition's own entries (EditionHooks.AddFileMenuItems, AddExportMenuItems): below Save As, and in Export
        // after the export configurations; once per menu, as the View menu's.
        private void AddEditionItems()
        {
            if (editionItemsAdded || ViewModel == null) return;
            editionItemsAdded = true;
            var index = Items.IndexOf(SaveAsItem);
            if (index >= 0)
                foreach (var item in EditionHooks.FileMenuItems(ViewModel))
                    Items.Insert(++index, item);
            var before = ExportSubMenu.Items.IndexOf(ExportSubMenu.Items.OfType<MenuFlyoutSeparator>().FirstOrDefault());
            if (before >= 0)
                foreach (var item in EditionHooks.ExportMenuItems(ViewModel))
                {
                    item.Tag = EditionEntry;
                    ExportSubMenu.Items.Insert(before++, item);
                }
        }

        /// <summary>The Export submenu's entries as shown (the automation test host reads them).</summary>
        public System.Collections.Generic.IReadOnlyList<string> ExportMenu() =>
            ExportSubMenu?.Items.OfType<MenuFlyoutItem>().Where(x => x.Visibility == Visibility.Visible).Select(x => x.Text).ToList() ?? new System.Collections.Generic.List<string>();

        /// <summary>How many recent files and folders the menu lists.</summary>
        private const int RecentInMenu = 10;

        /// <summary>
        /// The Open recent submenu built again from the lists: the files, then the folders in their submenu. Each entry
        /// is the name and the folder it is in, the full path in its tooltip; one that is not there any more is greyed
        /// out once that is known (looked at off the UI thread) and stays until it is removed. Done when the greying is.
        /// </summary>
        public Task UpdateOpenRecentItem(bool files = true, bool folders = true)
        {
            if (File == null || FileHistory == null) return Task.CompletedTask;
            var made = new List<MenuFlyoutItem>();
            if (files)
            {
                var list = FileHistory.FileRecentlyOpened.Take(RecentInMenu).ToList();
                made.AddRange(Fill(OpenRecentSubMenu, list, "RecentFile-", File.OpenFileCommand, path => _ = FileHistory.RemoveFileHistory(path)));
                NoRecentFilesItem.Visibility = list.Any() ? Visibility.Collapsed : Visibility.Visible;
                ClearRecentFilesItem.IsEnabled = list.Any();
            }
            if (folders)
            {
                var list = FileHistory.FolderRecentlyOpened.Take(RecentInMenu).ToList();
                made.AddRange(Fill(OpenRecentFoldersSubMenu, list, "RecentFolder-", File.OpenFolderCommand, path => _ = FileHistory.RemoveFolderHistory(path)));
                NoRecentFoldersItem.Visibility = list.Any() ? Visibility.Collapsed : Visibility.Visible;
                ClearRecentFoldersItem.IsEnabled = list.Any();
            }
            return GreyMissingAsync(made);
        }

        // The entries between the submenu's first item ("No recent ...") and its first separator, made anew.
        private List<MenuFlyoutItem> Fill(MenuFlyoutSubItem menu, List<string> paths, string idPrefix, System.Windows.Input.ICommand open, Action<string> remove)
        {
            while (menu.Items.Count > 1 && menu.Items[1] is not MenuFlyoutSeparator)
            {
                if (menu.Items[1] is MenuFlyoutItem old) recentEntries.Remove(old);
                menu.Items.RemoveAt(1);
            }
            WatchRecentKeys();
            var made = new List<MenuFlyoutItem>();
            for (var i = paths.Count - 1; i >= 0; i--)
            {
                var path = paths[i];
                var item = new MenuFlyoutItem { Text = RecentPaths.Label(path), Command = open, CommandParameter = path, Tag = path };
                ToolTipService.SetToolTip(item, path + "\n" + Locale.GetString("Recent.MenuHint"));
                AutomationProperties.SetAutomationId(item, idPrefix + i);
                AutomationProperties.SetHelpText(item, path);
                // A right-click opens the entry's menu, as the home page's recent list does: a right-click took the entry
                // out at once, where everywhere else it opens a menu (the 2026-11-11 menu report's M08). Delete still does.
                item.ContextRequested += (s, e) =>
                {
                    e.Handled = true;
                    ShowEntryMenu(item, e.TryGetPosition(item, out var at) ? at : new Windows.Foundation.Point(item.ActualWidth / 2, item.ActualHeight / 2),
                        EntryMenu(menu, item, path, open, remove), () => RemoveEntry(menu, item, path, remove));
                };
                // The entry drawn highlighted - the mouse over it, or the keys on it - is the one Delete takes out (see
                // OnRecentKey): it took out the first entry, where the keyboard had stayed (the user, 2026-11-12).
                Action removeThis = () => RemoveEntry(menu, item, path, remove);
                recentEntries[item] = removeThis;
                // The entry the keys went to (the mouse's is found where the pointer is, at the key: see OnRecentDelete).
                // Only the keys' own moves: the focus the list gives its first entry as it opens came after the mouse had
                // gone onto another, and Delete took out the first (XM02, job 416).
                item.GotFocus += (s, e) => { if (item.FocusState == FocusState.Keyboard) highlightedEntry = (item, removeThis); };
                // Gone as soon as it is not drawn highlighted: a Delete typed in the document must reach the document.
                item.LostFocus += (s, e) => { if (highlightedEntry.Item == item) highlightedEntry = default; };
                item.Unloaded += (s, e) => { if (highlightedEntry.Item == item) highlightedEntry = default; };
                menu.Items.Insert(1, item);
                made.Insert(0, item);
            }
            return made;
        }

        // A recent entry's own menu: open it, where it is, its path, and out of the list.
        private List<MenuFlyoutItemBase> EntryMenu(MenuFlyoutSubItem menu, MenuFlyoutItem item, string path, System.Windows.Input.ICommand open, Action<string> remove)
        {
            var openItem = new MenuFlyoutItem { Text = Locale.GetString("Open") };
            openItem.Click += (s, e) => open.Execute(path);
            var location = new MenuFlyoutItem { Text = Locale.GetString("OpenFileLocation") };
            location.Click += (s, e) => Common.OpenFileLocation(path);
            var copy = new MenuFlyoutItem { Text = Locale.GetString("CopyAsPath") };
            copy.Click += (s, e) =>
            {
                try { this.GetService<Interfaces.IClipboard>()?.SetText(path); }
                catch (Exception ex) { Log.Debug($"recent: copy path: {ex.Message}"); }
            };
            var removeItem = new MenuFlyoutItem { Text = Locale.GetString("HomePage.RemoveFromList") };
            AutomationProperties.SetAutomationId(removeItem, "RecentRemoveItem");
            removeItem.Click += (s, e) => RemoveEntry(menu, item, path, remove);
            return new List<MenuFlyoutItemBase> { openItem, location, copy, new MenuFlyoutSeparator(), removeItem };
        }

        // The entry's menu over the menus, which stay open until one of its items is chosen (the user, 2026-11-11: the
        // menus went as it opened). A flyout opened from a submenu is not taken for that menu's child, and closes every
        // menu open: a popup of its own instead, with the menu's look. Remove from list closes it alone, the list left
        // open for the next; another choice closes the menus too, as the menu bar closes them. Esc, or a click anywhere
        // else, closes it alone. While it is open, Delete takes out the entry it is for: the keyboard stays in the list,
        // on the entry the mouse last went over, and that one went (the user, 2026-11-12).
        private Popup entryPopup;
        private Action removeEntryShown;
        // The entry drawn highlighted in the list, and how it goes (set by its pointer and focus).
        private (MenuFlyoutItem Item, Action Remove) highlightedEntry;
        private readonly Dictionary<MenuFlyoutItem, Action> recentEntries = new();
        private bool entryMenuOpen;

        // The recent entry under the pointer now, drawn highlighted by it: found by where it is on the screen, not kept from
        // the pointer's events (moved by the test's mouse, the entries raised none, and Delete took out nothing - job 417).
        // The keyboard hook calls on the window's thread, as it was installed there.
        private Action EntryUnderPointer()
        {
            var window = ViewModel?.MainWindow ?? IntPtr.Zero;
            if (window == IntPtr.Zero || XamlRoot == null) return null;
            // Only with the menus open: a Delete in the document is the document's whatever the pointer is over.
            if (VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot).Count == 0) return null;
            var cursor = PInvoke.GetCursorPos();
            var client = PInvoke.GetClientRectOnScreen(window);
            var scale = XamlRoot.RasterizationScale;
            foreach (var (item, remove) in recentEntries)
            {
                if (!item.IsLoaded || item.XamlRoot == null) continue;
                var b = item.TransformToVisual(null).TransformBounds(new Windows.Foundation.Rect(0, 0, item.ActualWidth, item.ActualHeight));
                double left = client.left + b.X * scale, top = client.top + b.Y * scale;
                if (cursor.X >= left && cursor.X < left + b.Width * scale && cursor.Y >= top && cursor.Y < top + b.Height * scale)
                    return remove;
            }
            return null;
        }
        private IDisposable recentKeys;

        // Delete and Esc for the list and the entry's menu, from the window's keyboard hook: the entry's menu is a window of
        // its own, which the keyboard does not always reach (XM02: Delete went nowhere, and an Esc left it open over the
        // next click on File). Delete takes out the entry whose menu is open, else the entry highlighted; Esc closes the
        // entry's menu alone. Other keys, and these with no list open, are left alone.
        private void WatchRecentKeys()
        {
            if (recentKeys != null) return;
            var keys = this.GetService<IKeyboardAccelerator>();
            if (keys == null) return;
            recentKeys = keys.RegisterGlobal((s, e) =>
            {
                if (e.Modifiers != Windows.System.VirtualKeyModifiers.None) return;
                if (e.Key != Windows.System.VirtualKey.Delete && e.Key != Windows.System.VirtualKey.Escape) return;
                // Plain fields, kept on the window's thread: the hook may call from another.
                var popup = entryPopup;
                var open = popup != null && entryMenuOpen;
                if (e.Key == Windows.System.VirtualKey.Escape)
                {
                    if (!open) return;
                    e.Handled = true;
                    DispatcherQueue.TryEnqueueSafe(() => popup.IsOpen = false);
                    return;
                }
                var remove = open ? removeEntryShown : EntryUnderPointer() ?? highlightedEntry.Remove;
                if (remove == null) return;
                e.Handled = true;
                DispatcherQueue.TryEnqueueSafe(() =>
                {
                    if (open) popup.IsOpen = false;
                    remove();
                });
            });
        }

        private void ShowEntryMenu(MenuFlyoutItem item, Windows.Foundation.Point at, List<MenuFlyoutItemBase> items, Action removeThis)
        {
            var root = item.XamlRoot;
            if (root == null) return;
            if (entryPopup != null) entryPopup.IsOpen = false;
            var presenter = new MenuFlyoutPresenter();
            UiLanguage.Set(presenter);
            // A menu's presenter draws no background of its own: a flyout puts the system's acrylic behind it. A popup does
            // not, and this menu came up see-through (the user, 2026-11-12): the menus' acrylic in the app's theme
            // (AppBackdrop - the system's DesktopAcrylicBackdrop took Windows' theme, not the app's).
            presenter.RequestedTheme = (root.Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Default;
            var popup = new Popup { XamlRoot = root, Child = presenter, IsLightDismissEnabled = true, ShouldConstrainToRootBounds = false, SystemBackdrop = AppBackdrop.MenuFor(root) };
            foreach (var x in items)
            {
                presenter.Items.Add(x);
                if (x is MenuFlyoutItem choice) choice.Click += (s, e) =>
                {
                    popup.IsOpen = false;
                    if (AutomationProperties.GetAutomationId(choice) != "RecentRemoveItem") CloseMenus();
                };
            }
            presenter.KeyDown += (s, e) =>
            {
                if (e.Key != Windows.System.VirtualKey.Escape) return;
                e.Handled = true;
                popup.IsOpen = false;
                item.Focus(FocusState.Keyboard);
            };
            popup.Closed += (s, e) =>
            {
                if (entryPopup != popup) return;
                entryMenuOpen = false;
                entryPopup = null;
                removeEntryShown = null;
            };
            WatchRecentKeys();
            entryMenuOpen = true;
            entryPopup = popup;
            removeEntryShown = removeThis;
            var point = item.TransformToVisual(null).TransformPoint(at);
            popup.HorizontalOffset = point.X;
            popup.VerticalOffset = point.Y;
            popup.Opened += (s, e) => (items[0] as Control)?.Focus(FocusState.Programmatic);
            popup.IsOpen = true;
            Log.Debug($"recent: the entry's menu at {point.X:0},{point.Y:0}");
        }

        // The menus closed as the menu bar closes them (its item collapsed): their popups shut from outside left the menu
        // bar taking its menu for open.
        private void CloseMenus()
        {
            var peer = Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.FromElement(this)
                ?? Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(this);
            if (peer?.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.ExpandCollapse) is Microsoft.UI.Xaml.Automation.Provider.IExpandCollapseProvider menu)
            {
                menu.Collapse();
                return;
            }
            Log.Debug("recent: the menu bar item cannot be collapsed; its popups closed");
            if (XamlRoot != null) foreach (var open in VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot)) open.IsOpen = false;
        }

        private void RemoveEntry(MenuFlyoutSubItem menu, MenuFlyoutItem item, string path, Action<string> remove)
        {
            Log.Debug($"recent: {System.IO.Path.GetFileName(path)} taken out of the list");
            remove(path);
            var index = menu.Items.IndexOf(item);
            if (index >= 0) menu.Items.RemoveAt(index);
            var left = menu.Items.OfType<MenuFlyoutItem>().Any(x => x.Tag is string);
            if (menu == OpenRecentSubMenu)
            {
                NoRecentFilesItem.Visibility = left ? Visibility.Collapsed : Visibility.Visible;
                ClearRecentFilesItem.IsEnabled = left;
            }
            else
            {
                NoRecentFoldersItem.Visibility = left ? Visibility.Collapsed : Visibility.Visible;
                ClearRecentFoldersItem.IsEnabled = left;
            }
        }

        private static async Task GreyMissingAsync(List<MenuFlyoutItem> items)
        {
            try
            {
                var missing = await PathProbe.MissingAsync(items.Select(x => x.Tag as string));
                foreach (var item in items)
                    item.Opacity = missing.Contains(item.Tag as string) ? MissingOpacity : 1;
            }
            catch (Exception ex)
            {
                Log.Debug($"recent menu: {ex.Message}");
            }
        }

        /// <summary>An entry whose file or folder is not there (a dim entry, still there to be removed or tried again).</summary>
        public const double MissingOpacity = 0.45;

        /// <summary>
        /// The Open recent menu as built, for the automation test host: each entry's kind (file, folder), text, path and
        /// whether it is shown greyed; rebuilt first, and read once the greying is done.
        /// </summary>
        public async Task<IReadOnlyList<(string Kind, string Text, string Path, bool Missing)>> RecentMenuAsync()
        {
            await UpdateOpenRecentItem();
            IEnumerable<(string, string, string, bool)> Entries(MenuFlyoutSubItem menu, string kind) =>
                menu.Items.OfType<MenuFlyoutItem>().Where(x => x.Tag is string).Select(x => (kind, x.Text, (string)x.Tag, x.Opacity < 1));
            return Entries(OpenRecentSubMenu, "file").Concat(Entries(OpenRecentFoldersSubMenu, "folder")).ToList();
        }

        private void OnOpenRecentSubMenuLoaded(object sender, RoutedEventArgs e)
        {
            // The folders' submenu opening rebuilds only its own entries: the open menu around it is left as it is.
            _ = sender == OpenRecentFoldersSubMenu ? UpdateOpenRecentItem(files: false) : UpdateOpenRecentItem();
        }

        private void UpdateExportItem()
        {
            var configs = FileExport.ExportConfigs.ToList();
            while (ExportSubMenu.Items[1] is not MenuFlyoutSeparator && ExportSubMenu.Items[1].Tag != EditionEntry)
                ExportSubMenu.Items.RemoveAt(1);
            foreach (var config in configs.Reverse<ExportConfig>())
                ExportSubMenu.Items.Insert(1, new MenuFlyoutItem() { Text = config.Name, Command = File.ExportCommand, CommandParameter = config });
            NoExportConfigItem.Visibility = configs.Any() ? Visibility.Collapsed : Visibility.Visible;
        }

        private void OnExportSubMenuLoaded(object sender, RoutedEventArgs e)
        {
            UpdateExportItem();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            Bindings?.StopTracking();
            recentKeys?.Dispose();
            recentKeys = null;
        }
    }
}
