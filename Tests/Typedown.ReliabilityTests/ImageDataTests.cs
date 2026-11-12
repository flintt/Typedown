using System;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Typedown.Core.Utilities;

namespace Typedown.ReliabilityTests
{
    /// <summary>
    /// A picture written into the document as a data: address (#52, Embed as base64) and read back out of one: the kind
    /// comes from the bytes, and what goes in comes out the same.
    /// </summary>
    [TestClass]
    public class ImageDataTests
    {
        private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52 };
        private static readonly byte[] Jpeg = { 0xFF, 0xD8, 0xFF, 0xE1, 0, 0x10, 0x45, 0x78, 0x69, 0x66 };

        [TestMethod]
        public void APictureGoesInAndComesOutTheSame()
        {
            foreach (var (bytes, type, ext) in new[] { (Png, "image/png", "png"), (Jpeg, "image/jpeg", "jpg"), (Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'/>"), "image/svg+xml", "svg") })
            {
                var uri = ImageData.ToDataUri(bytes);
                StringAssert.StartsWith(uri, $"data:{type};base64,");
                Assert.IsTrue(ImageData.IsDataUri(uri));
                Assert.IsTrue(ImageData.TryParseDataUri(uri, out var back, out var extension), uri);
                CollectionAssert.AreEqual(bytes, back);
                Assert.AreEqual(ext, extension);
            }
        }

        [TestMethod]
        public void BytesThatSayNothingAreAPng()
        {
            StringAssert.StartsWith(ImageData.ToDataUri(new byte[] { 1, 2, 3, 4 }), "data:image/png;base64,");
        }

        [TestMethod]
        public void AHandWrittenOneIsReadToo()
        {
            // Broken over lines, as some tools write it; an svg written out rather than in base64.
            var encoded = Convert.ToBase64String(Png);
            var broken = "data:image/png;base64," + encoded.Substring(0, 8) + "\n  " + encoded.Substring(8);
            Assert.IsTrue(ImageData.TryParseDataUri(broken, out var bytes, out _));
            CollectionAssert.AreEqual(Png, bytes);
            Assert.IsTrue(ImageData.TryParseDataUri("data:image/svg+xml;charset=utf-8,%3Csvg%2F%3E", out var svg, out var ext));
            Assert.AreEqual("<svg/>", Encoding.UTF8.GetString(svg));
            Assert.AreEqual("svg", ext);
        }

        [TestMethod]
        public void WhatIsNotAPictureIsNotRead()
        {
            foreach (var uri in new[] { "data:text/html;base64,PGI+", "data:image/png;base64,***", "data:image/png;base64,", "https://example.com/a.png", "", null, "data:image/x.y z;base64,AAAA" })
                Assert.IsFalse(ImageData.TryParseDataUri(uri, out _, out _), uri ?? "null");
            Assert.IsFalse(ImageData.IsDataUri("images/a.png"));
        }

        [TestMethod]
        public void AnEmbeddedPictureIsShownAsItsStartKindAndSize()
        {
            // As source mode folds it (services/dataUriFold.ts): the first 8 characters of the encoding, then [KIND · size].
            var bytes = new byte[3000];
            Array.Copy(Png, bytes, Png.Length);
            var uri = ImageData.ToDataUri(bytes);
            Assert.AreEqual("data:image/png;base64," + uri.Substring(22, 8) + "…[PNG · 3 KB]", ImageData.ShortLabel(uri));
            Assert.AreEqual("…[JPEG · 10 B]", ImageData.ShortLabel(ImageData.ToDataUri(Jpeg))![^14..]);
            Assert.IsNull(ImageData.ShortLabel("images/a.png"));
            Assert.AreEqual("1.5 MB", ImageData.SizeLabel(1536 * 1024));
        }

        [TestMethod]
        public void TheKindComesFromTheBytes()
        {
            Assert.AreEqual("png", ImageData.Sniff(Png, null));
            Assert.AreEqual("jpg", ImageData.Sniff(Jpeg, null));
            Assert.AreEqual("gif", ImageData.Sniff(Encoding.ASCII.GetBytes("GIF89a....."), null));
            Assert.AreEqual("webp", ImageData.Sniff(Encoding.ASCII.GetBytes("RIFF\0\0\0\0WEBPVP8 "), null));
            Assert.IsNull(ImageData.Sniff(Encoding.ASCII.GetBytes("<html>"), null));
        }
    }
}
