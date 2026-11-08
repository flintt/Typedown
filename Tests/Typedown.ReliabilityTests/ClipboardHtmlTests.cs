using System.Text;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Typedown.Core.Utilities;

namespace Typedown.ReliabilityTests
{
    /// <summary>
    /// The HTML a copy puts on the clipboard (ClipboardHtml.Format): its offsets point where they say, in UTF-8 bytes, and
    /// the document is written as the browsers write it, so editors reading the body by a lower-case pattern find it.
    /// </summary>
    [TestClass]
    public class ClipboardHtmlTests
    {
        private static int Offset(string cf, string name) => int.Parse(Regex.Match(cf, "^" + name + @":(\d+)\r$", RegexOptions.Multiline).Groups[1].Value);

        [TestMethod]
        public void TheOffsetsPointAtTheDocumentAndTheFragmentInUtf8Bytes()
        {
            var fragment = "<table><tr><td>名字</td><td>ä</td></tr></table>";
            var cf = ClipboardHtml.Format(fragment);
            var bytes = Encoding.UTF8.GetBytes(cf);
            string Slice(int from, int to) => Encoding.UTF8.GetString(bytes, from, to - from);
            Assert.AreEqual(fragment, Slice(Offset(cf, "StartFragment"), Offset(cf, "EndFragment")));
            Assert.IsTrue(Slice(Offset(cf, "StartHTML"), Offset(cf, "EndHTML")).StartsWith("<html>"));
            Assert.AreEqual(bytes.Length, Offset(cf, "EndHTML"));
            StringAssert.StartsWith(cf, "Version:1.0\r\n");
        }

        [TestMethod]
        public void TheDocumentIsWrittenAsTheBrowsersWriteIt()
        {
            var cf = ClipboardHtml.Format("<p>a</p>");
            // Muya's (and the original Typedown's) paste takes the body by /<body[^>]*>([\s\S]*)<\/body>/.
            Assert.AreEqual("\r\n<!--StartFragment--><p>a</p><!--EndFragment-->\r\n", Regex.Match(cf, @"<body[^>]*>([\s\S]*)<\/body>").Groups[1].Value);
            Assert.AreEqual(ClipboardHtml.Format(""), ClipboardHtml.Format(null));
        }
    }
}
