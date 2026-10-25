using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Typedown.Core.Models;
using Typedown.Core.Utilities;

namespace Typedown.Core.Services
{
    /// <summary>
    /// The files and folders opened recently (File > Open recent, the home page, "open the last folder" at start-up), kept
    /// in the database. One instance for all windows: the lists are snapshots replaced whole, so a window on another
    /// thread reads them without a lock, and <see cref="Changed"/> tells each window to read them again.
    /// </summary>
    public class AccessHistory
    {
        /// <summary>How many of each the lists hold (the menu and the home page show fewer).</summary>
        public const int ListCount = 15;

        /// <summary>How many rows of each kind the database keeps; older ones go when a new one is recorded.</summary>
        public const int KeepRows = 100;

        private volatile IReadOnlyList<string> files = Array.Empty<string>();

        private volatile IReadOnlyList<string> folders = Array.Empty<string>();

        /// <summary>The files opened recently, the latest first.</summary>
        public IReadOnlyList<string> FileRecentlyOpened => files;

        /// <summary>The folders opened recently (the folder pane's root), the latest first.</summary>
        public IReadOnlyList<string> FolderRecentlyOpened => folders;

        /// <summary>A list changed. Raised on whichever thread made the change: a window hands it to its own.</summary>
        public event Action Changed;

        private readonly TaskCompletionSource<bool> initializedTask = new();

        // Serialise record writes: two concurrent DbContexts could each find no row and both insert, duplicating a
        // path. This closes the in-process race (a cross-process unique index/migration is still TODO).
        private static readonly System.Threading.SemaphoreSlim recordLock = new(1, 1);

        public AccessHistory()
        {
            _ = UpdateRecentlyOpened();
        }

        /// <summary>
        /// Settings > General > Data > Remember recently opened files: off, nothing new is recorded (what is there stays
        /// until it is cleared). Read from the settings file each time, as every window shares it.
        /// </summary>
        public static bool RecordingEnabled
        {
            get
            {
                try { return JsonSettingsStore.Shared(Path.Combine(Config.GetLocalFolderPath(), "Settings.json")).Get(RecordSettingName, true); }
                catch { return true; }
            }
        }

        /// <summary>The setting behind <see cref="RecordingEnabled"/> (SettingsViewModel.RecordRecentFiles).</summary>
        public const string RecordSettingName = "RecordRecentFiles";

        public async Task RecordFileHistory(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !RecordingEnabled) return;
            await recordLock.WaitAsync();
            try
            {
                using var ctx = await AppDbContext.Create();
                var model = ctx.FileAccessHistories;
                // One row per path, its time updated — not a new row every save. Otherwise the table grows without
                // bound and the most-recent query returns many rows of the same file, crowding others out.
                var existing = await model.FirstOrDefaultAsync(x => x.FilePath == filePath);
                if (existing != null)
                    existing.AccessTime = DateTime.Now;
                else
                    await model.AddAsync(new FileAccessHistory() { FilePath = filePath, AccessTime = DateTime.Now });
                await ctx.SaveChangesAsync();
                // Only the latest rows are kept: the lists show a few, and an old path says where someone was.
                var excess = await model.OrderByDescending(x => x.AccessTime).Skip(KeepRows).ToListAsync();
                if (excess.Count > 0)
                {
                    model.RemoveRange(excess);
                    await ctx.SaveChangesAsync();
                }
            }
            finally
            {
                recordLock.Release();
            }
            await ReloadFiles();
        }

        public async Task RemoveFileHistory(string filePath)
        {
            using (var ctx = await AppDbContext.Create())
            {
                var model = ctx.FileAccessHistories;
                model.RemoveRange(model.Where(x => x.FilePath == filePath));
                await ctx.SaveChangesAsync();
            }
            await ReloadFiles();
        }

        public async Task ClearFileHistory()
        {
            using (var ctx = await AppDbContext.Create())
            {
                var model = ctx.FileAccessHistories;
                model.RemoveRange(model);
                await ctx.SaveChangesAsync();
            }
            await ReloadFiles();
        }

        // The list read again from the database, which is what every change writes to first.
        private async Task ReloadFiles()
        {
            using var ctx = await AppDbContext.Create();
            files = await ctx.FileAccessHistories.OrderByDescending(x => x.AccessTime).Take(ListCount).Select(x => x.FilePath).ToListAsync();
            RaiseChanged();
        }

        public async Task RecordFolderHistory(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) || !RecordingEnabled) return;
            await recordLock.WaitAsync();
            try
            {
                using var ctx = await AppDbContext.Create();
                var model = ctx.FolderAccessHistories;
                // One row per path, its time updated — see RecordFileHistory.
                var existing = await model.FirstOrDefaultAsync(x => x.FolderPath == folderPath);
                if (existing != null)
                    existing.AccessTime = DateTime.Now;
                else
                    await model.AddAsync(new FolderAccessHistory() { FolderPath = folderPath, AccessTime = DateTime.Now });
                await ctx.SaveChangesAsync();
                var excess = await model.OrderByDescending(x => x.AccessTime).Skip(KeepRows).ToListAsync();
                if (excess.Count > 0)
                {
                    model.RemoveRange(excess);
                    await ctx.SaveChangesAsync();
                }
            }
            finally
            {
                recordLock.Release();
            }
            await ReloadFolders();
        }

        public async Task RemoveFolderHistory(string folderPath)
        {
            using (var ctx = await AppDbContext.Create())
            {
                var model = ctx.FolderAccessHistories;
                model.RemoveRange(model.Where(x => x.FolderPath == folderPath));
                await ctx.SaveChangesAsync();
            }
            await ReloadFolders();
        }

        public async Task ClearFolderHistory()
        {
            using (var ctx = await AppDbContext.Create())
            {
                var model = ctx.FolderAccessHistories;
                model.RemoveRange(model);
                await ctx.SaveChangesAsync();
            }
            await ReloadFolders();
        }

        private async Task ReloadFolders()
        {
            using var ctx = await AppDbContext.Create();
            folders = await ctx.FolderAccessHistories.OrderByDescending(x => x.AccessTime).Take(ListCount).Select(x => x.FolderPath).ToListAsync();
            RaiseChanged();
        }

        private void RaiseChanged()
        {
            try
            {
                Changed?.Invoke();
            }
            catch (Exception ex)
            {
                Log.Debug($"AccessHistory.Changed: {ex.Message}");
            }
        }

        private async Task UpdateRecentlyOpened()
        {
            try
            {
                await Task.WhenAll(ReloadFiles(), ReloadFolders());
            }
            catch (Exception ex)
            {
                // A failed history load (a locked or corrupt database, a migration error) must not leave
                // EnsureInitialized awaiting forever — start-up waits on it. Report it and carry on empty.
                Log.Debug($"AccessHistory init failed: {ex.Message}");
            }
            finally
            {
                initializedTask.TrySetResult(true);
            }
        }

        public async Task EnsureInitialized()
        {
            await initializedTask.Task;
        }

        /// <summary>Both lists emptied (Settings > General > Data > Clear recent).</summary>
        public async Task ClearHistory()
        {
            await ClearFileHistory();
            await ClearFolderHistory();
        }
    }
}
