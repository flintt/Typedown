using System;
using System.Threading;
using System.Threading.Tasks;

namespace Typedown.Core.Services
{
    /// <summary>
    /// The steps of a save as, in their order: the save lock waited for, the file picked (or given), a file open
    /// elsewhere refused, the page's text taken, the file written, the document's state made the file's. After each wait
    /// the document the command was given for is asked again (<see cref="Still"/>, taken before the command's first wait):
    /// gone before the write, nothing is written; gone after it, the file written is kept and the document shown now -
    /// another one - is not given its path, saved mark or backup. An untitled A saved as while untitled B came into the
    /// same tab (a wait for the lock or the picker) wrote B's text into A's file and gave B the path: A's identity was
    /// taken only after those waits (the audit of 2026-11-24, R01). Plain .NET, so the reliability tests drive each wait.
    /// </summary>
    public sealed class SaveAsRun
    {
        /// <summary>The document the command was given for is still this window's, shown.</summary>
        public Func<bool> Still { get; init; }

        /// <summary>Taken for the run and released after it; null when the caller holds it already.</summary>
        public SemaphoreSlim Lock { get; init; }

        /// <summary>The file to write; null: <see cref="Pick"/> asks for it.</summary>
        public string Target { get; init; }

        /// <summary>The file picked, or null when the picker was cancelled.</summary>
        public Func<Task<string>> Pick { get; init; }

        /// <summary>True when the file may not be written (open in another tab or window); the refusal said.</summary>
        public Func<string, Task<bool>> Refused { get; init; }

        /// <summary>The page's text taken (FileViewModel.FlushForSaveAsync), given what tells it the document went.</summary>
        public Func<Func<bool>, string, Task<bool>> Flush { get; init; }

        /// <summary>The text to write, once taken.</summary>
        public Func<string> Text { get; init; }

        /// <summary>Writes the file; true when it was written.</summary>
        public Func<string, string, Task<bool>> Write { get; init; }

        /// <summary>The file written made the document's (its path, hash, saved mark, backup); the path saved, or null.</summary>
        public Func<string, string, string> Commit { get; init; }

        /// <summary>Where the run stopped, for the log.</summary>
        public Action<string> Say { get; init; }

        public async Task<string> RunAsync()
        {
            if (Lock != null) await Lock.WaitAsync();
            try
            {
                if (!Still()) return Stop("the document went while the save waited for the one before it");
                var target = Target;
                if (target == null)
                {
                    target = await Pick();
                    if (target == null) return null;
                    if (!Still()) return Stop("the document went while the file was picked");
                }
                if (await Refused(target)) return null;
                if (!Still()) return Stop("the document went while the file was looked at");
                var flushed = await Flush(() => !Still(), target);
                if (!Still()) return Stop("the document went while its text was taken");
                if (!flushed) return null;
                var text = Text();
                if (!await Write(target, text)) return null;
                if (!Still()) return Stop("the file was written; the document went meanwhile and is not given its path");
                return Commit(target, text);
            }
            finally
            {
                Lock?.Release();
            }
        }

        private string Stop(string why)
        {
            Say?.Invoke("save as: " + why);
            return null;
        }
    }
}
