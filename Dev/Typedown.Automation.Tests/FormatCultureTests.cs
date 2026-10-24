using System;
using Typedown.Core.Utilities;
using Xunit;

namespace Typedown.Automation.Tests
{
    /// <summary>Typedown.Core FormatCulture: an interface language's dates in the region its translation is written for.</summary>
    public class FormatCultureTests
    {
        private static readonly DateTime Day = new(2026, 10, 24);

        [Fact]
        public void Traditional_Chinese_writes_dates_as_Taiwan_does_year_first()
        {
            // Not Hong Kong's "24/10/2026", the neutral zh-Hant's default.
            Assert.Equal("2026/10/24", Day.ToString("d", FormatCulture.For("zh-Hant")));
            Assert.Equal("2026/10/24", Day.ToString("d", FormatCulture.For("zh-Hans")));
        }

        [Fact]
        public void A_language_without_a_region_gets_the_one_its_translation_is_for()
        {
            // The neutral culture's formats are whichever region's its data has as the default, and they differ between
            // Windows and Linux: Windows wrote Hong Kong's 24/10/2026 for zh-Hant. The region is chosen, not left to it.
            Assert.Equal("zh-TW", FormatCulture.For("zh-Hant").Name);
            Assert.Equal("zh-CN", FormatCulture.For("zh-Hans").Name);
            Assert.Equal("pt-BR", FormatCulture.For("pt").Name);
            Assert.False(FormatCulture.For("de").IsNeutralCulture);
        }

        [Fact]
        public void A_language_with_its_region_or_a_plain_one_keeps_its_own()
        {
            Assert.Equal("10/24/2026", Day.ToString("d", FormatCulture.For("en-US")));
            Assert.Equal("24.10.2026", Day.ToString("d", FormatCulture.For("de")));
            Assert.Equal("24/10/2026", Day.ToString("d", FormatCulture.For("pt")));
            Assert.Equal("2026/10/24", Day.ToString("d", FormatCulture.For("ja")));
        }
    }
}
