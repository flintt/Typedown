using System;
using System.IO;
using System.Threading.Tasks;
using Typedown.Core.Interfaces;
using Typedown.Core.Utilities;

namespace Typedown.Core.Services
{
    /// <summary>
    /// A file or folder a menu acts on - a recent entry, a tab's document, a favourite, a row of the folder tree - taken as
    /// the menu opens, not looked up again when its item is chosen. No path: an untitled document.
    /// </summary>
    public sealed record FileEntry(string Path, bool IsFolder)
    {
        public bool HasPath => !string.IsNullOrEmpty(Path);

        /// <summary>A path whose kind is not said: a folder if there is one there now.</summary>
        public static FileEntry Of(string path) => new(path, !string.IsNullOrEmpty(path) && Directory.Exists(path));
    }

    /// <summary>
    /// What the home page, the tab strip, the folder tree and File > Open Recent do with an entry: show its place, copy its
    /// path, make it a favourite or not, take it out of the recent lists. Each did it its own way - the place shown for a
    /// path that is gone or for none, a copy that failed said or not, a tab's favourite always a file, a removal nobody
    /// heard fail (the modularity audit's M05, 2026-11-12). The same file had different items in each of those menus (the
    /// user, 2026-11-13; the menu report's M09): each has the same core now, in this order - Open, Open in New Window (a
    /// file) | the favourite, Open File Location, Copy as Path | what is its own (out of the list, a tab's closing, the
    /// tree's file operations). Which menus close after stays each menu's.
    /// </summary>
    public static class FileEntryActions
    {
        /// <summary>A file into this window's editor; a folder the folder pane's root - as a click on the entry does.</summary>
        public static async Task Open(FileEntry entry, ViewModels.FileViewModel file)
        {
            if (!CanShowLocation(entry) || file == null) return;
            if (entry.IsFolder) await file.OpenFolder(entry.Path);
            else await file.OpenFile(entry.Path);
        }

        /// <summary>A file only: a folder has no window of its own.</summary>
        public static bool CanOpenInNewWindow(FileEntry entry) => CanShowLocation(entry) && !entry.IsFolder;

        public static void OpenInNewWindow(FileEntry entry, ViewModels.FileViewModel file)
        {
            if (CanOpenInNewWindow(entry)) file?.NewWindowCommand.Execute(entry.Path);
        }

        public static bool CanShowLocation(FileEntry entry) => entry?.HasPath == true;

        /// <summary>
        /// Explorer with the entry selected; for one that is gone, the folder it was in, if that is there. Null when done
        /// (or there is nowhere to go, logged); else why not, to be said (<see cref="Report"/>).
        /// </summary>
        public static string ShowLocation(FileEntry entry)
        {
            if (!CanShowLocation(entry)) return null;
            try
            {
                if (File.Exists(entry.Path) || Directory.Exists(entry.Path))
                {
                    Common.OpenFileLocation(entry.Path);
                    return null;
                }
                var parent = Path.GetDirectoryName(entry.Path.TrimEnd('\\', '/'));
                if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                {
                    Log.Debug("file entry: not there any more; its folder shown");
                    Common.OpenFileLocation(parent);
                    return null;
                }
                Log.Debug("file entry: neither it nor its folder is there; nothing shown");
                return null;
            }
            catch (Exception ex)
            {
                Log.Debug($"file entry: showing its place failed: {ex.GetType().Name} {ex.Message}");
                return ex.Message;
            }
        }

        /// <summary>The path as it is (no quotes) on the clipboard. Null when done; else why not (the clipboard held by
        /// another program), to be said.</summary>
        public static string CopyPath(FileEntry entry, IClipboard clipboard)
        {
            if (!CanShowLocation(entry) || clipboard == null) return null;
            try
            {
                clipboard.SetText(entry.Path);
                return null;
            }
            catch (Exception ex)
            {
                Log.Debug($"file entry: copying its path failed: {ex.GetType().Name} {ex.Message}");
                return ex.Message;
            }
        }

        /// <summary>
        /// A failure said the same way wherever it came from: the folder tree's dialog (the tab strip and Open Recent only
        /// logged it, and nothing was seen to happen).
        /// </summary>
        public static async void Report(Microsoft.UI.Xaml.XamlRoot root, string error)
        {
            if (string.IsNullOrEmpty(error) || root == null) return;
            try { await Typedown.Core.Controls.AppContentDialog.Create(Locale.GetString("Error"), error, Locale.GetString("Ok")).ShowAsync(root); }
            catch (Exception ex) { Log.Debug($"file entry: the failure not shown: {ex.Message}"); }
        }

        /// <summary>Whether it is a favourite, and whether the item may change that (a new one only with room left).</summary>
        public static (bool IsFavorite, bool CanToggle) FavoriteState(FileEntry entry)
        {
            if (!CanShowLocation(entry)) return (false, false);
            var store = Favorites.Shared;
            var favorite = store.Contains(entry.Path);
            return (favorite, favorite || store.Items.Count < FavoritesStore.MaxCount);
        }

        /// <summary>A favourite no more, or one now (as the folder or file it is). False when nothing changed.</summary>
        public static bool ToggleFavorite(FileEntry entry)
        {
            if (!CanShowLocation(entry)) return false;
            var store = Favorites.Shared;
            var changed = store.Contains(entry.Path) ? store.Remove(entry.Path) : store.Add(entry.Path, entry.IsFolder);
            if (!changed) Log.Debug($"file entry: favourite not changed ({store.Items.Count} of {FavoritesStore.MaxCount})");
            return changed;
        }

        /// <summary>Out of the recent files or folders; a failure is logged, not lost.</summary>
        public static async Task RemoveFromRecent(FileEntry entry, AccessHistory history)
        {
            if (!CanShowLocation(entry) || history == null) return;
            try
            {
                if (entry.IsFolder) await history.RemoveFolderHistory(entry.Path);
                else await history.RemoveFileHistory(entry.Path);
            }
            catch (Exception ex)
            {
                Log.Debug($"file entry: taking it out of the recent list failed: {ex.GetType().Name} {ex.Message}");
            }
        }
    }
}
