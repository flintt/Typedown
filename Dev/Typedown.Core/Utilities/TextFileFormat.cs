using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Typedown.Core.Utilities
{
    /// <summary>The byte shape a file was read in, so saving it can put that shape back.</summary>
    public sealed class TextFileFormat
    {
        /// <summary>A new document: UTF-8 without a byte order mark, and the line ending this system uses.</summary>
        public static TextFileFormat Default { get; } = new(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), false, Environment.NewLine == "\r\n" ? "\r\n" : "\n");

        public Encoding Encoding { get; }

        public bool HasByteOrderMark { get; }

        /// <summary>
        /// True when the file's bytes did not round-trip through <see cref="Encoding"/> — a non-UTF-8 file (GBK,
        /// Latin-1, …) read as UTF-8 has U+FFFD where bytes could not be decoded, and saving it back would replace
        /// those bytes for good. Callers refuse to silently overwrite such a file.
        /// </summary>
        public bool LossyDecode { get; }

        /// <summary>"\n", "\r\n" or "\r"; the one the file used most, and what a save writes back.</summary>
        public string LineEnding { get; }

        private TextFileFormat(Encoding encoding, bool hasByteOrderMark, string lineEnding, bool lossyDecode = false)
        {
            Encoding = encoding;
            HasByteOrderMark = hasByteOrderMark;
            LineEnding = lineEnding;
            LossyDecode = lossyDecode;
        }

        /// <summary>
        /// Reads a file and returns its text with every line ending turned into "\n" — the editor only ever sees
        /// that — along with the shape to write back. A file with no byte order mark is read in the encoding it is
        /// found to be in (<see cref="GuessEncoding"/>).
        /// </summary>
        public static async Task<(string Text, TextFileFormat Format)> ReadAsync(string path, Encoding encodingWithoutMark = null) =>
            Read(await File.ReadAllBytesAsync(path), encodingWithoutMark);

        /// <summary>
        /// <see cref="ReadAsync"/> on bytes already read. <paramref name="ansiCodePage"/>: the system's legacy code page
        /// (936 on Chinese Windows), the guess for a file too short to tell; the running system's when not given.
        /// </summary>
        public static (string Text, TextFileFormat Format) Read(byte[] bytes, Encoding encodingWithoutMark = null, int? ansiCodePage = null)
        {
            var (encoding, bomLength) = DetectEncoding(bytes);
            // The reader's choice for a file with no byte order mark (the status bar's encoding menu): GBK, Big5...
            if (bomLength == 0) encoding = encodingWithoutMark ?? GuessEncoding(bytes, ansiCodePage ?? CultureInfo.CurrentCulture.TextInfo.ANSICodePage);
            var text = encoding.GetString(bytes, bomLength, bytes.Length - bomLength);
            var format = new TextFileFormat(encoding, bomLength > 0, DetectLineEnding(text), !RoundTrips(encoding, bytes, bomLength, text));
            return (Normalize(text), format);
        }

        /// <summary>
        /// The encoding of a file with no byte order mark. All files used to be read as UTF-8: a GBK, Big5 or
        /// Shift-JIS file came up garbled (a reader's report). In turn, each taken only if the file's bytes come back
        /// from it unchanged:
        /// <list type="number">
        /// <item>valid UTF-8 (plain ASCII too) is UTF-8;</item>
        /// <item>UTF-16 without its mark, told by where the zero bytes of ASCII letters fall;</item>
        /// <item>the charset detector (Mozilla's, UTF.Unknown) when it is at least half sure: right on a paragraph of
        /// Chinese, Japanese, Korean, Russian or Western text, lost on a few characters;</item>
        /// <item>GBK for a few Chinese characters on a Windows of another language: pairs of high bytes, all Chinese;</item>
        /// <item>the system's legacy code page, for those few characters (GBK on Chinese Windows);</item>
        /// <item>else UTF-8 as before, its decode marked lossy so that nothing saves over the file unasked.</item>
        /// </list>
        /// </summary>
        internal static Encoding GuessEncoding(byte[] bytes, int ansiCodePage)
        {
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            if (IsValidUtf8(bytes)) return utf8;
            if (Utf16WithoutMark(bytes) is { } utf16) return utf16;
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var detected = UtfUnknown.CharsetDetector.DetectFromBytes(bytes).Detected;
            // Bytes that read as nothing but Chinese in GBK, the detector unsure (it gave "笔记" as 1252 at exactly 0.5): GBK.
            var gbkLike = ansiCodePage != 936 && LooksLikeGbk();
            if (gbkLike && (detected == null || detected.Confidence < 0.6f) && Clean(Encoding.GetEncoding("gb18030")) is { } likelyGbk) return likelyGbk;
            if (detected != null && detected.Confidence >= 0.5f && Clean(ByDetectorName(detected.EncodingName)) is { } byDetector) return byDetector;
            // A few Chinese characters on a non-Chinese Windows (English Windows with a Chinese reader, w10 in EN01):
            // too short for the detector, and the system's 1252 took "笔记" for "±Ê¼Ç". GBK when every non-ASCII byte is
            // one of a pair of high bytes and they all read as Chinese - a Western accent is one high byte among letters.
            if (gbkLike && Clean(Encoding.GetEncoding("gb18030")) is { } gbk) return gbk;
            if (ansiCodePage > 0 && ansiCodePage != 65001 && Clean(TryGetEncoding(ansiCodePage)) is { } byLocale) return byLocale;
            // A Western file on a Windows of another language (a French note on Chinese Windows, EN01 on hp): its
            // accents in 1252, the detector unsure and the system's code page not reading it. Nothing in it a C1 control.
            if (ansiCodePage != 1252 && Clean(TryGetEncoding(1252)) is { } western) return western;
            return utf8;

            bool LooksLikeGbk()
            {
                var run = 0;
                foreach (var b in bytes)
                {
                    if (b >= 0x81 && b != 0xFF) { run++; continue; }
                    if (b >= 0x80 || run % 2 != 0) return false;
                    run = 0;
                }
                if (run % 2 != 0) return false;
                string text;
                try { text = Encoding.GetEncoding("gb18030").GetString(bytes); }
                catch { return false; }
                // Chinese characters and the full-width punctuation around them; nothing else above ASCII.
                return text.Any(ch => ch > 127) && text.All(ch => ch < 128 || ch is >= '　' and <= '〿' or >= '㐀' and <= '䶿' or >= '一' and <= '鿿' or >= '＀' and <= '￯');
            }

            Encoding Clean(Encoding encoding)
            {
                if (encoding == null) return null;
                try
                {
                    var text = encoding.GetString(bytes);
                    // C1 controls (U+0080-U+009F) are in no text: 1252 gives its five undefined bytes as them, so with
                    // them taken a Western Windows read every file "without loss" and auto-save wrote over any (SB03).
                    return text.Contains('�') || text.Any(ch => ch is >= '\u0080' and <= '\u009F') || !RoundTrips(encoding, bytes, 0, text) ? null : encoding;
                }
                catch
                {
                    return null;
                }
            }
        }

        // The detector's names as .NET knows them; ISO-8859-1 as Windows-1252, its superset (the curly quotes and the
        // euro a Western file has are 1252's).
        private static Encoding ByDetectorName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var known = name.ToLowerInvariant() switch
            {
                "iso-8859-1" or "ascii" => "windows-1252",
                "gb2312" or "gbk" or "x-cp936" => "gb18030",
                "shift-jis" => "shift_jis",
                "utf-16le" or "utf-16be" or "utf-8" or "utf-32le" or "utf-32be" => null, // UTF-8 and the marked ones are tested before
                var other => other,
            };
            if (known == null) return null;
            try { return Encoding.GetEncoding(known); }
            catch { return null; }
        }

        private static Encoding TryGetEncoding(int codePage)
        {
            try { return codePage == 936 ? Encoding.GetEncoding("gb18030") : Encoding.GetEncoding(codePage); }
            catch { return null; }
        }

        private static bool IsValidUtf8(byte[] bytes)
        {
            try
            {
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetCharCount(bytes);
                return true;
            }
            catch (DecoderFallbackException)
            {
                return false;
            }
        }

        // UTF-16 without a mark: text with Latin letters, digits or line breaks has a zero byte beside each of them -
        // after it in little-endian, before it in big-endian - and almost never on the other side.
        private static Encoding Utf16WithoutMark(byte[] bytes)
        {
            if (bytes.Length < 4 || bytes.Length % 2 != 0) return null;
            int evenZeros = 0, oddZeros = 0;
            for (var i = 0; i < bytes.Length; i++)
                if (bytes[i] == 0) { if (i % 2 == 0) evenZeros++; else oddZeros++; }
            var pairs = bytes.Length / 2;
            if (oddZeros >= pairs * 0.3 && evenZeros <= pairs * 0.05) return new UnicodeEncoding(bigEndian: false, byteOrderMark: false);
            if (evenZeros >= pairs * 0.3 && oddZeros <= pairs * 0.05) return new UnicodeEncoding(bigEndian: true, byteOrderMark: false);
            return null;
        }

        /// <summary>The encodings a file without a byte order mark can be read in again, by name (code pages registered first).</summary>
        public static readonly string[] ReopenEncodings = { "utf-8", "gb18030", "big5", "shift_jis", "euc-jp", "euc-kr", "windows-1252", "windows-1251", "utf-16le", "utf-16be" };

        public static Encoding EncodingByName(string name)
        {
            if (name == "utf-8") return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            if (name == "utf-16le" || name == "utf-16be") return new UnicodeEncoding(bigEndian: name == "utf-16be", byteOrderMark: false);
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(name);
        }

        /// <summary>"UTF-8", "UTF-8 BOM", "GB18030", "UTF-16 LE"...: what the status bar shows.</summary>
        public string EncodingLabel
        {
            get
            {
                var name = Encoding switch
                {
                    UTF8Encoding => "UTF-8",
                    // By code page: one read without its mark has no preamble to tell the byte order by.
                    UnicodeEncoding u => u.CodePage == 1201 ? "UTF-16 BE" : "UTF-16 LE",
                    UTF32Encoding u => u.CodePage == 12001 ? "UTF-32 BE" : "UTF-32 LE",
                    _ => Encoding.WebName.ToUpperInvariant(),
                };
                return Encoding is UTF8Encoding && HasByteOrderMark ? name + " BOM" : name;
            }
        }

        public string LineEndingLabel => LineEnding == "\r\n" ? "CRLF" : LineEnding == "\r" ? "CR" : "LF";

        /// <summary>This shape written with another line ending.</summary>
        public TextFileFormat WithLineEnding(string lineEnding) => new(Encoding, HasByteOrderMark, lineEnding, LossyDecode);

        /// <summary>
        /// This shape turned into UTF-8 (no mark), the line ending kept: what the reader asked for, so the text as shown
        /// is what is written, and auto-save may write it again.
        /// </summary>
        public TextFileFormat AsUtf8() => new(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), false, LineEnding);

        /// <summary>
        /// Whether writing a document read in this shape over a file needs the reader's yes first: the file was not read
        /// exactly (<see cref="LossyDecode"/>), so the write replaces the bytes shown as U+FFFD for good (R07). A save by
        /// hand asks (Save as UTF-8, or Cancel); an automatic save and an automation client's do not write at all. A new
        /// file, or one gone from disk, has no bytes to lose.
        /// </summary>
        public bool NeedsConsentToOverwrite(bool fileExists) => LossyDecode && fileExists;

        /// <summary>The text as it should hit the disk: the file's own line endings, and its byte order mark.</summary>
        public byte[] GetBytes(string text)
        {
            var body = LineEnding == "\n" ? Normalize(text) : Normalize(text).Replace("\n", LineEnding);
            var preamble = HasByteOrderMark ? Encoding.GetPreamble() : Array.Empty<byte>();
            var bytes = Encoding.GetBytes(body);
            if (preamble.Length == 0) return bytes;
            var result = new byte[preamble.Length + bytes.Length];
            preamble.CopyTo(result, 0);
            bytes.CopyTo(result, preamble.Length);
            return result;
        }

        /// <summary>Every line ending as "\n". The editor works in that alone; the file's own form is restored on save.</summary>
        public static string Normalize(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');

        /// <summary>Whether re-encoding the decoded text reproduces the file's body bytes exactly — a mismatch
        /// means the decode lost information and a save would not preserve the original file.</summary>
        private static bool RoundTrips(Encoding encoding, byte[] original, int bomLength, string decoded)
        {
            try
            {
                var reencoded = encoding.GetBytes(decoded);
                var bodyLength = original.Length - bomLength;
                if (reencoded.Length != bodyLength) return false;
                for (var i = 0; i < bodyLength; i++)
                    if (reencoded[i] != original[bomLength + i]) return false;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static (Encoding Encoding, int BomLength) DetectEncoding(byte[] bytes)
        {
            // UTF-32's mark starts with UTF-16's, so it has to be tested first.
            if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0x00 && bytes[3] == 0x00)
                return (new UTF32Encoding(bigEndian: false, byteOrderMark: true), 4);
            if (bytes.Length >= 4 && bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xFE && bytes[3] == 0xFF)
                return (new UTF32Encoding(bigEndian: true, byteOrderMark: true), 4);
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return (new UnicodeEncoding(bigEndian: false, byteOrderMark: true), 2);
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return (new UnicodeEncoding(bigEndian: true, byteOrderMark: true), 2);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return (new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), 3);
            return (new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 0);
        }

        /// <summary>
        /// The line ending the file uses. A file that mixes them is written back with whichever it used most, so a
        /// save cannot leave a file more mixed than it was; ties go to CRLF, then LF.
        /// </summary>
        private static string DetectLineEnding(string text)
        {
            int crlf = 0, lf = 0, cr = 0;
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] == '\r')
                {
                    if (i + 1 < text.Length && text[i + 1] == '\n') { crlf++; i++; }
                    else cr++;
                }
                else if (text[i] == '\n')
                {
                    lf++;
                }
            }
            if (crlf == 0 && lf == 0 && cr == 0) return Default.LineEnding; // a single line says nothing
            var most = new[] { crlf, lf, cr }.Max();
            return most == crlf ? "\r\n" : most == lf ? "\n" : "\r";
        }
    }
}
