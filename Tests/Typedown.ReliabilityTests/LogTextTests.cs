using Microsoft.VisualStudio.TestTools.UnitTesting;
using Typedown.Core.Utilities;

namespace Typedown.ReliabilityTests
{
    /// <summary>
    /// The debug log says where and how much of a document, never its words (LogText; the E2E case DL01 reads a real log
    /// for marker words).
    /// </summary>
    [TestClass]
    public class LogTextTests
    {
        [TestMethod]
        public void AnErrorKeepsItsFirstLineWithoutWhatItQuotes()
        {
            Assert.AreEqual("Parse error on line 2: (+25 chars left out)", LogText.Error("Parse error on line 2:\n...A[Zqxsecret]-->B\n---^"));
            Assert.AreEqual("Cannot read properties of undefined (reading <6 chars>)", LogText.Error("Cannot read properties of undefined (reading 'secret')"));
            Assert.AreEqual("Unexpected token <1 chars>, <9 chars> is not valid JSON", LogText.Error("Unexpected token 'Z', \"Zqxsecret\" is not valid JSON"));
            Assert.AreEqual("Could not find file 'C:\\docs\\a.md'.", LogText.Error("Could not find file 'C:\\docs\\a.md'."));
        }

        [TestMethod]
        public void ALongErrorIsCutAndSaysHowMuchWasLeftOut()
        {
            var text = LogText.Error(new string('x', 200) + "\nmore");
            Assert.IsTrue(text.StartsWith(new string('x', LogText.ErrorLimit) + " (+"), text);
            Assert.IsTrue(text.EndsWith("(+45 chars left out)"), text);
            Assert.AreEqual("", LogText.Error(null));
        }

        [TestMethod]
        public void AStackKeepsItsFramesOnly()
        {
            var stack = "Error: Zqxsecret went wrong\n    at render (http://localhost/main.js:1:200)\n    at x (http://localhost/main.js:2:5)";
            Assert.AreEqual("at render (http://localhost/main.js:1:200) | at x (http://localhost/main.js:2:5)", LogText.Stack(stack));
            Assert.AreEqual("render@http://localhost/main.js:1:200", LogText.Stack("Zqxsecret\nrender@http://localhost/main.js:1:200"));
        }

        [TestMethod]
        public void HeadingsAndAddressesBySizeAndPlace()
        {
            Assert.AreEqual("heading 3 of 40 (level 2, 9 chars)", LogText.Heading(2, 40, 2, "Zqxsecret"));
            Assert.AreEqual("a heading not in the outline of 40 (none)", LogText.Heading(-1, 40, 0, null));
            Assert.AreEqual("https://example.com", LogText.Address("https://example.com/Zqxsecret?q=Zqxsecret"));
            Assert.AreEqual("mailto:", LogText.Address("mailto:Zqxsecret@example.com"));
            Assert.AreEqual("a relative address (12 chars)", LogText.Address("Zqxsecret.md"));
        }
    }
}
