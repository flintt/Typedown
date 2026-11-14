using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Typedown.Mcp
{
    /// <summary>
    /// What the MCP tools do to a document's text on their own side: several find/replace pairs applied together, and a
    /// part of the text by lines or by a heading's section. Pure functions over the exact text read.
    /// </summary>
    public static class TextEdits
    {
        public sealed class Edit
        {
            public Edit(string find, string replacement, int expectedCount) { Find = find; Replacement = replacement; ExpectedCount = expectedCount; }
            public string Find { get; }
            public string Replacement { get; }
            public int ExpectedCount { get; }
        }

        /// <summary>Why a set of edits was refused: which edit (from 0), the kind, and what was found.</summary>
        public sealed class Refusal
        {
            public Refusal(int index, string kind, int actualCount, string message) { Index = index; Kind = kind; ActualCount = actualCount; Message = message; }
            public int Index { get; }
            public string Kind { get; }
            public int ActualCount { get; }
            public string Message { get; }
        }

        /// <summary>
        /// Every edit's find counted in the same text (ordinal, left to right, not overlapping itself); all of them must
        /// occur exactly as often as expected and no two edits may touch the same characters. Then all are applied at
        /// once. Either the new text, or why nothing is changed.
        /// </summary>
        public static (string? Text, Refusal? Refusal) Apply(string text, IReadOnlyList<Edit> edits)
        {
            var spans = new List<(int Start, int End, int Index, string Replacement)>();
            for (var i = 0; i < edits.Count; i++)
            {
                var edit = edits[i];
                if (string.IsNullOrEmpty(edit.Find)) return (null, new Refusal(i, "invalid_params", 0, $"edits[{i}].find is empty."));
                var found = new List<int>();
                for (var at = text.IndexOf(edit.Find, StringComparison.Ordinal); at >= 0; at = text.IndexOf(edit.Find, at + edit.Find.Length, StringComparison.Ordinal))
                    found.Add(at);
                if (found.Count != edit.ExpectedCount)
                    return (null, new Refusal(i, "match_count_mismatch", found.Count, $"edits[{i}].find occurs {found.Count} time(s), not {edit.ExpectedCount}."));
                spans.AddRange(found.Select(at => (at, at + edit.Find.Length, i, edit.Replacement)));
            }
            spans.Sort((a, b) => a.Start.CompareTo(b.Start));
            for (var k = 1; k < spans.Count; k++)
                if (spans[k].Start < spans[k - 1].End)
                    return (null, new Refusal(spans[k].Index, "edits_overlap", 0,
                        $"edits[{spans[k - 1].Index}] and edits[{spans[k].Index}] change the same text; make them one edit, or choose finds that do not overlap."));
            var result = new System.Text.StringBuilder(text.Length);
            var from = 0;
            foreach (var s in spans)
            {
                result.Append(text, from, s.Start - from).Append(s.Replacement);
                from = s.End;
            }
            result.Append(text, from, text.Length - from);
            return (result.ToString(), null);
        }

        /// <summary>The lines of the text, 1-based (the text uses \n; a last line ending in \n adds no empty line).</summary>
        public static string[] Lines(string text)
        {
            var lines = text.Split('\n');
            return text.EndsWith("\n", StringComparison.Ordinal) ? lines[..^1] : lines;
        }

        /// <summary>Lines startLine..endLine (inclusive, 1-based), clamped to the text, with \n after each.</summary>
        public static (string Text, int Start, int End) Slice(string text, int startLine, int endLine)
        {
            var lines = Lines(text);
            var start = Math.Max(1, startLine);
            var end = Math.Min(lines.Length, endLine);
            if (lines.Length == 0 || start > end) return ("", start, start - 1);
            return (string.Join("\n", lines[(start - 1)..end]) + "\n", start, end);
        }

        public sealed class Heading
        {
            public Heading(int line, int level, string text) { Line = line; Level = level; Text = text; }
            public int Line { get; }
            public int Level { get; }
            public string Text { get; }
        }

        private static readonly Regex Atx = new(@"^ {0,3}(#{1,6})(?:[ \t]+(.*?))?(?:[ \t]+#+)?[ \t]*$", RegexOptions.CultureInvariant);
        private static readonly Regex Fence = new(@"^ {0,3}(`{3,}|~{3,})", RegexOptions.CultureInvariant);

        /// <summary>The ATX headings (# ...) and their lines, outside fenced code blocks.</summary>
        public static List<Heading> Headings(string text)
        {
            var result = new List<Heading>();
            string? fence = null;
            var lines = Lines(text);
            for (var i = 0; i < lines.Length; i++)
            {
                var f = Fence.Match(lines[i]);
                if (fence == null && f.Success) { fence = f.Groups[1].Value; continue; }
                if (fence != null)
                {
                    if (f.Success && f.Groups[1].Value[0] == fence[0] && f.Groups[1].Value.Length >= fence.Length && lines[i].Trim().Trim(fence[0]).Length == 0) fence = null;
                    continue;
                }
                var m = Atx.Match(lines[i]);
                if (m.Success) result.Add(new Heading(i + 1, m.Groups[1].Value.Length, m.Groups[2].Value.Trim()));
            }
            return result;
        }

        /// <summary>
        /// A heading's section: its line to the line before the next heading of the same or a higher level. The heading is
        /// matched by its text (ignoring case and surrounding spaces); with several of that text, occurrence picks one (1 =
        /// the first). Null and the count of matches when it is not exactly one.
        /// </summary>
        public static (int Start, int End, Heading? Heading, int Matches) Section(string text, string heading, int occurrence = 0)
        {
            var all = Headings(text);
            var matches = all.Where(h => string.Equals(h.Text, heading.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 0 || (occurrence <= 0 && matches.Count > 1) || occurrence > matches.Count) return (0, 0, null, matches.Count);
            var h = matches[Math.Max(occurrence, 1) - 1];
            var next = all.FirstOrDefault(x => x.Line > h.Line && x.Level <= h.Level);
            return (h.Line, next == null ? Lines(text).Length : next.Line - 1, h, matches.Count);
        }
    }
}
