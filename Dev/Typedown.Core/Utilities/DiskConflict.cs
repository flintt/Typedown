using System;
using System.IO;

namespace Typedown.Core.Utilities
{
    /// <summary>
    /// A file changed on disk while the document was open: neither version is lost. What a save finds on disk is weighed
    /// against what the app last knew was there, and the version not kept goes to a copy of its own (AutoBackup.KeepCopy).
    /// </summary>
    public static class DiskConflict
    {
        /// <summary>
        /// True when the file holds text the app has not seen - another program wrote it since it was last read or written
        /// (a change inside the second after a save of our own was never looked at, R06) - so a save would put the document
        /// over it unasked. Hashes of the text as read (line endings made "\n"); <paramref name="known"/> 0 is not known
        /// (a new document): nothing to weigh. The disk already holding what the editor loaded or what is to be written
        /// is no conflict: nothing would be lost.
        /// </summary>
        public static bool ChangedSinceKnown(ulong onDisk, ulong known, ulong loaded, ulong toWrite) =>
            known != 0 && onDisk != known && onDisk != loaded && onDisk != toWrite;

        /// <summary>
        /// A kept copy's file name: the document's name, when, and whose version ("on disk", "unsaved"), its extension
        /// kept so it opens as it did - "notes 2026-11-04 213015 on disk.md".
        /// </summary>
        public static string KeptCopyName(string sourcePath, DateTime time, string label)
        {
            var name = Path.GetFileNameWithoutExtension(sourcePath ?? "");
            var extension = Path.GetExtension(sourcePath ?? "");
            if (string.IsNullOrWhiteSpace(name)) name = "untitled";
            if (string.IsNullOrEmpty(extension)) extension = ".md";
            return $"{name} {time:yyyy-MM-dd HHmmss} {label}{extension}";
        }
    }
}
