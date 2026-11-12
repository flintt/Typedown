using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Typedown.Core.Utilities;

namespace Typedown.Core.Services
{
    /// <summary>
    /// The documents the reader asked to see the first-edit notice for no more (the card's "Don't show again for this
    /// document"), by file path, in first-edit-mutes.json beside session.json: the app's own data, never written into the
    /// document. A document renamed or moved (here, in the folder pane, or outside the app while open) is followed, and
    /// one saved under another name is muted under both - as version history and review.json follow a document. An
    /// untitled document has no path: it is muted by its id for as long as the app runs, and under its path once it is
    /// saved. Settings > Editor turns the notice off for every document (SettingsViewModel.WarnFirstEditRewrite).
    /// </summary>
    public sealed class FirstEditMutesStore
    {
        /// <summary>The oldest dropped past this many: a notice shown again for a document muted long ago does no harm.</summary>
        public const int MaxCount = 1000;

        private readonly string file;

        private readonly object gate = new();

        private List<string> paths;

        private readonly HashSet<string> untitled = new(StringComparer.Ordinal);

        // The file written a snapshot at a time (Utilities.SnapshotWriter).
        private SnapshotWriter writer;
        private SnapshotWriter Writer => System.Threading.LazyInitializer.EnsureInitialized(ref writer, () => new SnapshotWriter(file, "first-edit mutes"));

        public FirstEditMutesStore(string file) => this.file = file;

        private void EnsureLoaded()
        {
            lock (gate)
            {
                if (paths != null) return;
                paths = new List<string>();
                try
                {
                    if (File.Exists(file))
                        paths = (JsonConvert.DeserializeObject<Stored>(File.ReadAllText(file))?.Paths ?? new List<string>())
                            .Where(x => !string.IsNullOrEmpty(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                }
                catch (Exception ex)
                {
                    // A file that cannot be read is kept aside, not overwritten by the next change.
                    Log.Debug($"first-edit mutes: {file} could not be read ({ex.Message}); kept as .bad");
                    try { File.Copy(file, file + ".bad", true); } catch { }
                }
            }
        }

        private sealed class Stored
        {
            [JsonProperty("version")]
            public int Version { get; set; } = 1;

            [JsonProperty("paths")]
            public List<string> Paths { get; set; } = new();
        }

        /// <summary>Whether the notice is muted for this document: its path, or (untitled) its id.</summary>
        public bool IsMuted(string path, string documentId)
        {
            if (string.IsNullOrEmpty(path))
            {
                lock (gate) return documentId != null && untitled.Contains(documentId);
            }
            EnsureLoaded();
            lock (gate) return paths.Any(x => RecentPaths.Same(x, path));
        }

        /// <summary>No notice for this document from now on (an untitled one: for as long as the app runs).</summary>
        public void Mute(string path, string documentId)
        {
            if (string.IsNullOrEmpty(path))
            {
                if (documentId != null) lock (gate) untitled.Add(documentId);
                return;
            }
            EnsureLoaded();
            lock (gate)
            {
                if (paths.Any(x => RecentPaths.Same(x, path))) return;
                paths.Add(RecentPaths.Normalize(path));
                if (paths.Count > MaxCount) paths.RemoveRange(0, paths.Count - MaxCount);
                Save();
            }
        }

        /// <summary>
        /// A document under another path: renamed or moved (<paramref name="keepOld"/> false, the old path follows), or
        /// saved under another name with the old file still there (true, both muted). A folder renamed takes the documents
        /// in it along. An untitled document saved (<paramref name="oldPath"/> null) takes its mute by its id.
        /// </summary>
        public void Follow(string oldPath, string newPath, bool keepOld, string documentId = null)
        {
            if (string.IsNullOrEmpty(newPath)) return;
            if (string.IsNullOrEmpty(oldPath))
            {
                bool wasMuted;
                lock (gate) wasMuted = documentId != null && untitled.Remove(documentId);
                if (wasMuted) Mute(newPath, null);
                return;
            }
            EnsureLoaded();
            lock (gate)
            {
                var from = RecentPaths.Normalize(oldPath);
                var to = RecentPaths.Normalize(newPath);
                var moved = new List<string>();
                for (var i = 0; i < paths.Count; i++)
                {
                    var path = RecentPaths.Normalize(paths[i]);
                    string now;
                    if (string.Equals(path, from, StringComparison.OrdinalIgnoreCase)) now = to;
                    else if (RecentPaths.IsInside(path, from)) now = to + path.Substring(from.TrimEnd('\\', '/').Length);
                    else continue;
                    if (!keepOld) paths[i] = now;
                    moved.Add(now);
                }
                if (moved.Count == 0) return;
                if (keepOld) paths.AddRange(moved.Where(x => !paths.Any(p => RecentPaths.Same(p, x))).ToList());
                paths = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                Save();
            }
        }

        /// <summary>Done when every change so far is on disk.</summary>
        public Task FlushAsync()
        {
            return Writer.FlushAsync();
        }

        // Under the lock: the text taken now, written after the writes before it, atomically.
        private void Save()
        {
            var text = JsonConvert.SerializeObject(new Stored { Paths = paths.ToList() }, Formatting.Indented);
            Writer.Write(text);
        }
    }

    /// <summary>The app's first-edit mutes (first-edit-mutes.json beside session.json): one store for every window.</summary>
    public static class FirstEditMutes
    {
        private static FirstEditMutesStore shared;

        private static readonly object gate = new();

        public static FirstEditMutesStore Shared
        {
            get
            {
                lock (gate) return shared ??= new FirstEditMutesStore(Path.Combine(Config.GetLocalFolderPath(), "first-edit-mutes.json"));
            }
        }
    }
}
