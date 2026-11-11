using System.IO;
using System.Threading.Tasks;
using Typedown.Core.Utilities;

namespace Typedown.Core.Services
{
    public class AutoBackup
    {
        private readonly string backupPath;

        public AutoBackup(string backupPath = null)
        {
            this.backupPath = backupPath ?? Path.Combine(Config.GetLocalFolderPath(), "Backup");
        }

        private const string UntitledPrefix = "untitled_";

        /// <summary>
        /// A document's backup file. A titled document's is keyed by its path (recovery matches it on reopen). An untitled
        /// one's is keyed by its document id when that is given: all untitled documents used to share the empty path's
        /// file, so with several open only the last one written survived a crash, and background ones were never kept.
        /// </summary>
        public string GetBackupFilePath(string sourcePath, string documentId = null)
        {
            if (!Directory.Exists(backupPath))
                Directory.CreateDirectory(backupPath);
            if (string.IsNullOrEmpty(sourcePath) && !string.IsNullOrEmpty(documentId))
                return Path.Combine(backupPath, UntitledPrefix + documentId + ".md");
            sourcePath ??= "";
            var pathHash = Common.SimpleHash2(sourcePath);
            var pathFilename = Path.GetFileName(sourcePath);
            return Path.Combine(backupPath, $"{pathHash}_{pathFilename}");
        }

        public async Task<bool> Backup(string path, string markdown, string documentId = null)
        {
            try
            {
                // The backup is the last line of defence; a crash while it is being written must not leave half of it.
                await Utilities.SafeFile.WriteAllTextAtomicAsync(GetBackupFilePath(path, documentId), markdown);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<string> GetBackup(string path, string documentId = null)
        {
            try
            {
                // No backup is the usual case (the document was saved): asked first rather than read and failed, which
                // logged a FileNotFoundException for every tab restored at startup.
                var file = GetBackupFilePath(path, documentId);
                return File.Exists(file) ? await File.ReadAllTextAsync(file) : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// A file's backup taken over as an untitled document's (a new id, the name <see cref="UntitledBackups"/> reads):
        /// for a file of the last session that is gone, whose unsaved text no restore offered any more (the audit's A06).
        /// The text comes back as an untitled tab. False when there is none, or it could not be moved.
        /// </summary>
        public bool AdoptAsUntitled(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return false;
                var named = GetBackupFilePath(path);
                if (!File.Exists(named)) return false;
                File.Move(named, GetBackupFilePath(null, System.Guid.NewGuid().ToString("N")));
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Untitled documents' backups left behind (a crash), as document id and file.</summary>
        public System.Collections.Generic.List<(string documentId, string file)> UntitledBackups()
        {
            var list = new System.Collections.Generic.List<(string, string)>();
            try
            {
                if (!Directory.Exists(backupPath)) return list;
                foreach (var file in Directory.GetFiles(backupPath, UntitledPrefix + "*.md"))
                {
                    var name = Path.GetFileNameWithoutExtension(file).Substring(UntitledPrefix.Length);
                    if (name.Length == 32 && System.Linq.Enumerable.All(name, c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
                        list.Add((name, file));
                }
                list.Sort((a, b) => File.GetLastWriteTimeUtc(a.Item2).CompareTo(File.GetLastWriteTimeUtc(b.Item2)));
            }
            catch
            {
                // Ignore: no recovery is better than a failed start.
            }
            return list;
        }

        /// <summary>
        /// Where the versions a conflict with the disk did not keep are put (KeepCopy): kept, not cleared by a save as a
        /// document's backup is.
        /// </summary>
        public string KeptFolder => Path.Combine(backupPath, "Kept");

        // Kept copies past this many go, the oldest first: the folder is not left to grow without end.
        private const int KeptLimit = 200;

        /// <summary>
        /// A copy of one version of a document, kept for good (a file changed on disk while it had unsaved changes: the
        /// version the reader did not keep - the disk's under "Keep mine", theirs under "Reload"). The copy's path, or null
        /// when it could not be written.
        /// </summary>
        public async Task<string> KeepCopy(string sourcePath, string text, string label)
        {
            try
            {
                Directory.CreateDirectory(KeptFolder);
                var file = Path.Combine(KeptFolder, DiskConflict.KeptCopyName(sourcePath, System.DateTime.Now, label));
                for (var i = 2; File.Exists(file); i++)
                    file = Path.Combine(KeptFolder, Path.GetFileNameWithoutExtension(DiskConflict.KeptCopyName(sourcePath, System.DateTime.Now, label)) + $" ({i})" + Path.GetExtension(file));
                await Utilities.SafeFile.WriteAllTextAtomicAsync(file, text ?? "");
                var kept = new DirectoryInfo(KeptFolder).GetFiles();
                if (kept.Length > KeptLimit)
                    foreach (var old in System.Linq.Enumerable.Take(System.Linq.Enumerable.OrderBy(kept, f => f.LastWriteTimeUtc), kept.Length - KeptLimit))
                        old.Delete();
                return file;
            }
            catch
            {
                return null;
            }
        }

        public void DeleteBackup(string path, string documentId = null)
        {
            try
            {
                File.Delete(GetBackupFilePath(path, documentId));
            }
            catch
            {
                // Ignore
            }
        }
    }
}
