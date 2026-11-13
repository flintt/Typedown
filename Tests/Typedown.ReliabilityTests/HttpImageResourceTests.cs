using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Typedown.Core.Utilities;

namespace Typedown.ReliabilityTests
{
    [TestClass]
    public class HttpImageResourceTests
    {
        private sealed class Replies : HttpMessageHandler
        {
            public readonly Queue<HttpResponseMessage> Responses = new();
            public readonly List<Uri> Requests = new();
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                Requests.Add(request.RequestUri);
                return Task.FromResult(Responses.Dequeue());
            }
        }

        [TestMethod]
        public async Task ReadsImagesWithoutCorsAndFollowsOnlyWebRedirects()
        {
            var handler = new Replies();
            using var http = new HttpClient(handler);
            handler.Responses.Enqueue(new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://cdn.example/picture") } });
            var image = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 1, 2, 3 }) };
            image.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            handler.Responses.Enqueue(image);
            var result = await HttpImageResource.ReadAsync("http://images.example/picture", http);
            Assert.IsTrue(result.HasValue);
            Assert.AreEqual("image/png", result.Value.ContentType);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, result.Value.Bytes);
            Assert.AreEqual(2, handler.Requests.Count);
        }

        [DataTestMethod]
        [DataRow("file:///C:/secret.png")]
        [DataRow("http://user:password@example.com/a.png")]
        [DataRow("ftp://example.com/a.png")]
        [DataRow("https://example.com/a.png")]
        public async Task RejectsNonHttpEntryPointsBeforeMakingAnyRequest(string address)
        {
            var handler = new Replies();
            using var http = new HttpClient(handler);
            Assert.IsNull(await HttpImageResource.ReadAsync(address, http));
            Assert.AreEqual(0, handler.Requests.Count);
        }

        [TestMethod]
        public async Task RejectsNonImageResponsesOversizedBodiesAndFileRedirects()
        {
            var handler = new Replies();
            using var http = new HttpClient(handler);
            handler.Responses.Enqueue(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("not an image") });
            Assert.IsNull(await HttpImageResource.ReadAsync("http://images.example/secret", http));
            var large = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 1 }) };
            large.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            large.Content.Headers.ContentLength = HttpImageResource.MaxBytes + 1L;
            handler.Responses.Enqueue(large);
            Assert.IsNull(await HttpImageResource.ReadAsync("http://images.example/large.png", http));
            handler.Responses.Enqueue(new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri("file:///C:/secret.png") } });
            Assert.IsNull(await HttpImageResource.ReadAsync("http://images.example/redirect", http));
            Assert.AreEqual(3, handler.Requests.Count);
        }
    }
}
