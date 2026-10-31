using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Typedown.Core.Utilities
{
    /// <summary>
    /// A CSS font-family value as a list: "Georgia, 'Microsoft YaHei UI', sans-serif" is three fonts, tried in turn. The
    /// font picker puts the font chosen first and keeps the ones after it (a Chinese fallback, a generic family).
    /// </summary>
    public static class FontFamilyList
    {
        private static readonly HashSet<string> generic = new(System.StringComparer.OrdinalIgnoreCase)
        {
            "serif", "sans-serif", "monospace", "cursive", "fantasy", "system-ui", "ui-serif", "ui-sans-serif", "ui-monospace", "ui-rounded", "math", "emoji", "fangsong",
        };

        /// <summary>The fonts in the value, in order, without quotes; commas inside quotes are part of a name.</summary>
        public static List<string> Split(string value)
        {
            var names = new List<string>();
            var current = new StringBuilder();
            char quote = '\0';
            foreach (var ch in value ?? "")
            {
                if (quote != '\0')
                {
                    if (ch == quote) quote = '\0';
                    else current.Append(ch);
                }
                else if (ch == '\'' || ch == '"') quote = ch;
                else if (ch == ',') { Add(); }
                else current.Append(ch);
            }
            Add();
            return names;

            void Add()
            {
                var name = current.ToString().Trim();
                if (name.Length > 0) names.Add(name);
                current.Clear();
            }
        }

        /// <summary>A name as CSS writes it: quoted when it has anything but letters, digits and hyphens; a generic family as it is.</summary>
        public static string Quote(string name) =>
            generic.Contains(name) || name.All(ch => char.IsLetterOrDigit(ch) || ch == '-') ? name : "'" + name.Replace("'", "\\'") + "'";

        /// <summary>The value with <paramref name="font"/> first: in place of the first font, the others kept (the chosen one not twice).</summary>
        public static string WithFirst(string value, string font)
        {
            var names = Split(value);
            if (names.Count > 0) names.RemoveAt(0);
            names.RemoveAll(n => string.Equals(n, font, System.StringComparison.OrdinalIgnoreCase));
            names.Insert(0, font);
            return string.Join(", ", names.Select(Quote));
        }

        /// <summary>The first font of the value (the one the picker shows as chosen), or null.</summary>
        public static string First(string value) => Split(value).FirstOrDefault();
    }
}
