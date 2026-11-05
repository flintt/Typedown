using System;
using Microsoft.UI.Xaml;

namespace Typedown.Core.Models
{
    // The commit timer on the window's thread: typing waits in pending this long after its last change, then is a step.
    // Apart from the history's logic (ContentHistory.cs), which the reliability tests compile with a timer of their own.
    public partial class ContentHistory
    {
        private DispatcherTimer commitTimer;

        partial void InitTimer()
        {
            commitTimer = new();
            commitTimer.Tick += (s, e) => CommitPending();
        }

        partial void StartTimer(TimeSpan interval)
        {
            commitTimer.Interval = interval;
            commitTimer.Start();
        }

        partial void StopTimer() => commitTimer.Stop();
    }
}
