using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Typedown.Core.Utilities;

namespace Typedown.Core.Services
{
    /// <summary>A favourite file or folder.</summary>
    public sealed class FavoriteEntry
    {
        [JsonProperty("path")]
        public string Path { get; set; }

        [JsonProperty("folder")]
        public bool Folder { get; set; }

        [JsonProperty("added")]
        public DateTime Added { get; set; }
    }

    /// <summary>
    /// The favourite files and folders, in favorites.json beside session.json - not in the settings (Restore defaults
    /// must not take them) and not in the database. At most <see cref="MaxCount"/>. An entry whose file or folder is not
    /// there stays (greyed out where it is shown): a network drive or a USB stick may only be unplugged. One store for all
    /// windows; <see cref="Changed"/> is raised on the thread that made the change, and each window hands it to its own.
    /// </summary>
    public sealed class FavoritesStore
    {
        public const int MaxCount = 50;

        private readonly string file;

        private readonly object gate = new();

        private List<FavoriteEntry> items;

        private Task writing = Task.CompletedTask;

        public FavoritesStore(string file) => this.file = file;

        /// <summary>A favourite was added, removed or renamed (or the file was read for the first time).</summary>
        public event Action Changed;

        /// <summary>The favourites in the order they were added. Empty until read (<see cref="LoadAsync"/>).</summary>
        public IReadOnlyList<FavoriteEntry> Items
        {
            get { lock (gate) return items?.ToList() ?? new List<FavoriteEntry>(); }
        }

        public bool IsLoaded { get { lock (gate) return items != null; } }

        /// <summary>The file read off the UI thread, once; a start-up does not wait for it.</summary>
        public Task LoadAsync() => IsLoaded ? Task.CompletedTask : Task.Run(() => { if (EnsureLoaded()) RaiseChanged(); });

        // True when this call read the file.
        private bool EnsureLoaded()
        {
            lock (gate)
            {
                if (items != null) return false;
                items = Read();
                return true;
            }
        }

        private List<FavoriteEntry> Read()
        {
            try
            {
                if (!File.Exists(file)) return new List<FavoriteEntry>();
                var stored = JsonConvert.DeserializeObject<Stored>(File.ReadAllText(file));
                return (stored?.Items ?? new List<FavoriteEntry>()).Where(x => !string.IsNullOrEmpty(x?.Path))
                    .GroupBy(x => RecentPaths.Normalize(x.Path), StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                    .Take(MaxCount).ToList();
            }
            catch (Exception ex)
            {
                // A file that cannot be read is kept aside, not overwritten by the next change.
                Log.Debug($"favorites: {file} could not be read ({ex.Message}); kept as .bad");
                try { File.Copy(file, file + ".bad", true); } catch { }
                return new List<FavoriteEntry>();
            }
        }

        private sealed class Stored
        {
            [JsonProperty("version")]
            public int Version { get; set; } = 1;

            [JsonProperty("items")]
            public List<FavoriteEntry> Items { get; set; } = new();
        }

        public bool Contains(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            EnsureLoaded();
            lock (gate) return items.Any(x => RecentPaths.Same(x.Path, path));
        }

        /// <summary>Added at the end; false when it is one already or there are <see cref="MaxCount"/>.</summary>
        public bool Add(string path, bool folder)
        {
            if (string.IsNullOrEmpty(path)) return false;
            EnsureLoaded();
            lock (gate)
            {
                if (items.Count >= MaxCount || items.Any(x => RecentPaths.Same(x.Path, path))) return false;
                items.Add(new FavoriteEntry { Path = path, Folder = folder, Added = DateTime.Now });
                Save();
            }
            RaiseChanged();
            return true;
        }

        public bool Remove(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            EnsureLoaded();
            lock (gate)
            {
                if (items.RemoveAll(x => RecentPaths.Same(x.Path, path)) == 0) return false;
                Save();
            }
            RaiseChanged();
            return true;
        }

        /// <summary>
        /// A file or folder renamed or moved in the app: the favourite that is it, and every one inside a renamed folder,
        /// follows. The number of favourites changed.
        /// </summary>
        public int Rename(string oldPath, string newPath)
        {
            if (string.IsNullOrEmpty(oldPath) || string.IsNullOrEmpty(newPath)) return 0;
            EnsureLoaded();
            var changed = 0;
            lock (gate)
            {
                var from = RecentPaths.Normalize(oldPath);
                var to = RecentPaths.Normalize(newPath);
                foreach (var item in items)
                {
                    var path = RecentPaths.Normalize(item.Path);
                    if (string.Equals(path, from, StringComparison.OrdinalIgnoreCase))
                        item.Path = to;
                    else if (RecentPaths.IsInside(path, from))
                        item.Path = to + path.Substring(from.TrimEnd('\\', '/').Length);
                    else
                        continue;
                    changed++;
                }
                if (changed > 0) Save();
            }
            if (changed > 0) RaiseChanged();
            return changed;
        }

        /// <summary>Done when every change so far is on disk.</summary>
        public Task FlushAsync()
        {
            lock (gate) return writing;
        }

        // Under the lock: the text taken now, written after the writes before it, atomically.
        private void Save()
        {
            var text = JsonConvert.SerializeObject(new Stored { Items = items.Select(x => new FavoriteEntry { Path = x.Path, Folder = x.Folder, Added = x.Added }).ToList() }, Formatting.Indented);
            writing = writing.ContinueWith(async _ =>
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(file));
                    await SafeFile.WriteAllTextAtomicAsync(file, text);
                }
                catch (Exception ex)
                {
                    Log.Debug($"favorites: could not write {file}: {ex.Message}");
                }
            }, TaskScheduler.Default).Unwrap();
        }

        private void RaiseChanged()
        {
            try { Changed?.Invoke(); }
            catch (Exception ex) { Log.Debug($"favorites: Changed: {ex.Message}"); }
        }
    }
}
