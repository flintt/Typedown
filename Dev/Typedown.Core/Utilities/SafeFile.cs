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
        /// The same, waited for: for a store that saves as the window closes (cursor places, the session, shared notes),
        /// which wrote over its file directly and could leave half of it after a crash (the modularity audit's M09). Safe
        /// on the window's thread: the write never comes back to the caller's thread (ConfigureAwait(false) throughout).
        /// </summary>
        public static void WriteAllTextAtomic(string path, string text) =>
            WriteAllTextAtomicAsync(path, text).GetAwaiter().GetResult();

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
            RemoveStaleTemps(directory, path);
            var keepTemp = false;
            // The rename below makes the temp file the target, attributes and all: remember the target's own.
            FileAttributes? previous = null;
            try { if (File.Exists(path)) previous = File.GetAttributes(path); } catch { }
            try
            {
                using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
                {
                    // Not back on the caller's thread: the typography designer, version history and writing statistics
                    // wait for this on the window's thread (GetResult), and a write that completed later needed that
                    // thread to go on - the window hung for good (the user's x280, typing a preset's name).
                    await stream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
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
                catch (PlatformNotSupportedException)
                {
                    // The file system has no atomic rename at all (some network/virtual file systems); the copy
                    // is the only way to place the content. This is the one path where a non-atomic overwrite is
                    // unavoidable - so the file as it was is copied aside first, and kept with the new content's temp
                    // file when the copy over it fails part way (R05: a cut copy left neither version whole).
                    string before = null;
                    if (File.Exists(path))
                    {
                        before = Path.Combine(string.IsNullOrEmpty(directory) ? "." : directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.before.tmp");
                        File.Copy(path, before);
                    }
                    try
                    {
                        File.Copy(tempPath, path, overwrite: true);
                    }
                    catch
                    {
                        keepTemp = true;
                        throw;
                    }
                    if (before != null) try { File.Delete(before); } catch { }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    // A transient lock (an antivirus scan, a sync client briefly holding the file) is the common
                    // recoverable cause; retry the atomic replace a few times.
                    for (var attempt = 0; attempt < 4; attempt++)
                    {
                        await Task.Delay(75).ConfigureAwait(false);
                        try
                        {
                            if (!MoveReplace(tempPath, path))
                            {
                                if (File.Exists(path))
                                    File.Replace(tempPath, path, null, ignoreMetadataErrors: true);
                                else
                                    File.Move(tempPath, path);
                            }
                            RestoreAttributes(path, previous);
                            return;
                        }
                        catch (IOException) { }
                        catch (UnauthorizedAccessException) { }
                    }
                    // Still locked after the retries. Do NOT overwrite in place with a non-atomic copy that could
                    // leave a partial file — keep the original intact and the finished temp file as a recovery
                    // copy, and report the failure to the caller so it can prompt or retry.
                    keepTemp = true;
                    throw;
                }
                RestoreAttributes(path, previous);
            }
            finally
            {
                // Delete the temp file unless it is being kept as the recovery copy for a failed atomic replace.
                if (!keepTemp)
                {
                    try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                }
            }
        }

        /// <summary>
        /// This file's own temp files left by a write that never finished (the app ended, the power went) or kept as the
        /// recovery copy of a failed replace, once they are a week old: they were never cleaned up (R05). Newer ones stay
        /// - a failed save's copy is there to be found.
        /// </summary>
        private static void RemoveStaleTemps(string directory, string path)
        {
            try
            {
                var folder = string.IsNullOrEmpty(directory) ? "." : directory;
                foreach (var stale in Directory.EnumerateFiles(folder, $".{Path.GetFileName(path)}.*.tmp"))
                    if (DateTime.UtcNow - File.GetLastWriteTimeUtc(stale) > TimeSpan.FromDays(7))
                        try { File.Delete(stale); } catch { }
            }
            catch { }
        }

        /// <summary>
        /// The saved file gets back the attributes it had, not the temp file's hidden and temporary marks - which the
        /// rename carried over in 1.2.27 to 1.2.30, leaving every file Typedown saved hidden (sync clients skip such
        /// files). A file that carries both marks was left so by those versions and is put right on its next save.
        /// </summary>
        private static void RestoreAttributes(string path, FileAttributes? previous)
        {
            try
            {
                var attributes = previous ?? File.GetAttributes(path);
                if ((attributes & FileAttributes.Temporary) != 0) attributes &= ~(FileAttributes.Hidden | FileAttributes.Temporary);
                File.SetAttributes(path, attributes == 0 ? FileAttributes.Normal : attributes);
            }
            catch { }
        }
    }
}
