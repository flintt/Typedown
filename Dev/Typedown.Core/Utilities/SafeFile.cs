using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Typedown.Core.Utilities
{
    public static class SafeFile
    {
        /// <summary>
        /// Writes <paramref name="text"/> to <paramref name="path"/> without ever leaving a truncated file behind:
        /// the content goes to a temporary file in the same directory, is flushed to disk, and then replaces the
        /// target in one step. A crash or power loss mid-write leaves the previous version intact (upstream #56).
        /// Falls back to a direct write where the replace step is not supported (some network/virtual file systems).
        /// </summary>
        public static Task WriteAllTextAtomicAsync(string path, string text, Encoding encoding = null) =>
            WriteAllBytesAtomicAsync(path, (encoding ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)).GetBytes(text));

        /// <summary>
        /// The same atomic write for content whose bytes the caller has already produced — a document keeps the
        /// encoding, byte order mark and line ending it was opened with (see <see cref="TextFileFormat"/>).
        /// </summary>
        // MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH: an atomic same-volume rename that overwrites the
        // target and leaves no backup file beside it. Returns false when it is not applicable (not Windows, or
        // a cross-volume move the OS refuses), so the caller falls back to the managed path.
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern bool MoveFileEx(string existing, string newName, uint flags);

        private static bool MoveReplace(string source, string destination)
        {
            try { return MoveFileEx(source, destination, 0x1 | 0x8); }
            catch { return false; }
        }

        public static async Task WriteAllBytesAtomicAsync(string path, byte[] bytes)
        {
            var directory = Path.GetDirectoryName(path);
            // The temp file has to sit in the same directory as the target, so the replace below is a rename
            // on the same volume and therefore atomic. It exists only for the moment between being written and
            // becoming the target. It is a dot-file and marked hidden and temporary so a sync client (Synology
            // Drive, OneDrive) skips it rather than uploading a file that is about to vanish.
            var tempPath = Path.Combine(string.IsNullOrEmpty(directory) ? "." : directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
            try
            {
                using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
                {
                    await stream.WriteAsync(bytes, 0, bytes.Length);
                    stream.Flush(flushToDisk: true);
                }
                try { File.SetAttributes(tempPath, FileAttributes.Hidden | FileAttributes.Temporary); } catch { }
                try
                {
                    // Replace atomically, and without the ~RF….TMP backup file File.Replace leaves on Windows —
                    // which a sync drive would upload and could leave behind after a crash. MoveFileEx with
                    // REPLACE_EXISTING is the same-volume atomic rename with no backup; the managed fallbacks
                    // cover other file systems.
                    if (!MoveReplace(tempPath, path))
                    {
                        if (File.Exists(path))
                            File.Replace(tempPath, path, null, ignoreMetadataErrors: true);
                        else
                            File.Move(tempPath, path);
                    }
                }
                catch (Exception ex) when (ex is PlatformNotSupportedException || ex is IOException || ex is UnauthorizedAccessException)
                {
                    // Replace can fail across volumes or on file systems without rename semantics; keep the data
                    // safe by copying the finished temp file over the target instead of streaming into it.
                    File.Copy(tempPath, path, overwrite: true);
                }
            }
            finally
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            }
        }
    }
}
