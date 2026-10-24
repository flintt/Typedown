#nullable enable
using System.Collections.Generic;
using System.Globalization;

namespace Typedown.Core.Utilities
{
    /// <summary>
    /// The culture dates and numbers are written in for an interface language. A language without a region ("zh-Hant",
    /// "pt") is a neutral culture, whose formats are some region's default - Hong Kong's day/month/year for zh-Hant -
    /// so it is given the region its translation is written for. Plain .NET, so it is tested on Linux
    /// (Dev/Typedown.Automation.Tests/FormatCultureTests.cs).
    /// </summary>
    public static class FormatCulture
    {
        // The regions the translations are written for, where the neutral culture's default is another one's.
        private static readonly Dictionary<string, string> Regions = new(System.StringComparer.OrdinalIgnoreCase)
        {
            ["zh-Hant"] = "zh-TW",
            ["zh-Hans"] = "zh-CN",
            ["pt"] = "pt-BR",
        };

        public static CultureInfo For(string language)
        {
            var name = Regions.TryGetValue(language, out var region) ? region : language;
            var culture = CultureInfo.GetCultureInfo(name);
            if (!culture.IsNeutralCulture) return culture;
            try { return CultureInfo.CreateSpecificCulture(name); }
            catch (CultureNotFoundException) { return culture; }
        }
    }
}
