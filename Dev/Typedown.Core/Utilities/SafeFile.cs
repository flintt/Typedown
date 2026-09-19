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
        public static async Task WriteAllTextAtomicAsync(string path, string text, Encoding encoding = null)
        {
            encoding ??= new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            var directory = Path.GetDirectoryName(path);
            var tempPath = Path.Combine(string.IsNullOrEmpty(directory) ? "." : directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
            try
            {
                using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
                {
                    var bytes = encoding.GetBytes(text);
                    await stream.WriteAsync(bytes, 0, bytes.Length);
                    stream.Flush(flushToDisk: true);
                }
                try
                {
                    if (File.Exists(path))
                        File.Replace(tempPath, path, null, ignoreMetadataErrors: true);
                    else
                        File.Move(tempPath, path);
                }
                catch (Exception ex) when (ex is PlatformNotSupportedException || ex is IOException || ex is UnauthorizedAccessException)
                {
                    // Replace can fail across volumes or on file systems without rename semantics; keep the data safe
                    // by copying the finished temp file over the target instead of streaming into it.
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
