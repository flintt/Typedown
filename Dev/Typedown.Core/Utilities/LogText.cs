using System;
using System.Text.RegularExpressions;

namespace Typedown.Core.Utilities
{
    /// <summary>
    /// What the debug log may say about a document: where and how much, never what it says. The log is read to find
    /// faults (and sent along with a report), and a document's text is the reader's own - a load's line ended with the
    /// first words of the document, an outline line with a heading, an error with the formula or diagram it failed on.
    /// File names and paths may be logged; the text in a file may not (the E2E case DL01 reads a real log for marker words).
    /// </summary>
    public static class LogText
    {
        /// <summary>A text by its size alone: "24 chars", or "none".</summary>
        public static string Size(string text) => text == null ? "none" : $"{text.Length} chars";

        /// <summary>A heading of the outline by its place: "heading 3 of 40 (level 2, 24 chars)".</summary>
        public static string Heading(int index, int count, int level, string text) =>
            index < 0 ? $"a heading not in the outline of {count} ({Size(text)})" : $"heading {index + 1} of {count} (level {level}, {Size(text)})";

        private static readonly Regex Quoted = new(@"'[^'\r\n]*'|""[^""\r\n]*""|`[^`\r\n]*`|‘[^’\r\n]*’|“[^”\r\n]*”|「[^」\r\n]*」", RegexOptions.Compiled);

        private static readonly Regex RootedPath = new(@"^([A-Za-z]:[\\/]|\\\\|/)", RegexOptions.Compiled);

        /// <summary>The most an error's message keeps.</summary>
        public const int ErrorLimit = 160;

        /// <summary>
        /// An error's message for the log: its first line, anything quoted in it replaced by its length (a renderer's
        /// error quotes the formula or diagram it failed on; the script engine's quote names of the code) - a quoted
        /// rooted path stays - and at most <see cref="ErrorLimit"/> characters, the rest by its length. The type and the
        /// stack say where it went wrong; the message's words are not needed for that.
        /// </summary>
        public static string Error(string message)
        {
            if (string.IsNullOrEmpty(message)) return message ?? "";
            var newline = message.IndexOfAny(new[] { '\r', '\n' });
            var first = newline < 0 ? message : message.Substring(0, newline);
            var rest = newline < 0 ? 0 : message.Length - newline;
            first = Quoted.Replace(first, m =>
            {
                var inner = m.Value.Substring(1, m.Value.Length - 2);
                return RootedPath.IsMatch(inner) ? m.Value : $"<{inner.Length} chars>";
            });
            if (first.Length > ErrorLimit)
            {
                rest += first.Length - ErrorLimit;
                first = first.Substring(0, ErrorLimit);
            }
            return rest > 0 ? $"{first} (+{rest} chars left out)" : first;
        }

        /// <summary>
        /// A stack for the log: only its frames ("at ..." lines, a script's "name@place" ones), at most <paramref name="frames"/>.
        /// The first lines of a script's stack repeat its message.
        /// </summary>
        public static string Stack(string stack, int frames = 12)
        {
            if (string.IsNullOrEmpty(stack)) return "";
            var kept = new System.Collections.Generic.List<string>();
            foreach (var raw in stack.Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith("at ", StringComparison.Ordinal) || Regex.IsMatch(line, @"^[\w$.<>]*@\S+:\d+:\d+$")) kept.Add(line);
                if (kept.Count >= frames) break;
            }
            return string.Join(" | ", kept);
        }

        /// <summary>A link's address for the log: its scheme and host ("https://example.com"), or its scheme alone.</summary>
        public static string Address(string address)
        {
            if (string.IsNullOrEmpty(address)) return address ?? "";
            if (Uri.TryCreate(address, UriKind.Absolute, out var uri))
            {
                if (uri.IsFile) return "file:" + System.IO.Path.GetFileName(uri.LocalPath);
                return uri.Scheme is "http" or "https" ? $"{uri.Scheme}://{uri.Host}" : uri.Scheme + ":";
            }
            return $"a relative address ({Size(address)})";
        }
    }
}
