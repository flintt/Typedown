using System.Text;

namespace Typedown.Core.Utilities
{
    /// <summary>
    /// HTML as it goes on the Windows clipboard (CF_HTML): the header with the byte offsets, then a document with the
    /// fragment between its markers.
    /// </summary>
    public static class ClipboardHtml
    {
        /// <summary>
        /// The fragment in CF_HTML, written as the browsers write it: lower-case tags, the markers without spaces. What
        /// Windows' own HtmlFormatHelper.CreateHtmlFormat writes (&lt;HTML&gt;&lt;BODY&gt;, "&lt;!--StartFragment --&gt;")
        /// editors that read the body by a lower-case pattern (Muya's, the original Typedown's) did not find, and pasted
        /// the header as text: "Version:1.0StartHTML:00000097..." (a reader, 2026-11-04). Offsets count UTF-8 bytes.
        /// </summary>
        public static string Format(string fragment)
        {
            const string header = "Version:1.0\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";
            const string before = "<html>\r\n<body>\r\n<!--StartFragment-->";
            const string after = "<!--EndFragment-->\r\n</body>\r\n</html>";
            fragment ??= "";
            var utf8 = Encoding.UTF8;
            // The header's length does not depend on the numbers: each is ten digits.
            int startHtml = utf8.GetByteCount(string.Format(header, 0, 0, 0, 0));
            int startFragment = startHtml + utf8.GetByteCount(before);
            int endFragment = startFragment + utf8.GetByteCount(fragment);
            int endHtml = endFragment + utf8.GetByteCount(after);
            return string.Format(header, startHtml, endHtml, startFragment, endFragment) + before + fragment + after;
        }
    }
}
