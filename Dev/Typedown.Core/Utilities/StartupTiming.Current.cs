using System;

namespace Typedown.Core.Utilities
{
    public partial class StartupTiming
    {
        /// <summary>This process's start, written to debug.log.</summary>
        public static StartupTiming Current { get; } = new(ProcessStart(), Log.Debug);

        /// <summary>A stage of the start reached now.</summary>
        public static void Reached(string stage, bool last = false) => Current.Mark(stage, DateTime.Now, last);

        private static DateTime? ProcessStart()
        {
            try { return System.Diagnostics.Process.GetCurrentProcess().StartTime; }
            catch { return null; }
        }
    }
}
