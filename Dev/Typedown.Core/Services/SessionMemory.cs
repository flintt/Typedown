using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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

        private static string StorePath => Path.Combine(Config.GetLocalFolderPath(), "session.json");

        public static Session Load()
        {
            try
            {
                if (!File.Exists(StorePath)) return null;
                var session = JsonConvert.DeserializeObject<Session>(File.ReadAllText(StorePath));
                if (session?.Files == null) return null;
                // Files that vanished meanwhile are dropped; the active index follows the surviving list.
                var active = session.ActiveIndex >= 0 && session.ActiveIndex < session.Files.Count ? session.Files[session.ActiveIndex] : null;
                session.Files = session.Files.Where(File.Exists).ToList();
                session.ActiveIndex = Math.Max(0, session.Files.IndexOf(active));
                // Only untitled documents open: still a session, for the one to show among them.
                return session.Files.Count > 0 || session.ActiveDocumentId != null ? session : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>The key a tab has in <see cref="Session.Order"/>: its path, or "untitled:" and its document id.</summary>
        public static string OrderKey(string path, string documentId) => string.IsNullOrEmpty(path) ? "untitled:" + documentId : path;

        public static void Save(IEnumerable<string> files, int activeIndex, string activeDocumentId = null, List<string> order = null)
        {
            try
            {
                var list = files.Where(f => !string.IsNullOrEmpty(f)).ToList();
                Directory.CreateDirectory(Path.GetDirectoryName(StorePath));
                File.WriteAllText(StorePath, JsonConvert.SerializeObject(new Session { Files = list, ActiveIndex = Math.Max(0, activeIndex), ActiveDocumentId = activeDocumentId, Order = order, Time = DateTime.UtcNow }));
            }
            catch
            {
            }
        }
    }
}
