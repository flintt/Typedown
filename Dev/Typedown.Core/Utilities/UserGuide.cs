using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Typedown.Core.Utilities
{
    /// <summary>
    /// The user guide Help > User guide opens: Chinese for a Chinese interface, English otherwise. It ships beside the
    /// app (Resources\Guide, a read-only folder in the installed and Store builds), so a copy is opened from the app's own
    /// data folder: an edit to it cannot fail for want of rights, nor change what the next opening shows. The copy names
    /// the edition (Typeleaf) where the text says Typedown, as the installer's copy of the documents does. An edition
    /// with features of its own ships a supplement beside the guide (user-guide.edition.md, user-guide.edition.en.md),
    /// added at the end of the copy.
    /// </summary>
    public static class UserGuide
    {
        public static string Prepare(string language)
        {
            var file = (language ?? "").StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "user-guide.md" : "user-guide.en.md";
            var source = Path.Combine(AppContext.BaseDirectory, "Resources", "Guide", file);
            var text = File.ReadAllText(source);
            var supplement = Path.Combine(Path.GetDirectoryName(source), Path.GetFileNameWithoutExtension(file).Replace("user-guide", "user-guide.edition") + ".md");
            if (File.Exists(supplement)) text = text.TrimEnd() + "\n\n" + File.ReadAllText(supplement).Trim() + "\n";
            if (Typedown.Automation.Brand.Name != "Typedown") // brand-ok: the name the guide is written with
            {
                text = text.Replace("typedownctl", Typedown.Automation.Brand.CliName); // brand-ok
                text = Regex.Replace(text, @"(?<![\w/\\.-])Typedown(?![-_.]\w|\w)", Typedown.Automation.Brand.Name); // brand-ok
            }
            var folder = Path.Combine(Config.LocalAppDataFolder, "guide");
            Directory.CreateDirectory(folder);
            var target = Path.Combine(folder, file);
            File.WriteAllText(target, text);
            return target;
        }

        /// <summary>The interface's language: the one chosen in the settings, else the system's first.</summary>
        public static string InterfaceLanguage() =>
            !string.IsNullOrEmpty(Locale.AppliedLanguage) ? Locale.AppliedLanguage : Windows.Globalization.ApplicationLanguages.Languages.FirstOrDefault() ?? "en";
    }
}
