using System;
using System.Collections.Generic;

namespace Typedown.Core.Utilities
{
    /// <summary>
    /// The start-up's stages in debug.log ("startup: window shown at 812 ms (+420)"), each once, timed from the process's
    /// start: a report of a slow start says where the time went (todo #16). Only the first window's start counts; once
    /// its document is shown nothing more is written.
    /// </summary>
    public partial class StartupTiming
    {
        private readonly DateTime? processStart;

        private readonly Action<string> write;

        private readonly HashSet<string> marked = new();

        private double previous;

        private bool over;

        public StartupTiming(DateTime? processStart, Action<string> write)
        {
            this.processStart = processStart;
            this.write = write;
        }

        public void Mark(string stage, DateTime now, bool last = false)
        {
            lock (marked)
            {
                if (over || processStart == null || !marked.Add(stage)) return;
                var at = (now - processStart.Value).TotalMilliseconds;
                write($"startup: {stage} at {at:0} ms (+{at - previous:0})");
                previous = at;
                over = last;
            }
        }
    }
}
