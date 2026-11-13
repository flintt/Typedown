using Microsoft.VisualStudio.TestTools.UnitTesting;
using Typedown.Core.Utilities;

namespace Typedown.ReliabilityTests
{
    [TestClass]
    public class WebViewPagePolicyTests
    {
        private const string Editor = "https://editor.example/index.html";

        [DataTestMethod]
        [DataRow("https://editor.example/index.html")]
        [DataRow("https://EDITOR.example:443/index.html#heading")]
        [DataRow("https://editor.example/%69ndex.html")]
        public void AcceptsTheExactDocumentAndItsFragments(string address) =>
            Assert.IsTrue(WebViewPagePolicy.Matches(address, Editor));

        [DataTestMethod]
        [DataRow("https://editor.example/index.html.evil")]
        [DataRow("https://editor.example/index.html/other")]
        [DataRow("https://editor.example/Index.html")]
        [DataRow("https://editor.example/index.html?other=1")]
        [DataRow("https://editor.example:444/index.html")]
        [DataRow("https://editor.example.evil/index.html")]
        [DataRow("https://user@editor.example/index.html")]
        [DataRow("https://editor.example@evil.example/index.html")]
        [DataRow("http://editor.example/index.html")]
        [DataRow("https://editor.example/index.html%2fother")]
        [DataRow("https://editor.example/index.html%3Fother=1")]
        [DataRow("about:blank")]
        [DataRow("index.html")]
        [DataRow("")]
        [DataRow(null)]
        public void RefusesOtherDocumentsAndDeceptiveAddresses(string address) =>
            Assert.IsFalse(WebViewPagePolicy.Matches(address, Editor));

        [DataTestMethod]
        [DataRow("http://localhost:30000/")]
        [DataRow("http://localhost:3000.evil.example/")]
        [DataRow("http://localhost:3000/other.html")]
        [DataRow("http://localhost:3000/?other=1")]
        public void DevelopmentServerUsesAnExactOriginAndDocument(string address) =>
            Assert.IsFalse(WebViewPagePolicy.Matches(address, "http://localhost:3000/"));

        [TestMethod]
        public void FilePagesMatchTheWholePath()
        {
            const string file = "file:///C:/Program%20Files/App/index.html";
            Assert.IsTrue(WebViewPagePolicy.Matches(file + "#heading", file));
            Assert.IsFalse(WebViewPagePolicy.Matches(file + ".html", file));
            Assert.IsFalse(WebViewPagePolicy.Matches(file + "/other", file));
            Assert.IsFalse(WebViewPagePolicy.Matches("file://server/C:/Program%20Files/App/index.html", file));
        }

        [TestMethod]
        public void BothTheMessageSourceAndActiveDocumentMustBeTrusted()
        {
            Assert.IsTrue(WebViewPagePolicy.CanReceiveMessage(Editor, Editor, Editor));
            Assert.IsFalse(WebViewPagePolicy.CanReceiveMessage("https://evil.example/", Editor, Editor));
            Assert.IsFalse(WebViewPagePolicy.CanReceiveMessage(Editor, "https://evil.example/", Editor));
            Assert.IsFalse(WebViewPagePolicy.CanReceiveMessage(Editor, null, Editor));
        }
    }
}
