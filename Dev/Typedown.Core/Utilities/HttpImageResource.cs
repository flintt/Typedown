using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Typedown.Core.Utilities
{
    /// <summary>HTTP images remain drawable from the HTTPS editor without allowing active mixed content.</summary>
    public static class HttpImageResource
    {
        public const string Endpoint = "https://local-images.invalid/__http_image";
        public const int MaxBytes = 32 * 1024 * 1024;
        private static readonly HttpClient client = new(new HttpClientHandler { AllowAutoRedirect = false });

        public static bool IsWebAddress(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            (uri.Scheme == "http" || uri.Scheme == "https") && uri.UserInfo.Length == 0;

        public static async Task<(byte[] Bytes, string ContentType)?> ReadAsync(string address, HttpClient http = null,
            CancellationToken cancellationToken = default)
        {
            // The page uses this for HTTP images only. HTTPS is allowed only as a redirect destination.
            if (!IsWebAddress(address) || new Uri(address).Scheme != "http") return null;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                for (var redirect = 0; redirect <= 5; redirect++)
                {
                    using var response = await (http ?? client).GetAsync(address, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                    if ((int)response.StatusCode is >= 300 and < 400)
                    {
                        var location = response.Headers.Location;
                        if (location == null) return null;
                        address = new Uri(new Uri(address), location).AbsoluteUri;
                        if (!IsWebAddress(address)) return null;
                        continue;
                    }
                    var mime = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
                    if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxBytes ||
                        (!mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase) && mime != "application/octet-stream")) return null;
                    using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
                    using var output = new MemoryStream();
                    var buffer = new byte[81920];
                    int count;
                    while ((count = await input.ReadAsync(buffer, 0, buffer.Length, timeout.Token)) != 0)
                    {
                        if (output.Length + count > MaxBytes) return null;
                        output.Write(buffer, 0, count);
                    }
                    return (output.ToArray(), mime);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is OperationCanceledException || ex is IOException || ex is UriFormatException) { }
            return null;
        }
    }
}
