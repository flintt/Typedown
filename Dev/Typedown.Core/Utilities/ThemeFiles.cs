using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Typedown.Core.Enums;

namespace Typedown.Core.Utilities
{
    /// <summary>A custom theme read from the themes folder. See docs/custom-theme.md for the format.</summary>
    public class CustomTheme
    {
        public string Id { get; set; }

        public string Name { get; set; }

        /// <summary>The built-in theme this one builds on; anything it does not set comes from there.</summary>
        public AppTheme Base { get; set; }

        public string Accent { get; set; }

        public string Author { get; set; }

        public string Path { get; set; }

        /// <summary>Window surface behind the editor; null keeps the colour of the built-in theme.</summary>
        public string Background { get; set; }

        /// <summary>Panels: side pane, tab bar, status bar.</summary>
        public string Surface { get; set; }

        /// <summary>Text in the shell.</summary>
        public string Foreground { get; set; }

        /// <summary>Separators between the panels.</summary>
        public string Border { get; set; }
    }

    /// <summary>
    /// Custom themes: one CSS file each in %LOCALAPPDATA%\Typedown\themes. The first comment block carries the
    /// metadata, the rest is applied to the editor after the built-in theme it names.
    /// </summary>
    public static class ThemeFiles
    {
        public static string Folder => System.IO.Path.Combine(Config.GetLocalFolderPath(), "themes");

        public static IReadOnlyList<CustomTheme> List()
        {
            var themes = new List<CustomTheme>();
            try
            {
                if (!Directory.Exists(Folder)) return themes;
                foreach (var path in Directory.EnumerateFiles(Folder, "*.css").OrderBy(x => x))
                {
                    try
                    {
                        themes.Add(Parse(path, File.ReadAllText(path)));
                    }
                    catch (Exception ex)
                    {
                        Log.Debug($"theme {System.IO.Path.GetFileName(path)}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"theme folder: {ex.Message}");
            }
            return themes;
        }

        public static CustomTheme Find(string id) =>
            string.IsNullOrEmpty(id) ? null : List().FirstOrDefault(x => x.Id == id);

        /// <summary>The CSS to hand the editor, empty when the theme is gone.</summary>
        public static string Read(string id)
        {
            var theme = Find(id);
            if (theme == null) return string.Empty;
            try
            {
                return File.ReadAllText(theme.Path);
            }
            catch (Exception ex)
            {
                Log.Debug($"read theme {id}: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>Creates the folder and, the first time, an example theme to start from.</summary>
        public static void EnsureFolder()
        {
            try
            {
                if (Directory.Exists(Folder)) return;
                Directory.CreateDirectory(Folder);
                File.WriteAllText(System.IO.Path.Combine(Folder, "example.css"), Example);
            }
            catch (Exception ex)
            {
                Log.Debug($"create theme folder: {ex.Message}");
            }
        }

        /// <summary>"#268bd2" or "#26d" as a brush; null when the value is missing or unreadable.</summary>
        public static Windows.UI.Xaml.Media.Brush Brush(string colour)
        {
            var text = colour?.Trim().TrimStart('#');
            if (string.IsNullOrEmpty(text)) return null;
            try
            {
                if (text.Length == 3)
                    text = string.Concat(text[0], text[0], text[1], text[1], text[2], text[2]);
                if (text.Length < 6) return null;
                var r = Convert.ToByte(text.Substring(0, 2), 16);
                var g = Convert.ToByte(text.Substring(2, 2), 16);
                var b = Convert.ToByte(text.Substring(4, 2), 16);
                return new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, r, g, b));
            }
            catch
            {
                return null; // a theme with a broken colour keeps the built-in one
            }
        }

        /// <summary>Black or white, whichever can be read on the given colour; null when there is no colour.</summary>
        public static Windows.UI.Xaml.Media.Brush Readable(string colour)
        {
            var brush = Brush(colour) as Windows.UI.Xaml.Media.SolidColorBrush;
            if (brush == null) return null;
            var c = brush.Color;
            var luminance = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255;
            var tone = (byte)(luminance > 0.55 ? 26 : 240);
            return new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, tone, tone, tone));
        }

        private static CustomTheme Parse(string path, string css)
        {
            var id = System.IO.Path.GetFileNameWithoutExtension(path);
            var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var header = Regex.Match(css, @"/\*(.*?)\*/", RegexOptions.Singleline);
            if (header.Success)
            {
                foreach (Match m in Regex.Matches(header.Groups[1].Value, @"^[\s*]*([A-Za-z]+)\s*:\s*(.+?)\s*$", RegexOptions.Multiline))
                    meta[m.Groups[1].Value] = m.Groups[2].Value;
            }
            var baseTheme = AppTheme.Light;
            if (meta.TryGetValue("base", out var b))
            {
                switch (b.Trim().ToLowerInvariant())
                {
                    case "dark": baseTheme = AppTheme.Dark; break;
                    case "black": baseTheme = AppTheme.Black; break;
                }
            }
            string Value(string key) => meta.TryGetValue(key, out var v) && v.Length > 0 ? v : null;
            return new CustomTheme
            {
                Id = id,
                Name = Value("name") ?? id,
                Base = baseTheme,
                Accent = Value("accent"),
                Author = Value("author"),
                Path = path,
                Background = Value("background"),
                Surface = Value("surface"),
                Foreground = Value("foreground"),
                Border = Value("border")
            };
        }

        private const string Example = @"/* Typedown theme
 * name: Example
 * base: light
 * accent: #268bd2
 * author: you
 *
 * These four colour the window around the editor (side pane, tab bar, status bar, separators). Leave them
 * out and the window keeps the colours of the built-in theme named by ""base"".
 * background: #fdf6e3
 * surface: #f2ead7
 * foreground: #073642
 * border: #e0dbc8
 *
 * Every declaration below overrides the built-in theme named by ""base"", so a theme only states what it
 * changes. The full list of variables is in docs/custom-theme.md.
 */
:root {
  --editorBgColor: #fdf6e3;
  --editorColor: #073642;
  --codeBgColor: #eee8d5;
  --codeBlockBgColor: #eee8d5;
  --tableBorderColor: #e0dbc8;
  --floatBgColor: #fdf6e3;
  --itemBgColor: #eee8d5;
}
";
    }
}
