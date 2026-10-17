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
                menu.Items.RemoveAt(1);
            var made = new List<MenuFlyoutItem>();
            for (var i = paths.Count - 1; i >= 0; i--)
            {
                var path = paths[i];
                var item = new MenuFlyoutItem { Text = RecentPaths.Label(path), Command = open, CommandParameter = path, Tag = path };
                ToolTipService.SetToolTip(item, path + "\n" + Locale.GetString("Recent.RemoveHint"));
                AutomationProperties.SetAutomationId(item, idPrefix + i);
                AutomationProperties.SetHelpText(item, path);
                // A right-click (or Delete) takes the entry out of the list at once; the menu stays open.
                item.RightTapped += (s, e) => { e.Handled = true; RemoveEntry(menu, item, path, remove); };
                item.KeyDown += (s, e) =>
                {
                    if (e.Key != Windows.System.VirtualKey.Delete) return;
                    e.Handled = true;
                    RemoveEntry(menu, item, path, remove);
                };
                menu.Items.Insert(1, item);
                made.Insert(0, item);
            }
            return made;
        }

        private void RemoveEntry(MenuFlyoutSubItem menu, MenuFlyoutItem item, string path, Action<string> remove)
        {
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
        }
    }
}
