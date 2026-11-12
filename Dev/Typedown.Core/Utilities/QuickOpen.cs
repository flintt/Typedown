using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Typedown.Core.Utilities
{
    /// <summary>A file quick open offers: its path, its name, where it is (shown under the name), and whether it was opened lately.</summary>
    public sealed record QuickOpenCandidate(string Path, string Name, string Relative, bool Recent)
    {
        // What a list row is called (a screen reader reads the row by it).
        public override string ToString() => Name;
    }

    /// <summary>
    /// Quick open (File > Quick Open, Ctrl+P): the files of the folder in the side pane and the files opened lately, found
    /// by a few letters of the name - in order, not necessarily together ("tdo" finds "todo.md") - or of the path.
    /// </summary>
    public static class QuickOpen
    {
        /// <summary>Listed at most from a folder: a whole drive opened as the folder is not walked to its end.</summary>
        public const int MaxFiles = 20000;

        /// <summary>The files under the folder, nearer ones first, as the folder search walks it (FolderSearch.Included).</summary>
        public static List<string> Files(string root, CancellationToken token, int max = MaxFiles)
        {
            var found = new List<string>();
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return found;
            var pending = new Queue<string>();
            pending.Enqueue(root);
            while (pending.Count > 0 && found.Count < max)
            {
                token.ThrowIfCancellationRequested();
                List<FileSystemInfo> entries;
                try { entries = new DirectoryInfo(pending.Dequeue()).EnumerateFileSystemInfos().OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList(); }
                catch (Exception) { continue; }
                foreach (var entry in entries)
                {
                    if (!FolderSearch.Included(entry)) continue;
                    if (entry is DirectoryInfo) pending.Enqueue(entry.FullName);
                    else if (found.Count < max) found.Add(entry.FullName);
                }
            }
            return found;
        }

        /// <summary>
        /// The candidates the query finds, best first. Every word of the query is to be found in the name or the path. A
        /// name that is the query, then one that starts with it, then one that has it, then one that has its letters in
        /// order; a file opened lately before another of the same standing, then the shorter path. No query: the files
        /// opened lately first, in their order, then the folder's.
        /// </summary>
        public static List<QuickOpenCandidate> Rank(string query, IEnumerable<QuickOpenCandidate> candidates, int limit = 50)
        {
            var words = (query ?? "").Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var all = candidates.ToList();
            if (words.Length == 0)
                return all.Where(c => c.Recent).Concat(all.Where(c => !c.Recent)).Take(limit).ToList();
            var scored = new List<(QuickOpenCandidate Candidate, int Score, int Order)>();
            for (var i = 0; i < all.Count; i++)
            {
                var c = all[i];
                var total = 0;
                foreach (var word in words)
                {
                    var score = Score(word, c.Name, c.Relative ?? c.Path);
                    if (score <= 0) { total = 0; break; }
                    total += score;
                }
                if (total <= 0) continue;
                if (c.Recent) total += 50;
                scored.Add((c, total, i));
            }
            return scored
                .OrderByDescending(x => x.Score)
                .ThenBy(x => (x.Candidate.Relative ?? x.Candidate.Path).Length)
                .ThenBy(x => x.Order)
                .Take(limit)
                .Select(x => x.Candidate)
                .ToList();
        }

        private static int Score(string word, string name, string path)
        {
            var stem = Path.GetFileNameWithoutExtension(name);
            if (string.Equals(name, word, StringComparison.OrdinalIgnoreCase) || string.Equals(stem, word, StringComparison.OrdinalIgnoreCase)) return 1000;
            if (name.StartsWith(word, StringComparison.OrdinalIgnoreCase)) return 800;
            var at = name.IndexOf(word, StringComparison.OrdinalIgnoreCase);
            if (at >= 0) return 600 - Math.Min(at, 100);
            if (path.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0) return 400;
            if (InOrder(word, name, out var gaps)) return 300 - Math.Min(gaps, 200);
            if (InOrder(word, path, out gaps)) return Math.Max(1, 100 - Math.Min(gaps, 99));
            return 0;
        }

        // The word's letters in the text in order, and how many letters of the text lie between them.
        private static bool InOrder(string word, string text, out int gaps)
        {
            gaps = 0;
            var at = -1;
            foreach (var ch in word)
            {
                var next = text.IndexOf(ch.ToString(), at + 1, StringComparison.OrdinalIgnoreCase);
                if (next < 0) return false;
                if (at >= 0) gaps += next - at - 1;
                at = next;
            }
            return true;
        }
    }
}
