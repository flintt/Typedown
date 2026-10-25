#nullable enable
using System;
using System.Globalization;

namespace Typedown.Core.Utilities
{
    /// <summary>
    /// When a recent file was last changed, as the home page writes it: the time today, the weekday and time within the
    /// week, the date before that. Plain .NET, so it is tested on Linux (Dev/Typedown.Automation.Tests/RecentTimeTests.cs).
    /// </summary>
    public static class RecentTime
    {
        public static string Format(DateTime time, DateTime now, CultureInfo culture)
        {
            if (time.Date == now.Date) return time.ToString("t", culture);
            // The weekday, then the short time: "t" inside a custom format is the first letter of AM/PM, not the time
            // ("周二 下", "Tue P").
            if ((now.Date - time.Date).TotalDays < 7) return time.ToString("ddd", culture) + " " + time.ToString("t", culture);
            return time.ToString("d", culture);
        }
    }
}
