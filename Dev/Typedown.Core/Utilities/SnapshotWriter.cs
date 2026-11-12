using System;
using System.IO;
using System.Threading.Tasks;

namespace Typedown.Core.Utilities
{
    /// <summary>
    /// A store's file written one snapshot after another: each text taken when it is given, written after the ones before
    /// it, atomically (SafeFile), off the caller's thread; a write that fails is logged and the next goes on. The favourites
    /// and the first-edit mutes each wrote this out (the modularity audit's M09); what goes in the text, when to save and
    /// whom to tell stay the store's.
    /// </summary>
    public sealed class SnapshotWriter
    {
        private readonly string file;
        private readonly string what;
        private readonly object gate = new();
        private Task writing = Task.CompletedTask;

        /// <param name="what">The store's name, for the log.</param>
        public SnapshotWriter(string file, string what)
        {
            this.file = file;
            this.what = what;
        }

        public void Write(string text)
        {
            lock (gate)
                writing = writing.ContinueWith(async _ =>
                {
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(file));
                        await SafeFile.WriteAllTextAtomicAsync(file, text);
                    }
                    catch (Exception ex)
                    {
                        Log.Debug($"{what}: could not write {file}: {ex.Message}");
                    }
                }, TaskScheduler.Default).Unwrap();
        }

        /// <summary>Done when every text given so far is on disk.</summary>
        public Task FlushAsync()
        {
            lock (gate) return writing;
        }
    }
}
