#nullable disable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;

namespace Typedown.Core.Utilities
{
    /// <summary>What a folder search found: a name, a file's content, or the content of a file already listed by its name.</summary>
    public enum FolderSearchHitKind { Name, Content, AlsoContent }

    /// <summary>
    /// One result of the side pane's folder search. A file whose name and content both match is one result in the
    /// names group, its <see cref="AlsoContent"/> set when the content pass gets to it (the list shows a marker then).
    /// </summary>
    public sealed class FolderSearchHit : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public FolderSearchHitKind Kind { get; init; }

        public string FullPath { get; init; }

        public string Name { get; init; }

        /// <summary>The path under the search's root, with the system's separators.</summary>
        public string RelativePath { get; init; }

        public bool IsFolder { get; init; }

        /// <summary>The matching line around the query (content matches); null for a name match.</summary>
        public string Snippet => snippet;

        /// <summary>A name match whose content matches too.</summary>
        public bool AlsoContent => alsoContent;

        /// <summary>Opening it runs the in-document find for the query: its content matched.</summary>
        public bool ContentMatched => Kind == FolderSearchHitKind.Content || alsoContent;

        // Fields with get-only properties, raised by hand: the host's property weaver leaves these alone.
        private string snippet;
        private bool alsoContent;

        internal void MarkAlsoContent(string line)
        {
            if (alsoContent) return;
            alsoContent = true;
            snippet = line;
            OnPropertyChanged(nameof(AlsoContent));
            OnPropertyChanged(nameof(Snippet));
            OnPropertyChanged(nameof(ContentMatched));
        }

        internal FolderSearchHit WithSnippet(string line)
        {
            snippet = line;
            return this;
        }

        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public override string ToString() => RelativePath ?? Name ?? "";
    }

    /// <summary>The counts at the end of a pass (or of the whole search).</summary>
    public sealed record FolderSearchSummary(int Names, bool NamesAtCap, int Contents, bool ContentsAtCap, int FilesRead);

    /// <summary>
    /// The side pane's search through a folder, in two passes. The first reads only the directory listings and
    /// reports the folders and the Markdown/plain-text files whose names contain the query (any size, at most
    /// <see cref="NameCap"/>); the second reads those files' contents (up to <see cref="MaxFileBytes"/> each) and
    /// reports each one with a line that contains the query (at most <see cref="ContentCap"/>). A file the first pass
    /// listed is reported by the second as <see cref="FolderSearchHitKind.AlsoContent"/>, not again.
    /// Hidden and system entries, folders whose names start with a dot and node_modules are left out, their insides
    /// too; the root's own name is not matched. Case-insensitive substring, as the in-document find.
    /// No UI here: the side pane runs it off the UI thread and gets the hits through <c>report</c>, in order.
    /// </summary>
    public sealed class FolderSearch
    {
        public const int DefaultNameCap = 100;
        public const int DefaultContentCap = 200;
        public const long DefaultMaxFileBytes = 8 * 1024 * 1024;

        public int NameCap { get; init; } = DefaultNameCap;

        public int ContentCap { get; init; } = DefaultContentCap;

        public long MaxFileBytes { get; init; } = DefaultMaxFileBytes;

        /// <summary>
        /// Searches <paramref name="root"/> for <paramref name="query"/>. <paramref name="namesDone"/> is told the
        /// first pass's counts before the second starts. Throws <see cref="OperationCanceledException"/> when cancelled.
        /// </summary>
        public FolderSearchSummary Run(string root, string query, Action<FolderSearchHit> report, CancellationToken token, Action<FolderSearchSummary> namesDone = null)
        {
            if (string.IsNullOrEmpty(query)) return new FolderSearchSummary(0, false, 0, false, 0);
            var listed = new Dictionary<string, string>(StringComparer.Ordinal); // a listed file's path -> its relative path
            var toRead = new List<(string Path, string Name)>();
            var names = 0;

            // Pass 1: the listings, breadth first (the nearer a name is to the root, the earlier), each folder's entries by name.
            var pending = new Queue<string>();
            pending.Enqueue(root);
            while (pending.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                var dir = pending.Dequeue();
                List<FileSystemInfo> entries;
                try { entries = new DirectoryInfo(dir).EnumerateFileSystemInfos().OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList(); }
                catch (Exception) { continue; }
                foreach (var entry in entries)
                {
                    token.ThrowIfCancellationRequested();
                    if (!Included(entry)) continue;
                    var isFolder = entry is DirectoryInfo;
                    if (isFolder) pending.Enqueue(entry.FullName);
                    else if (((FileInfo)entry).Length <= MaxFileBytes) toRead.Add((entry.FullName, entry.Name));
                    if (names >= NameCap || entry.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    names++;
                    var hit = new FolderSearchHit { Kind = FolderSearchHitKind.Name, FullPath = entry.FullName, Name = entry.Name, RelativePath = Relative(root, entry.FullName), IsFolder = isFolder };
                    if (!isFolder) listed[entry.FullName] = hit.RelativePath;
                    report(hit);
                }
            }
            var namesAtCap = names >= NameCap;
            namesDone?.Invoke(new FolderSearchSummary(names, namesAtCap, 0, false, 0));

            // Pass 2: the contents, in the order the files were met.
            var contents = 0;
            var read = 0;
            foreach (var (path, name) in toRead)
            {
                if (contents >= ContentCap) break;
                token.ThrowIfCancellationRequested();
                read++;
                var snippet = FindLine(path, query, token);
                if (snippet == null) continue;
                if (listed.TryGetValue(path, out var relative))
                {
                    // The listed hit is marked by whoever shows it (FolderSearchResults, on its own thread).
                    report(new FolderSearchHit { Kind = FolderSearchHitKind.AlsoContent, FullPath = path, Name = name, RelativePath = relative }.WithSnippet(snippet));
                    continue;
                }
                contents++;
                report(new FolderSearchHit { Kind = FolderSearchHitKind.Content, FullPath = path, Name = name, RelativePath = Relative(root, path) }.WithSnippet(snippet));
            }
            return new FolderSearchSummary(names, namesAtCap, contents, contents >= ContentCap, read);
        }

        /// <summary>What the search looks at: no hidden or system entry, no dot folder or node_modules; files the editor opens.</summary>
        private static bool Included(FileSystemInfo entry)
        {
            if (entry.Attributes.HasFlag(FileAttributes.Hidden) || entry.Attributes.HasFlag(FileAttributes.System)) return false;
            if (entry is DirectoryInfo) return !entry.Name.StartsWith(".") && entry.Name != "node_modules";
            return FileTypeHelper.IsEditableTextFile(entry.Name);
        }

        /// <summary>The first line holding the query, cut to about 120 characters around it; null when none does or the file cannot be read.</summary>
        private static string FindLine(string path, string query, CancellationToken token)
        {
            try
            {
                foreach (var line in File.ReadLines(path))
                {
                    token.ThrowIfCancellationRequested();
                    var index = line.IndexOf(query, StringComparison.OrdinalIgnoreCase);
                    if (index < 0) continue;
                    var start = Math.Max(0, index - 40);
                    return (start > 0 ? "…" : "") + line.Substring(start, Math.Min(line.Length - start, 120)).Trim();
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { }
            return null;
        }

        private static string Relative(string root, string path) =>
            path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? path.Substring(root.Length).TrimStart('\\', '/') : path;
    }
}
