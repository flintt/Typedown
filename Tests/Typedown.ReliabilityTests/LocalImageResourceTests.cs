using Microsoft.VisualStudio.TestTools.UnitTesting;
using Typedown.Core.Utilities;

namespace Typedown.ReliabilityTests
{
    [TestClass]
    public class LocalImageResourceTests
    {
        private const string Endpoint = "https://editor.invalid/__local_image";
        private static string Address(string source) => Endpoint + "?src=" + Uri.EscapeDataString(source);

        [TestMethod]
        public async Task LocalReadsPreserveBytesRejectOversizeAndHonorCancellation()
        {
            var path = Path.GetTempFileName();
            try
            {
                var bytes = new byte[] { 1, 2, 3, 4 };
                await File.WriteAllBytesAsync(path, bytes);
                CollectionAssert.AreEqual(bytes, await LocalImageResource.ReadAsync(path, default));
                using (var file = new FileStream(path, FileMode.Open, FileAccess.Write)) file.SetLength(LocalImageResource.MaxBytes + 1L);
                Assert.IsNull(await LocalImageResource.ReadAsync(path, default));
                using var canceled = new CancellationTokenSource();
                canceled.Cancel();
                await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => LocalImageResource.ReadAsync(path, canceled.Token));
            }
            finally { File.Delete(path); }
        }

        [DataTestMethod]
        [DataRow(@"C:\notes\a + b#1.png", "image/png")]
        [DataRow(@"D:\图\流程 图.svg", "image/svg+xml")]
        [DataRow(@"\\server\share\a.jpg", "image/jpeg")]
        [DataRow(@"C:\notes\%23.png", "image/png")]
        [DataRow(@"C:\notes\sub\..\a.webp", "image/webp")]
        [DataRow("file:///C:/notes/a%20b.png", "image/png")]
        public void AcceptsAbsoluteImageReferencesWithoutDoubleDecoding(string source, string mime)
        {
            Assert.IsTrue(LocalImageResource.TryResolve(Address(source), Endpoint, "GET", true, out var path, out var contentType));
            Assert.AreEqual(mime, contentType);
            if (source.Contains("%23")) StringAssert.Contains(path, "%23.png");
            if (source.StartsWith("file:")) StringAssert.Contains(path, "a b.png");
        }

        [DataTestMethod]
        [DataRow(@"C:\notes\secret.txt")]
        [DataRow(@"C:\notes\page.html")]
        [DataRow(@"C:\notes\a.png:secret.png")]
        [DataRow(@"\\?\C:\notes\a.png")]
        [DataRow(@"\\.\C:\notes\a.png")]
        [DataRow(@"\??\C:\notes\a.png")]
        [DataRow(@"C:a.png")]
        [DataRow("../a.png")]
        [DataRow("https://evil.example/a.png")]
        [DataRow("data:image/png;base64,AA==")]
        public void RefusesNonImagesDevicesStreamsAndRelativePaths(string source) =>
            Assert.IsFalse(LocalImageResource.TryResolve(Address(source), Endpoint, "GET", true, out _, out _));

        [TestMethod]
        public void AnImageCannotBeReadAsTextOrExecuted()
        {
            var url = Address(@"C:\notes\a.svg");
            Assert.IsFalse(LocalImageResource.TryResolve(url, Endpoint, "GET", false, out _, out _), "fetch, script and frame contexts");
            Assert.IsFalse(LocalImageResource.TryResolve(url, Endpoint, "POST", true, out _, out _));
            Assert.IsFalse(LocalImageResource.TryResolve(url.Replace("editor.invalid/", "editor.invalid.evil/"), Endpoint, "GET", true, out _, out _));
            Assert.IsFalse(LocalImageResource.TryResolve(url.Replace("/__local_image?", "/__local_image/other?"), Endpoint, "GET", true, out _, out _));
            Assert.IsFalse(LocalImageResource.TryResolve(url + "&src=other.png", Endpoint, "GET", true, out _, out _));
            Assert.IsFalse(LocalImageResource.TryResolve(Endpoint, Endpoint, "GET", true, out _, out _));
        }
    }
}
