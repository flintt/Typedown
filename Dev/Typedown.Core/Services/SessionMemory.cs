using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Typedown.Core.Utilities;

namespace Typedown.Core.Services
{
    /// <summary>
    /// The set of documents open when the last window closed, so the next start can bring them all back as tabs
    /// (startup action "restore last session"). Cursor positions come from <see cref="CursorMemory"/>, the window
    /// placement from the window itself, so only the file list and the active index live here.
    /// </summary>
    public static class SessionMemory
    {
        public class Session
        {
            public List<string> Files { get; set; } = new();
            public int ActiveIndex { get; set; }

            /// <summary>
            /// The active document when it had no file (an untitled one, kept by "Keep unsaved changes when closing"): its
            /// document id, which its backup and the tab it comes back in keep. Null when the active one is a file.
            /// </summary>
            public string ActiveDocumentId { get; set; }

            /// <summary>
            /// The tabs in their order, files by path and untitled documents as "untitled:" and their document id: the
            /// untitled ones come back from their backups after the files, and are put back where they were.
            /// </summary>
            public List<string> Order { get; set; }

            public DateTime Time { get; set; }
        }

        /// <summary>For the tests: where session.json is (null: the app's local folder), and how a file is looked for.</summary>
        internal static string StoreFolder { get; set; }
        internal static Func<string, bool> FileExists { get; set; } = File.Exists;

        private static string StorePath => Path.Combine(StoreFolder ?? Config.GetLocalFolderPath(), "session.json");

        public static Session Load()
        {
            try
            {
                if (!File.Exists(StorePath)) return null;
                var session = JsonConvert.DeserializeObject<Session>(File.ReadAllText(StorePath));
                if (session?.Files == null) return null;
                // Files that vanished meanwhile are dropped; the active index follows the surviving list.
                var active = session.ActiveIndex >= 0 && session.ActiveIndex < session.Files.Count ? session.Files[session.ActiveIndex] : null;
                // A vanished file's unsaved text (its backup) comes back as an untitled document: dropped with the file, it
                // stayed on disk with no restore offering it (the audit's A06).
                var backups = new AutoBackup();
                var found = Look(session.Files);
                foreach (var gone in session.Files.Where(f => found[f] == false))
                    if (backups.AdoptAsUntitled(gone)) Log.Debug($"session: {Path.GetFileName(gone)} is gone; its unsaved text kept as an untitled document");
                session.Files = session.Files.Where(f => found[f] != false).ToList();
                session.ActiveIndex = Math.Max(0, session.Files.IndexOf(active));
                // Only untitled documents open: still a session, for the one to show among them.
                return session.Files.Count > 0 || session.ActiveDocumentId != null ? session : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>How long the start waits for the files to be looked for, all together.</summary>
        internal static TimeSpan LookBudget { get; set; } = TimeSpan.FromSeconds(1.5);

        // Each file looked for once, all at once, off the start's thread, for LookBudget at most: true there, false gone,
        // null not answered in time (a network place offline) - kept, its open to tell. Each was looked for twice, one
        // after the other, and an offline share held the start for about 53 s (the audit of 2026-11-24, P10a).
        private static Dictionary<string, bool?> Look(IEnumerable<string> files)
        {
            var looks = files.Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(f => f, f => System.Threading.Tasks.Task.Run(() => FileExists(f)), StringComparer.OrdinalIgnoreCase);
            try { System.Threading.Tasks.Task.WaitAll(looks.Values.ToArray(), LookBudget); } catch (AggregateException) { }
            var found = new Dictionary<string, bool?>(StringComparer.OrdinalIgnoreCase);
            foreach (var (file, look) in looks)
            {
                found[file] = look.IsCompletedSuccessfully ? look.Result : look.IsCompleted ? false : null;
                if (found[file] == null) Log.Debug($"session: {Path.GetFileName(file)} did not answer in {LookBudget.TotalSeconds:0.#} s (a network place?); kept, its open will tell");
            }
            return found;
        }

        /// <summary>The key a tab has in <see cref="Session.Order"/>: its path, or "untitled:" and its document id.</summary>
        public static string OrderKey(string path, string documentId) => string.IsNullOrEmpty(path) ? "untitled:" + documentId : path;

        public static void Save(IEnumerable<string> files, int activeIndex, string activeDocumentId = null, List<string> order = null)
        {
            try
            {
                var list = files.Where(f => !string.IsNullOrEmpty(f)).ToList();
                Directory.CreateDirectory(Path.GetDirectoryName(StorePath));
                Typedown.Core.Utilities.SafeFile.WriteAllTextAtomic(StorePath, JsonConvert.SerializeObject(new Session { Files = list, ActiveIndex = Math.Max(0, activeIndex), ActiveDocumentId = activeDocumentId, Order = order, Time = DateTime.UtcNow }));
            }
            catch
            {
            }
        }
    }
}
