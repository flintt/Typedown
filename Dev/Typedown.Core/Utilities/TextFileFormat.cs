using System;
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
        /// that — along with the shape to write back. Text files with no byte order mark are read as UTF-8, which
        /// is what the app has always done.
        /// </summary>
        public static async Task<(string Text, TextFileFormat Format)> ReadAsync(string path, Encoding encodingWithoutMark = null)
        {
            var bytes = await File.ReadAllBytesAsync(path);
            var (encoding, bomLength) = DetectEncoding(bytes);
            // The reader's choice for a file with no byte order mark (the status bar's encoding menu): GBK, Big5...
            if (bomLength == 0 && encodingWithoutMark != null) encoding = encodingWithoutMark;
            var text = encoding.GetString(bytes, bomLength, bytes.Length - bomLength);
            var format = new TextFileFormat(encoding, bomLength > 0, DetectLineEnding(text), !RoundTrips(encoding, bytes, bomLength, text));
            return (Normalize(text), format);
        }

        /// <summary>The encodings a file without a byte order mark can be read in again, by name (code pages registered first).</summary>
        public static readonly string[] ReopenEncodings = { "utf-8", "gb18030", "big5", "shift_jis", "euc-kr", "windows-1252" };

        public static Encoding EncodingByName(string name)
        {
            if (name == "utf-8") return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
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
                    UnicodeEncoding u => u.GetPreamble() is [0xFE, 0xFF] ? "UTF-16 BE" : "UTF-16 LE",
                    UTF32Encoding u => u.GetPreamble() is [0, 0, 0xFE, 0xFF] ? "UTF-32 BE" : "UTF-32 LE",
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
