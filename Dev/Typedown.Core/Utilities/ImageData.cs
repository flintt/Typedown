using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Typedown.Core.Utilities
{
    /// <summary>
    /// A picture's bytes: what kind it is (from its first bytes, not its name), and the picture written into the document
    /// itself as a data: address (Settings > Image > Embed as base64) and read back out of one.
    /// </summary>
    public static class ImageData
    {
        /// <summary>The kind of picture the bytes are, as a file extension ("png", "jpg", "svg"...), or <paramref name="defaultType"/>.</summary>
        public static string Sniff(byte[] bytes, string defaultType)
        {
            string headerCode = HeaderInfo(bytes).ToUpper();

            // Every JPEG starts FF D8 FF (E0 JFIF, E1 Exif, DB raw...): only E0 was known, and an Exif photo was "png".
            if (headerCode.StartsWith("FFD8FF"))
                return "jpg";
            if (bytes.Length >= 12 && Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" && Encoding.ASCII.GetString(bytes, 8, 4) == "WEBP")
                return "webp";
            if (bytes.Length >= 12 && Encoding.ASCII.GetString(bytes, 4, 4) == "ftyp" && Encoding.ASCII.GetString(bytes, 8, 4) is "avif" or "avis")
                return "avif";
            if (headerCode.StartsWith("00000100"))
                return "ico";
            if (IsSvg(bytes))
                return "svg";
            if (headerCode.StartsWith("49492A"))
                return "tiff";
            if (headerCode.StartsWith("424D"))
                return "bmp";
            if (headerCode.StartsWith("474946"))
                return "gif";
            if (headerCode.StartsWith("89504E470D0A1A0A"))
                return "png";
            return defaultType;
        }

        private static bool IsSvg(byte[] bytes)
        {
            var head = Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 1024)).TrimStart('﻿', ' ', '\t', '\r', '\n');
            return head.StartsWith("<svg", StringComparison.OrdinalIgnoreCase)
                || (head.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) && head.IndexOf("<svg", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public static string HeaderInfo(byte[] bytes)
        {
            var sb = new StringBuilder();
            foreach (byte b in bytes.Take(8))
                sb.Append(b.ToString("X2"));
            return sb.ToString();
        }

        /// <summary>The media type of a kind of picture: jpg is image/jpeg, svg image/svg+xml.</summary>
        public static string MediaType(string type) => type switch
        {
            "jpg" => "image/jpeg",
            "svg" => "image/svg+xml",
            "ico" => "image/x-icon",
            _ => "image/" + type,
        };

        /// <summary>The file extension of a picture's media type (image/jpeg is jpg), or null for one that is not a picture.</summary>
        public static string Extension(string mediaType)
        {
            if (string.IsNullOrEmpty(mediaType) || !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return null;
            return mediaType.Substring(6).ToLowerInvariant() switch
            {
                "jpeg" or "pjpeg" => "jpg",
                "svg+xml" => "svg",
                "x-icon" or "vnd.microsoft.icon" => "ico",
                var other => Regex.IsMatch(other, "^[a-z0-9]+$") ? other : null,
            };
        }

        /// <summary>The picture as a data: address, its kind read from its bytes (PNG when they say nothing).</summary>
        public static string ToDataUri(byte[] bytes) =>
            $"data:{MediaType(Sniff(bytes, "png"))};base64,{Convert.ToBase64String(bytes)}";

        private static readonly Regex dataUri = new(@"^data:(?<type>[\w.+-]+/[\w.+-]+)?(?<params>(;[\w.+-]+=[^;,]*)*)(?<base64>;base64)?,(?<data>.*)$",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        /// <summary>
        /// A picture's bytes and file extension out of a data: address. A base64 one may be broken over lines or have spaces
        /// (a hand-written one); an svg one may be written out instead (percent-escaped). False for anything else.
        /// </summary>
        public static bool TryParseDataUri(string uri, out byte[] bytes, out string extension)
        {
            bytes = null;
            extension = null;
            var m = uri == null ? null : dataUri.Match(uri.Trim());
            if (m == null || !m.Success) return false;
            extension = Extension(m.Groups["type"].Value);
            if (extension == null) return false;
            var data = m.Groups["data"].Value;
            try
            {
                bytes = m.Groups["base64"].Success
                    ? Convert.FromBase64String(Regex.Replace(data, @"\s+", ""))
                    : Encoding.UTF8.GetBytes(Uri.UnescapeDataString(data));
            }
            catch (FormatException)
            {
                bytes = null;
                return false;
            }
            return bytes.Length > 0;
        }

        /// <summary>The address is a picture written into the document (data:image/...).</summary>
        public static bool IsDataUri(string uri) => uri != null && uri.TrimStart().StartsWith("data:image/", StringComparison.OrdinalIgnoreCase);
    }
}
