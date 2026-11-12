using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Typedown.Core.Controls;
using Typedown.Core.Interfaces;
using Typedown.Core.Utilities;
using Typedown.Core.ViewModels;

namespace Typedown.Core.Services
{
    public class ImageAction
    {
        public SettingsViewModel Settings { get; }

        public AppViewModel AppViewModel => ServiceProvider.GetService<AppViewModel>();

        public FileViewModel FileViewModel => ServiceProvider.GetService<FileViewModel>();

        private IServiceProvider ServiceProvider { get; }

        public ImageAction(SettingsViewModel settingsViewModel, IServiceProvider serviceProvider)
        {
            Settings = settingsViewModel;
            ServiceProvider = serviceProvider;
        }

        public async Task<string> DoLocalFileAction(string src)
        {
            try
            {
                var result = src;
                if (!UriHelper.TryGetLocalPath(src, out var filePath))
                    return result;
                switch (Settings.InsertLocalImageAction)
                {
                    case Enums.InsertImageAction.CopyToPath:
                        result = await Task.Run(() => CopyImage(InsertImageSource.Local, AppViewModel.GetImageAbsolutePath(filePath)));
                        break;
                    case Enums.InsertImageAction.Upload:
                        result = await Upload(InsertImageSource.Local, AppViewModel.GetImageAbsolutePath(filePath));
                        break;
                    case Enums.InsertImageAction.Base64:
                        var bytes = await File.ReadAllBytesAsync(AppViewModel.GetImageAbsolutePath(filePath));
                        if (Embed(bytes) is { } embedded) return embedded;
                        await SayTooLargeToEmbed(bytes.Length);
                        result = ConvertImagePath(filePath);
                        break;
                    default:
                        result = ConvertImagePath(filePath);
                        break;
                }
                if (UriHelper.IsAbsolutePath(result))
                    return new Uri(result).AbsoluteUri;
                return result;
            }
            catch (Exception ex)
            {
                await AppContentDialog.Create(Locale.GetString("Error"), ex.Message, Locale.GetString("Ok")).ShowAsync(AppViewModel.XamlRoot);
                return src;
            }
        }

        /// <summary>
        /// A picture on the web as Settings > Image > Insert web image says (kept, copied to a folder or uploaded): its
        /// new address. Throws what went wrong; <see cref="DoWebFileAction"/> says it in a dialog.
        /// </summary>
        public async Task<string> ProcessWebImageAsync(string src, Uri page = null)
        {
            var result = src;
            switch (Settings.InsertWebImageAction)
            {
                case Enums.InsertImageAction.CopyToPath:
                    result = await SaveImage(InsertImageSource.Web, await GetWebImage(new(src), page));
                    break;
                case Enums.InsertImageAction.Upload:
                    result = await Upload(InsertImageSource.Web, await GetWebImage(new(src), page));
                    break;
                case Enums.InsertImageAction.Base64:
                    var bytes = await GetWebImage(new(src), page);
                    if (Embed(bytes) is { } embedded) return embedded;
                    await SayTooLargeToEmbed(bytes.Length);
                    return src;
                default:
                    return src;
            }
            if (UriHelper.IsAbsolutePath(result))
                return new Uri(result).AbsoluteUri;
            // A path beside the document, written as Markdown writes it: with / (it came back ".\\images\\x.png").
            return UriHelper.IsWebUrl(result) ? result : result.Replace('\\', '/');
        }

        public async Task<string> DoWebFileAction(string src)
        {
            try
            {
                return await ProcessWebImageAsync(src);
            }
            catch (Exception ex)
            {
                await AppContentDialog.Create(Locale.GetString("Error"), ex.Message, Locale.GetString("Ok")).ShowAsync(AppViewModel.XamlRoot);
                return src;
            }
        }

