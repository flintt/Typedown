using System;
using System.Globalization;
using Typedown.Core.Utilities;
using Xunit;

namespace Typedown.Automation.Tests
{
    /// <summary>Typedown.Core RecentTime: the home page's "last changed" in the interface's language.</summary>
    public class RecentTimeTests
    {
        private static readonly DateTime Now = new(2026, 10, 24, 10, 0, 0);

        // ICU puts a narrow no-break space before AM/PM; Windows' NLS a plain one.
        private static string Plain(string s) => s.Replace('\u202f', ' ').Replace('\u00a0', ' ');

        [Fact]
        public void Within_the_week_it_is_the_weekday_and_the_whole_time()
        {
            var tuesday = new DateTime(2026, 10, 20, 15, 5, 0);
            // Not "Tue P" / "周二 下": the time is there, not the first letter of PM.
            Assert.Equal("Tue 3:05 PM", Plain(RecentTime.Format(tuesday, Now, CultureInfo.GetCultureInfo("en-US"))));
            var zh = RecentTime.Format(tuesday, Now, CultureInfo.GetCultureInfo("zh-Hans"));
            Assert.StartsWith("周二 ", zh);
            Assert.Contains("15:05", zh);
        }

        [Fact]
        public void Today_it_is_the_time_and_before_the_week_the_date()
        {
            Assert.Equal("9:30 AM", Plain(RecentTime.Format(new DateTime(2026, 10, 24, 9, 30, 0), Now, CultureInfo.GetCultureInfo("en-US"))));
            Assert.Equal("10/1/2026", RecentTime.Format(new DateTime(2026, 10, 1, 9, 30, 0), Now, CultureInfo.GetCultureInfo("en-US")));
            Assert.Equal("01.10.2026", RecentTime.Format(new DateTime(2026, 10, 1, 9, 30, 0), Now, CultureInfo.GetCultureInfo("de")));
        }
    }
}
