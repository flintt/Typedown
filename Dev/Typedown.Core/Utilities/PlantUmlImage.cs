using System;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Typedown.Core.Utilities
{
    /// <summary>Copying a diagram from a configured PlantUML server must not require disabling browser isolation.</summary>
    public static class PlantUmlImage
    {
        public const string DefaultServer = "https://www.plantuml.com/plantuml";
        public const int MaxPngBytes = 16 * 1024 * 1024;
        private static readonly HttpClient client = new(new HttpClientHandler { AllowAutoRedirect = false });

        public static bool IsAllowed(string address, string configuredServer)
        {
            if (!Uri.TryCreate(configuredServer, UriKind.Absolute, out var server) ||
                (server.Scheme != "http" && server.Scheme != "https")) server = new Uri(DefaultServer);
            if (!Uri.TryCreate(address, UriKind.Absolute, out var request) ||
                server.UserInfo.Length != 0 || request.UserInfo.Length != 0 ||
                request.Query.Length != 0 || request.Fragment.Length != 0 ||
                !string.Equals(request.Scheme, server.Scheme, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(request.IdnHost, server.IdnHost, StringComparison.OrdinalIgnoreCase) || request.Port != server.Port)
                return false;
            var prefix = server.AbsolutePath.TrimEnd('/') + "/png/";
            return request.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal) &&
                Regex.IsMatch(request.AbsolutePath.Substring(prefix.Length), @"\A~h[0-9a-fA-F]+\z");
        }

        public static async Task<string> GetPngAsync(string address, string configuredServer, bool enabled, HttpClient http = null)
        {
            if (!enabled || !IsAllowed(address, configuredServer)) return null;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try
            {
                using var response = await (http ?? client).GetAsync(address, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if (!response.IsSuccessStatusCode || response.Headers.Location != null ||
                    !string.Equals(response.Content.Headers.ContentType?.MediaType, "image/png", StringComparison.OrdinalIgnoreCase) ||
                    response.Content.Headers.ContentLength > MaxPngBytes) return null;
                using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
                using var output = new MemoryStream();
                var buffer = new byte[81920];
                int count;
                while ((count = await input.ReadAsync(buffer, 0, buffer.Length, timeout.Token)) != 0)
                {
                    if (output.Length + count > MaxPngBytes) return null;
                    output.Write(buffer, 0, count);
                }
                var bytes = output.ToArray();
                if (bytes.Length < 8 || bytes[0] != 137 || bytes[1] != 80 || bytes[2] != 78 || bytes[3] != 71 ||
                    bytes[4] != 13 || bytes[5] != 10 || bytes[6] != 26 || bytes[7] != 10) return null;
                return Convert.ToBase64String(bytes);
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is OperationCanceledException || ex is IOException)
            {
                return null;
            }
        }
    }
}