        public async Task<string> DoClipboardAction(IClipboardImage image)
        {
            try
            {
                string result;
                switch (Settings.InsertClipboardImageAction)
                {
                    case Enums.InsertImageAction.Upload:
                        // Off the UI thread: GetBytes waits on WinRT calls that complete on the thread it is called from,
                        // and called from a paste it hung the window for good.
                        result = await Upload(InsertImageSource.Clipboard, await Task.Run(image.GetBytes));
                        break;
                    case Enums.InsertImageAction.Base64:
                        var bytes = await Task.Run(() => Settings.Base64ClipboardJpeg ? image.GetJpegBytes(Settings.Base64JpegQuality) : image.GetBytes());
                        if (Embed(bytes) is { } embedded) return embedded;
                        await SayTooLargeToEmbed(bytes.Length);
                        result = await Task.Run(() => SaveImage(InsertImageSource.Clipboard, image));
                        break;
                    default:
                        result = await Task.Run(() => SaveImage(InsertImageSource.Clipboard, image));
                        break;
                }
                if (UriHelper.IsAbsolutePath(result))
                    return new Uri(result).AbsoluteUri;
                return result;
            }
            catch (Exception ex)
            {
                await AppContentDialog.Create(Locale.GetString("Error"), ex.Message, Locale.GetString("Ok")).ShowAsync(AppViewModel.XamlRoot);
                return string.Empty;
            }
        }

        /// <summary>
        /// The picture as a data: address, kept in the document itself (Settings > Image > Embed as base64), or null when it
        /// is larger than the size set for that: it is then inserted as it would be without embedding, and the reader told.
        /// </summary>
        public string Embed(byte[] bytes) =>
            bytes.Length > Math.Max(1, Settings.Base64ImageMaxKB) * 1024L ? null : ImageData.ToDataUri(bytes);

        private async Task SayTooLargeToEmbed(long bytes)
        {
            Log.Debug($"image: {bytes / 1024} KB is over the {Settings.Base64ImageMaxKB} KB set for embedding; inserted without");
            var text = string.Format(Locale.GetDialogString("Image.Base64.TooLarge"), (bytes + 1023) / 1024, Settings.Base64ImageMaxKB);
            await AppContentDialog.Create(Locale.GetString("Image.InsertActions.Base64"), text, Locale.GetString("Ok")).ShowAsync(AppViewModel.XamlRoot);
        }

        public string CopyImage(InsertImageSource source, string sourceFile, string destFolder = null)
        {
            destFolder ??= GetDefaultDestFolder(source);
            var name = UniqueName(destFolder, Path.GetFileName(sourceFile), existing => Common.FileContentEqual(existing, sourceFile));
            var destFilePath = AppViewModel.GetImageAbsolutePath(Path.Combine(destFolder, name));
            if (!File.Exists(destFilePath))
            {
                new FileInfo(destFilePath).Directory?.Create();
                File.Copy(sourceFile, destFilePath);
            }
            return Path.Combine(destFolder, name);
        }

        public async Task<string> SaveImage(InsertImageSource source, byte[] bytes, string fileName = null, string destFolder = null)
        {
            fileName ??= $"{Guid.NewGuid()}.{GetImageType(bytes, "png")}";
            destFolder ??= GetDefaultDestFolder(source);
            var name = UniqueName(destFolder, fileName, existing => Common.FileContentEqual(existing, bytes));
            var destFilePath = AppViewModel.GetImageAbsolutePath(Path.Combine(destFolder, name));
            if (!File.Exists(destFilePath))
            {
                new FileInfo(destFilePath).Directory?.Create();
                await File.WriteAllBytesAsync(destFilePath, bytes);
            }
            return Path.Combine(destFolder, name);
        }

        public string SaveImage(InsertImageSource source, IClipboardImage image, string fileName = null, string destFolder = null)
        {
            fileName ??= $"{Guid.NewGuid()}.png";
            destFolder ??= GetDefaultDestFolder(source);
            var png = image.GetBytes();
            var name = UniqueName(destFolder, fileName, existing => Common.FileContentEqual(existing, png));
            var destFilePath = AppViewModel.GetImageAbsolutePath(Path.Combine(destFolder, name));
            if (!File.Exists(destFilePath))
            {
                new FileInfo(destFilePath).Directory?.Create();
                image.SaveAsPng(destFilePath);
            }
            return Path.Combine(destFolder, name);
        }

