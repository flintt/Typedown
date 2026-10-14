using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Xml.Linq;
using Typedown.Core.Utilities;

namespace Typedown.ReliabilityTests
{
    /// <summary>
    /// A machine without the WebView2 runtime (todo #15): the start-up says so and points to where the runtime is had,
    /// instead of a blank window - also when the bundled setup could not install it (offline, refused elevation).
    /// </summary>
    [TestClass]
    public class WebView2RuntimeTests
    {
        [TestMethod]
        public void NoVersionMeansNoRuntime()
        {
            // Older loaders answer null rather than throwing when nothing is installed.
            Assert.IsFalse(WebView2Runtime.IsInstalledVersion(null));
            Assert.IsFalse(WebView2Runtime.IsInstalledVersion(""));
            Assert.IsFalse(WebView2Runtime.IsInstalledVersion("  "));
            Assert.IsTrue(WebView2Runtime.IsInstalledVersion("120.0.2210.91"));
        }

        [TestMethod]
        public void TheDownloadPageIsMicrosoftsOwn()
        {
            var uri = new Uri(WebView2Runtime.DownloadUrl);
            Assert.AreEqual("https", uri.Scheme);
            Assert.AreEqual("developer.microsoft.com", uri.Host);
        }

        private static XElement Named(XDocument xaml, string name) =>
            xaml.Descendants().SingleOrDefault(node => (string)node.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == name);

        [TestMethod]
        public void TheInstallWindowOffersTheDownloadAndSaysWhenTheSetupFailed()
        {
            var xaml = XDocument.Load(Path.Combine(Repository.Root, "Dev", "Typedown.Core", "Controls", "DialogControls", "WebView2InstallControl.xaml"));
            var link = Named(xaml, "DownloadLink");
            Assert.IsNotNull(link, "the install window has a download link");
            Assert.AreEqual(WebView2Runtime.DownloadUrl, (string)link.Attribute("NavigateUri"));
            var failed = Named(xaml, "InstallFailedText");
            Assert.IsNotNull(failed, "the install window can say the setup did not install the runtime");
            Assert.AreEqual("Collapsed", (string)failed.Attribute("Visibility"), "only after a setup that failed");
            // The E2E case (WV02) finds them by these ids, in any language.
            foreach (var id in new[] { "WebView2InstallButton", "WebView2CancelButton", "WebView2DownloadLink", "WebView2InstallFailedText" })
                Assert.IsTrue(xaml.Descendants().Any(node => node.Attributes().Any(a => a.Name.LocalName == "AutomationProperties.AutomationId" && a.Value == id)), id);
        }

        [TestMethod]
        public void TheEditorErrorViewLinksTheDownloadToo()
        {
            var xaml = XDocument.Load(Path.Combine(Repository.Root, "Dev", "Typedown.Core", "Controls", "EditorControls", "EditorContainer.xaml"));
            var view = Named(xaml, "EditorInitErrorView");
            Assert.IsNotNull(view);
            Assert.IsTrue(view.Descendants().Any(node => node.Name.LocalName == "HyperlinkButton" && (string)node.Attribute("NavigateUri") == WebView2Runtime.DownloadUrl));
        }

        [TestMethod]
        public void TheNewStringsExistInEnglishWithAChineseComment()
        {
            var resw = XDocument.Load(Path.Combine(Repository.Root, "Dev", "Typedown.Core", "Resources", "Strings", "en", "DialogResources.resw"));
            foreach (var key in new[] { "WebView2DownloadLink", "WebView2InstallFailed" })
            {
                var data = resw.Descendants("data").SingleOrDefault(node => (string)node.Attribute("name") == key);
                Assert.IsNotNull(data, key);
                Assert.IsFalse(string.IsNullOrWhiteSpace((string)data.Element("comment")), key + " comment");
            }
        }
    }
}
