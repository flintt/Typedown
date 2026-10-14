using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Typedown.Core.Utilities
{
    /// <summary>
    /// How a recent or favourite file or folder is named in a list: its own name and the folder it is in. The whole path
    /// is long, and two "notes.md" are told apart by their folders; the full path goes in the tooltip.
    /// </summary>
    public static class RecentPaths
    {
        /// <summary>The file's or folder's own name; a drive ("C:\") is its path.</summary>
        public static string Name(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            var trimmed = path.TrimEnd('\\', '/');
            var name = Path.GetFileName(trimmed);
            return string.IsNullOrEmpty(name) ? path : name;
        }

        /// <summary>The name of the folder it is in ("docs" for D:\work\docs\a.md, "D:\" for D:\a.md), or null for a drive.</summary>
        public static string Parent(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            string parent;
            try { parent = Path.GetDirectoryName(path.TrimEnd('\\', '/')); }
            catch { return null; }
            if (string.IsNullOrEmpty(parent)) return null;
            return Name(parent);
        }

        /// <summary>The folder it is in, whole ("D:\work\docs"), or null.</summary>
        public static string ParentPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try { return Path.GetDirectoryName(path.TrimEnd('\\', '/')); }
            catch { return null; }
        }

        /// <summary>"a.md — docs": the name and the folder it is in.</summary>
        public static string Label(string path)
        {
            var parent = Parent(path);
            return parent == null ? Name(path) : $"{Name(path)} — {parent}";
        }

        /// <summary>
        /// The folders above <paramref name="folder"/>, nearest first, up to its drive: D:\a\b gives D:\a, D:\.
        /// </summary>
        public static IReadOnlyList<string> Ancestors(string folder)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(folder)) return list;
            string current;
            try { current = Path.GetDirectoryName(folder.TrimEnd('\\', '/')); }
            catch { return list; }
            while (!string.IsNullOrEmpty(current) && list.Count < 64)
            {
                list.Add(current);
                string next;
                try { next = Path.GetDirectoryName(current); }
                catch { break; }
                if (string.Equals(next, current, StringComparison.OrdinalIgnoreCase)) break;
                current = next;
            }
            return list;
        }

        /// <summary>The same file or folder, whatever the case and a trailing separator (Windows paths).</summary>
        public static bool Same(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            return string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The full path without a trailing separator (a drive keeps its own), for comparing.</summary>
        public static string Normalize(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            string full;
            try { full = Path.GetFullPath(path); }
            catch { full = path; }
            var trimmed = full.TrimEnd('\\', '/');
            // "C:" alone is the current directory of that drive, not its root.
            return trimmed.Length == 0 || trimmed.EndsWith(":") ? full : trimmed;
        }

        /// <summary><paramref name="path"/> is <paramref name="folder"/> or inside it.</summary>
        public static bool IsInside(string path, string folder)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(folder)) return false;
            var p = Normalize(path);
            var f = Normalize(folder).TrimEnd('\\', '/');
            return string.Equals(p, f, StringComparison.OrdinalIgnoreCase)
                || p.StartsWith(f + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || p.StartsWith(f + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Whether files and folders still exist, looked at off the UI thread and given up on after a while: a recent file
    /// on a network drive that is not connected can take many seconds to answer, and a list must not wait for it. What
    /// does not answer in time counts as there - an entry is greyed out only once it is known to be gone (and never
    /// removed: a USB stick or a share may just not be plugged in).
    /// </summary>
    public static class PathProbe
    {
        private static readonly ConcurrentDictionary<string, (bool Exists, DateTime At)> cache = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>How long an answer is reused: a list opened twice in a row does not ask the disk twice.</summary>
        public static TimeSpan CacheFor { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>The default wait for one round of answers.</summary>
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);

        /// <summary>The paths known to be gone (each looked at once, all at the same time); <paramref name="exists"/> is for tests.</summary>
        public static async Task<HashSet<string>> MissingAsync(IEnumerable<string> paths, TimeSpan? timeout = null, Func<string, bool> exists = null)
        {
            exists ??= p => File.Exists(p) || Directory.Exists(p);
            var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var now = DateTime.UtcNow;
            var asked = new List<(string Path, Task<bool> Answer)>();
            foreach (var path in (paths ?? Enumerable.Empty<string>()).Where(p => !string.IsNullOrEmpty(p)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (cache.TryGetValue(path, out var known) && now - known.At < CacheFor)
                {
                    if (!known.Exists) missing.Add(path);
                    continue;
                }
                var probe = exists;
                asked.Add((path, Task.Run(() =>
                {
                    var answer = probe(path);
                    cache[path] = (answer, DateTime.UtcNow);
                    return answer;
                })));
            }
            if (asked.Count == 0) return missing;
            var all = Task.WhenAll(asked.Select(a => a.Answer));
            await Task.WhenAny(all, Task.Delay(timeout ?? DefaultTimeout)).ConfigureAwait(false);
            foreach (var (path, answer) in asked)
                if (answer.IsCompletedSuccessfully && !answer.Result) missing.Add(path);
            return missing;
        }

        /// <summary>Forgets what was found (a list cleared, a test starting afresh).</summary>
        public static void Forget(string path = null)
        {
            if (path == null) cache.Clear();
            else cache.TryRemove(path, out _);
        }
    }
}
