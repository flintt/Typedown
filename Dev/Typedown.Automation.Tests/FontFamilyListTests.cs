using Typedown.Core.Utilities;
using Xunit;

namespace Typedown.Automation.Tests
{
    /// <summary>Typedown.Core FontFamilyList: the font picker puts the font chosen first and keeps the fallbacks after it.</summary>
    public sealed class FontFamilyListTests
    {
        [Fact]
        public void A_value_splits_into_its_fonts_quotes_and_commas_inside_quotes_kept_as_names()
        {
            Assert.Equal(new[] { "Georgia", "Microsoft YaHei UI", "sans-serif" }, FontFamilyList.Split("Georgia, 'Microsoft YaHei UI', sans-serif"));
            Assert.Equal(new[] { "A, B", "C" }, FontFamilyList.Split("\"A, B\",C"));
            Assert.Empty(FontFamilyList.Split(""));
            Assert.Empty(FontFamilyList.Split(null!));
        }

        [Fact]
        public void The_chosen_font_takes_the_first_place_and_the_fallbacks_stay()
        {
            Assert.Equal("'Palatino Linotype', 'Microsoft YaHei UI', sans-serif",
                FontFamilyList.WithFirst("Georgia, 'Microsoft YaHei UI', sans-serif", "Palatino Linotype"));
            Assert.Equal("Cambria", FontFamilyList.WithFirst("", "Cambria"));
            Assert.Equal("'Source Han Serif SC'", FontFamilyList.WithFirst("Georgia", "Source Han Serif SC"));
        }

        [Fact]
        public void A_font_already_further_down_the_list_is_not_there_twice()
        {
            Assert.Equal("'Microsoft YaHei UI', serif", FontFamilyList.WithFirst("Georgia, 'Microsoft YaHei UI', serif", "Microsoft YaHei UI"));
        }

        [Fact]
        public void Generic_families_and_plain_names_are_not_quoted()
        {
            Assert.Equal("serif", FontFamilyList.Quote("serif"));
            Assert.Equal("Consolas", FontFamilyList.Quote("Consolas"));
            Assert.Equal("'Courier New'", FontFamilyList.Quote("Courier New"));
            Assert.Equal("微软雅黑", FontFamilyList.Quote("微软雅黑"));
        }
    }
}
