using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Typedown.Core.Models;

namespace Typedown.Core.Services
{
    /// <summary>
    /// Remembers the caret position per document so reopening a file continues where you left off (upstream #50).
    /// Stored as JSON under the app's local folder; capped to the most recently used entries.
    /// </summary>
    public static class CursorMemory
    {
        private class Entry
        {
            public CursorState Cursor { get; set; }
            /// <summary>Vertical scroll offset (px); the only position reading mode has, since it never moves a caret.</summary>
            public double? ScrollY { get; set; }
            /// <summary>
            /// The block at the top of the window with the offset (the page's own JSON: its text, its height, how far
            /// through): what the next load puts back where it was, the page's preview first (services/loadPreview).
            /// </summary>
            public string ScrollAnchor { get; set; }
            public DateTime Time { get; set; }
        }

        private const int MaxEntries = 500;
        private static readonly object sync = new();
        private static Dictionary<string, Entry> entries;
        private static bool dirty;

        private static string StorePath => Path.Combine(Config.GetLocalFolderPath(), "cursors.json");

        private static void EnsureLoaded()
        {
            if (entries != null) return;
            try
            {
                entries = File.Exists(StorePath)
                    ? JsonConvert.DeserializeObject<Dictionary<string, Entry>>(File.ReadAllText(StorePath)) ?? new()
                    : new();
            }
            catch
            {
                entries = new();
            }
        }

        public static CursorState Get(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            lock (sync)
            {
                EnsureLoaded();
                return entries.TryGetValue(Normalize(path), out var entry) ? entry.Cursor : null;
            }
        }

        public static void Set(string path, CursorState cursor)
        {
            if (string.IsNullOrEmpty(path) || cursor == null) return;
            lock (sync)
            {
                EnsureLoaded();
                var key = Normalize(path);
                entries.TryGetValue(key, out var existing);
                entries[key] = new Entry { Cursor = cursor, ScrollY = existing?.ScrollY, ScrollAnchor = existing?.ScrollAnchor, Time = DateTime.UtcNow };
                dirty = true;
            }
        }

        public static double? GetScroll(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            lock (sync)
            {
                EnsureLoaded();
                return entries.TryGetValue(Normalize(path), out var entry) ? entry.ScrollY : null;
            }
        }

        /// <summary>The block at the top of the window kept with the offset (see <see cref="Entry.ScrollAnchor"/>), or null.</summary>
        public static string GetScrollAnchor(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            lock (sync)
            {
                EnsureLoaded();
                return entries.TryGetValue(Normalize(path), out var entry) ? entry.ScrollAnchor : null;
            }
        }

        public static void SetScroll(string path, double scrollY, string anchor = null)
        {
            if (string.IsNullOrEmpty(path)) return;
            lock (sync)
            {
                EnsureLoaded();
                var key = Normalize(path);
                var cursor = entries.TryGetValue(key, out var existing) ? existing.Cursor : null;
                entries[key] = new Entry { Cursor = cursor, ScrollY = scrollY, ScrollAnchor = anchor, Time = DateTime.UtcNow };
                dirty = true;
            }
        }

        /// <summary>Writes pending changes to disk; cheap no-op when nothing changed.</summary>
        public static void Flush()
        {
            Dictionary<string, Entry> snapshot;
            lock (sync)
            {
                if (!dirty || entries == null) return;
                if (entries.Count > MaxEntries)
                    foreach (var key in entries.OrderBy(x => x.Value.Time).Take(entries.Count - MaxEntries).Select(x => x.Key).ToList())
                        entries.Remove(key);
                snapshot = new(entries);
                dirty = false;
            }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StorePath));
                File.WriteAllText(StorePath, JsonConvert.SerializeObject(snapshot));
            }
            catch
            {
                // best effort
            }
        }

        private static string Normalize(string path) => path.Replace('/', '\\').ToLowerInvariant();
    }
}
