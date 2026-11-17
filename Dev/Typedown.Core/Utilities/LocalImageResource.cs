using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace Typedown.Core.Utilities
{
    /// <summary>A local image may be drawn, never fetched as text, executed, or opened as an editor document.</summary>
    public static class LocalImageResource
    {
        public const string Host = "editor.invalid";
        public const string EditorAddress = "https://" + Host + "/index.html";
        public const string ImagePath = "/__local_image";
        public const string ImageEndpoint = "https://local-images.invalid" + ImagePath;
        public const int MaxBytes = 32 * 1024 * 1024;

        public static async Task<byte[]> ReadAsync(string path, CancellationToken cancellationToken)
        {
            // Opening a UNC file can block. Keep it off the dispatcher; the caller bounds concurrent operations.
            return await Task.Run(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                    81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
                cancellationToken.ThrowIfCancellationRequested();
                if (input.Length > MaxBytes) return null;
                using var output = new MemoryStream();
                var buffer = new byte[81920];
                int count;
                while ((count = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) != 0)
                {
                    if (output.Length + count > MaxBytes) return null;
                    output.Write(buffer, 0, count);
                }
                return output.ToArray();
            }, cancellationToken);
        }

        public static bool TryResolve(string address, string endpoint, string method, bool imageRequest,
            out string path, out string contentType)
        {
            path = null;
            contentType = null;
            if (!imageRequest || method != "GET" ||
                !Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Fragment.Length != 0 ||
                !WebViewPagePolicy.Matches(uri.GetLeftPart(UriPartial.Path), endpoint)) return false;

            // ParseQueryString performs the transport decoding once. Do not URL-decode its result again: a file
            // literally named "%23.png" must not turn into "#.png" (nor an encoded separator into a device path).
            var sources = HttpUtility.ParseQueryString(uri.Query).GetValues("src");
            if (sources == null || sources.Length != 1 || string.IsNullOrWhiteSpace(sources[0])) return false;
            var local = sources[0];
            if (local.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                if (!Uri.TryCreate(local, UriKind.Absolute, out var file) || !file.IsFile || file.UserInfo.Length != 0)
                    return false;
                local = file.LocalPath;
            }

            try
            {
                var windows = local.Replace('/', '\\');
                if (!Path.IsPathFullyQualified(local) || windows.StartsWith(@"\\?\", StringComparison.Ordinal) ||
                    windows.StartsWith(@"\\.\", StringComparison.Ordinal) || windows.StartsWith(@"\??\", StringComparison.Ordinal) ||
                    windows.IndexOf(':', windows.Length > 1 && windows[1] == ':' ? 2 : 0) >= 0) return false;
                var full = Path.GetFullPath(local);
                contentType = Path.GetExtension(full).ToLowerInvariant() switch
                {
                    ".png" => "image/png",
                    ".jpg" or ".jpeg" or ".jfif" => "image/jpeg",
                    ".gif" => "image/gif",
                    ".svg" => "image/svg+xml",
                    ".webp" => "image/webp",
                    ".avif" => "image/avif",
                    ".bmp" => "image/bmp",
                    ".ico" => "image/x-icon",
                    ".tif" or ".tiff" => "image/tiff",
                    _ => null
                };
                if (contentType == null) return false;
                path = full;
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return false;
            }
        }
    }
}
