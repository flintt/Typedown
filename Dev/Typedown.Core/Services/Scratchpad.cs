using System;
using System.IO;

namespace Typedown.Core.Services
{
    /// <summary>
    /// The scratchpad (the user's 33, 2026-11-22): a small window of its own with one text box, the same for every window
    /// and tab. Its text is <see cref="Shared"/>; the window is the app's (Typedown's ScratchpadWindow), reached through
    /// <see cref="Toggle"/> and <see cref="Show"/>, which it sets.
    /// </summary>
    public static class Scratchpad
    {
        private static ScratchpadStore shared;

        private static readonly object gate = new();

        public static ScratchpadStore Shared
        {
            get
            {
                lock (gate)
                    return shared ??= new ScratchpadStore(Path.Combine(Config.GetLocalFolderPath(), "scratchpad.json"));
            }
        }

        /// <summary>Shown if hidden, hidden if shown (View > Scratchpad, its shortcut).</summary>
        public static Action Toggle { get; set; } = () => { };

        /// <summary>Shown, and brought to the front (what was put in it is seen).</summary>
        public static Action Show { get; set; } = () => { };
    }
}
