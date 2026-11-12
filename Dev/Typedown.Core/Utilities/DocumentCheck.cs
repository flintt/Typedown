using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Typedown.Core.Utilities
{
    public enum DocumentProblemKind
    {
        /// <summary>A picture whose file is not there.</summary>
        MissingImage,
        /// <summary>A link to a file or folder beside the document that is not there.</summary>
        MissingFile,
        /// <summary>A link to a heading of the document (#...) that no heading has.</summary>
        MissingHeading,
        /// <summary>A formula or diagram the editor could not draw (the page says which).</summary>
        NotDrawn,
    }

    /// <summary>One problem: what it is, its line (1-based; 0 when not known), what it points at, and the text to find it by.</summary>
    public sealed record DocumentProblem(DocumentProblemKind Kind, int Line, string Target, string Find);

    /// <summary>
    /// File > Check Document, and before an export: what in the document will not come out right - pictures and linked
    /// files that are not there, links to headings the document does not have. Read from the Markdown as written; what
    /// is in code (fenced, indented by a fence, or between backticks) is text, not a link.
    /// </summary>
    public static class DocumentCheck
    {
        private static readonly Regex fence = new(@"^ {0,3}(`{3,}|~{3,})");
        private static readonly Regex codeSpan = new(@"(`+)(?:(?!\1).)+?\1");
        private static readonly Regex image = new(@"!\[(?:[^\]\\]|\\.)*\]\(\s*(?:<([^>]*)>|([^)\s]+))(?:\s+(?:""[^""]*""|'[^']*'|\([^)]*\)))?\s*\)");
        private static readonly Regex htmlImage = new(@"<img\b[^>]*?\bsrc\s*=\s*(?:""([^""]*)""|'([^']*)')", RegexOptions.IgnoreCase);
        private static readonly Regex link = new(@"(?<!!)\[(?:[^\]\\]|\\.)*\]\(\s*(?:<([^>]*)>|([^)\s]+))(?:\s+(?:""[^""]*""|'[^']*'|\([^)]*\)))?\s*\)");
        private static readonly Regex definition = new(@"^ {0,3}\[(?!\^)[^\]]+\]:\s*(?:<([^>]*)>|(\S+))");
        private static readonly Regex scheme = new(@"^[a-zA-Z][a-zA-Z0-9+.-]*:");

        /// <param name="documentFolder">The document's folder, for links; null for a document not yet saved (its links are not looked at).</param>
        /// <param name="imageFolder">The folder pictures are relative to (the document's, or its image root); null as above.</param>
        /// <param name="hasHeading">Whether a #fragment names a heading of the document (<see cref="HeadingMatcher"/>); null
        /// when the headings are not known (source mode, the page not answering): links to headings are then not looked at.</param>
        /// <param name="exists">Whether a path is there (a file or folder); the disk by default.</param>
        public static List<DocumentProblem> Run(string markdown, string documentFolder, string imageFolder, Func<string, bool> hasHeading, Func<string, bool> exists = null)
        {
            exists ??= path => File.Exists(path) || Directory.Exists(path);
            var problems = new List<DocumentProblem>();
            var lines = (markdown ?? "").Replace("\r\n", "\n").Split('\n');
            string open = null;
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var f = fence.Match(line);
                if (open != null)
                {
                    // Closed by a run of the same mark, at least as long, and nothing else on the line.
                    var closing = line.Trim();
                    if (f.Success && closing.Length >= open.Length && closing.All(ch => ch == open[0]))
                        open = null;
                    continue;
                }
                if (f.Success)
                {
                    open = f.Groups[1].Value;
                    continue;
                }
                var text = codeSpan.Replace(line, m => new string(' ', m.Length));
                foreach (Match m in image.Matches(text))
                    CheckPicture(Target(m), i + 1, m.Value);
                foreach (Match m in htmlImage.Matches(text))
                    CheckPicture(Target(m), i + 1, m.Value);
                foreach (Match m in link.Matches(text))
                    CheckLink(Target(m), i + 1, m.Value);
                var d = definition.Match(text);
                if (d.Success) CheckLink(Target(d), i + 1, d.Value);
            }
            return problems;

            void CheckPicture(string target, int line, string found)
            {
                if (!IsLocal(target, out var path) || path.StartsWith("#")) return;
                var full = Resolve(imageFolder, path);
                if (full != null && !exists(full)) problems.Add(new(DocumentProblemKind.MissingImage, line, target, found));
            }

            void CheckLink(string target, int line, string found)
            {
                if (!IsLocal(target, out var path)) return;
                if (path.StartsWith("#"))
                {
                    var anchor = Decode(path.Substring(1));
                    if (hasHeading != null && anchor.Length > 0 && !hasHeading(anchor))
                        problems.Add(new(DocumentProblemKind.MissingHeading, line, target, found));
                    return;
                }
                var full = Resolve(documentFolder, path);
                if (full != null && !exists(full)) problems.Add(new(DocumentProblemKind.MissingFile, line, target, found));
            }
        }

        /// <summary>
        /// A #fragment matched against the headings as a click on the link matches it (the page's scrollToAnchor): the
        /// heading's anchor, its text, or either with everything but letters and digits left out ("1.1-研究" is "11 研究").
        /// </summary>
        public static Func<string, bool> HeadingMatcher(IEnumerable<(string Slug, string Text)> headings)
        {
            static string Loose(string text) => Regex.Replace((text ?? "").ToLowerInvariant(), @"[^\p{L}\p{N}]+", "");
            var exact = new HashSet<string>(StringComparer.Ordinal);
            var loose = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (slug, text) in headings)
            {
                exact.Add(slug ?? "");
                exact.Add((text ?? "").ToLowerInvariant());
                loose.Add(Loose(slug));
                loose.Add(Loose(text));
            }
            loose.Remove("");
            return fragment => exact.Contains(fragment.ToLowerInvariant()) || loose.Contains(Loose(fragment));
        }

        private static string Target(Match m)
        {
            for (var g = 1; g < m.Groups.Count; g++)
                if (m.Groups[g].Success) return m.Groups[g].Value.Trim();
            return "";
        }

        // A file beside the document (or #, a place in it); the path without its ?query and #place, decoded. file: too.
        private static bool IsLocal(string target, out string path)
        {
            path = null;
            if (string.IsNullOrEmpty(target) || target.StartsWith("//")) return false;
            if (target.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                if (!Uri.TryCreate(target, UriKind.Absolute, out var uri)) return false;
                path = uri.LocalPath;
                return true;
            }
            if (scheme.IsMatch(target) && !Regex.IsMatch(target, @"^[a-zA-Z]:[\\/]")) return false;
            if (target.StartsWith("#")) { path = target; return true; }
            var cut = target.IndexOfAny(new[] { '?', '#' });
            path = Decode(cut >= 0 ? target.Substring(0, cut) : target);
            return path.Length > 0;
        }

        private static string Decode(string text)
        {
            try { return Uri.UnescapeDataString(text); }
            catch { return text; }
        }

        private static string Resolve(string folder, string path)
        {
            try
            {
                path = path.Replace('/', Path.DirectorySeparatorChar);
                if (Path.IsPathRooted(path)) return Path.GetFullPath(path);
                return string.IsNullOrEmpty(folder) ? null : Path.GetFullPath(Path.Combine(folder, path));
            }
            catch { return null; }
        }
    }
}