        /// <summary>
        /// The name to save a picture under in a folder: its own, or "name (2).ext", "name (3).ext"... when a different
        /// picture already has it; a file there with the same content is reused. The document was given the original
        /// name whatever the file was saved as, so a second, different "截图.png" showed the first one; and each try
        /// was numbered from the last ("a (2) (3).png").
        /// </summary>
        private string UniqueName(string destFolder, string fileName, Func<string, bool> sameContent)
        {
            var stem = Path.GetFileNameWithoutExtension(fileName);
            var ext = Path.GetExtension(fileName);
            var name = fileName;
            for (var i = 2; ; i++)
            {
                var path = AppViewModel.GetImageAbsolutePath(Path.Combine(destFolder, name));
                if (!File.Exists(path) || sameContent(path)) return name;
                name = $"{stem} ({i}){ext}";
            }
        }

        private static readonly System.Net.Http.HttpClient web = new(new System.Net.Http.HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate })
        {
            Timeout = TimeSpan.FromSeconds(30),
        };

        /// <summary>
        /// A picture on the web, fetched as a browser would, with a browser's user agent. Hotlink protection differs from
        /// site to site, so the referrer is tried in turn while the server refuses (401/403): the page the picture was
        /// copied from (<paramref name="page"/>), none, then the picture's own site - cnBeta's pictures came with none or
        /// the article, and were refused (403) with their own site as referrer, which was tried alone. Throws when the
        /// answer is not a picture: an error or sign-in page was saved as a .png before.
        /// </summary>
        public async Task<byte[]> GetWebImage(Uri uri, Uri page = null)
        {
            var referrers = new List<Uri>();
            if (page != null && (page.Scheme == Uri.UriSchemeHttp || page.Scheme == Uri.UriSchemeHttps)) referrers.Add(page);
            referrers.Add(null);
            referrers.Add(new Uri(uri.GetLeftPart(UriPartial.Authority) + "/"));
            Exception refused = null;
            foreach (var referrer in referrers)
            {
                using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, uri);
                request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36 Edg/124.0");
                request.Headers.TryAddWithoutValidation("Accept", "image/avif,image/webp,image/apng,image/svg+xml,image/*,*/*;q=0.8");
                if (referrer != null) request.Headers.Referrer = referrer;
                using var response = await web.SendAsync(request).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    refused = new InvalidDataException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
                    continue;
                }
                if (!response.IsSuccessStatusCode)
                    throw new InvalidDataException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
                var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                if (GetImageType(bytes, null) == null)
                    throw new InvalidDataException($"not a picture ({response.Content.Headers.ContentType?.MediaType ?? "unknown"}, {bytes.Length} bytes)");
                if (referrer != referrers[0]) Log.Debug($"web picture: {uri.Host} answered with the referrer {(referrer?.ToString() ?? "none")}");
                return bytes;
            }
            throw refused;
        }

        public string GetDefaultDestFolder(InsertImageSource source)
        {
            var folder = source switch
            {
                InsertImageSource.Clipboard => Settings.InsertClipboardImageAction == Enums.InsertImageAction.CopyToPath ? Settings.InsertClipboardImageCopyPath : Settings.DefaultImageBasePath,
                InsertImageSource.Local => Settings.InsertLocalImageAction == Enums.InsertImageAction.CopyToPath ? Settings.InsertLocalImageCopyPath : Settings.DefaultImageBasePath,
                InsertImageSource.Web => Settings.InsertWebImageAction == Enums.InsertImageAction.CopyToPath ? Settings.InsertWebImageCopyPath : Settings.DefaultImageBasePath,
                _ => throw new NotImplementedException()
            };
            return ExpandPathVariables(folder);
        }

        /// <summary>
        /// Typora-style variables in image folders (upstream #8): ${filename} (document name without extension),
        /// ${filedir} (document folder), ${year} ${month} ${day}. Relative results resolve against the document folder.
        /// </summary>
        public string ExpandPathVariables(string folder)
        {
            if (string.IsNullOrEmpty(folder) || !folder.Contains("${")) return folder;
            var filePath = FileViewModel?.FilePath;
            var now = DateTime.Now;
            return Regex.Replace(folder, @"\$\{(\w+)\}", m => m.Groups[1].Value.ToLowerInvariant() switch
            {
                "filename" => string.IsNullOrEmpty(filePath) ? "untitled" : Path.GetFileNameWithoutExtension(filePath),
                "filedir" or "dirname" => string.IsNullOrEmpty(filePath) ? "." : Path.GetDirectoryName(filePath),
                "year" => now.ToString("yyyy"),
                "month" => now.ToString("MM"),
                "day" => now.ToString("dd"),
                _ => m.Value,
            });
        }

        public async Task<string> Upload(InsertImageSource source, string filePath)
        {
            return await ServiceProvider.GetService<ImageUpload>().Upload(source, filePath);
        }

        /// <summary>
        /// A pasted or downloaded picture uploaded: saved first as a file named for its type ("image.png", "image.jpg"),
        /// in a folder of its own. It went up as Path.GetTempFileName()'s "tmpXXXX.tmp" - an S3 object of that name, sent
        /// as application/octet-stream, which a browser downloads rather than shows; a script got the .tmp name too.
        /// </summary>
        public async Task<string> Upload(InsertImageSource source, byte[] bytes)
        {
            var folder = Path.Combine(Path.GetTempPath(), "image-upload-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var file = Path.Combine(folder, $"image.{GetImageType(bytes, "png")}");
                await File.WriteAllBytesAsync(file, bytes);
                return await Upload(source, file);
            }
            finally
            {
                try { Directory.Delete(folder, true); } catch { }
            }
        }

        public enum InsertImageSource { Clipboard, Local, Web };

        public static string GetImageType(byte[] bytes, string defaultType) => ImageData.Sniff(bytes, defaultType);

        /// <summary>
        /// The document saved into another folder (an untitled one's first save, or Save as): the pictures it points at
        /// by relative paths were beside the old place - for an untitled document, the default picture folder - and are
        /// copied to the same relative places beside the new one, so they still show. Only copied, never overwritten or
        /// removed; paths leading out of the new folder (..) are left. Returns how many were copied.
        /// </summary>
        public static int CopyRelativeImages(string markdown, string fromBase, string toBase)
        {
            if (string.IsNullOrEmpty(markdown) || string.IsNullOrEmpty(fromBase) || string.IsNullOrEmpty(toBase)) return 0;
            fromBase = Path.GetFullPath(fromBase);
            toBase = Path.GetFullPath(toBase);
            if (string.Equals(fromBase.TrimEnd('\\'), toBase.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return 0;
            var copied = 0;
            foreach (var address in MarkdownImages.Find(markdown).Select(r => r.Address).Distinct())
            {
                if (string.IsNullOrEmpty(address) || UriHelper.IsWebUrl(address) || address.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                    || address.Contains("://") || UriHelper.IsAbsolutePath(address)) continue;
                string relative;
                try { relative = Uri.UnescapeDataString(address.Split('?', '#')[0]).Replace('/', '\\'); }
                catch { continue; }
                var source = Path.GetFullPath(Path.Combine(fromBase, relative));
                var target = Path.GetFullPath(Path.Combine(toBase, relative));
                if (!target.StartsWith(toBase.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) continue;
                if (!File.Exists(source) || File.Exists(target)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(source, target);
                copied++;
            }
            return copied;
        }

        public static string GetHeaderInfo(byte[] bytes) => ImageData.HeaderInfo(bytes);

        public string ConvertImagePath(string filePath)
        {
            if (UriHelper.IsAbsolutePath(filePath) && Settings.PreferRelativeImagePaths)
            {
                filePath = Path.GetRelativePath(FileViewModel.ImageBasePath, filePath);
                if (Settings.AddSymbolBeforeRelativePath)
                    filePath = "./" + filePath;
            }
            return filePath.Replace('\\', '/');
        }
    }
}
