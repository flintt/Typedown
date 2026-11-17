using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Typedown.Core.Utilities;

namespace Typedown.ReliabilityTests
{
    [TestClass]
    public class PlantUmlImageTests
    {
        [DataTestMethod]
        [DataRow("https://www.plantuml.com/plantuml/png/~h4142", "")]
        [DataRow("http://localhost:8080/diagrams/png/~h4142", "http://localhost:8080/diagrams/")]
        public void AllowsOnlyTheConfiguredDiagramEndpoint(string address, string server) =>
            Assert.IsTrue(PlantUmlImage.IsAllowed(address, server));

        [DataTestMethod]
        [DataRow("https://www.plantuml.com.evil/plantuml/png/~h4142")]
        [DataRow("https://user@www.plantuml.com/plantuml/png/~h4142")]
        [DataRow("https://www.plantuml.com/plantuml/other/~h4142")]
        [DataRow("https://www.plantuml.com/plantuml/png/~h4142?url=other")]
        [DataRow("https://www.plantuml.com/plantuml/png/../secret")]
        [DataRow("file:///C:/secret.png")]
        public void RefusesOtherOriginsPathsAndSchemes(string address) =>
            Assert.IsFalse(PlantUmlImage.IsAllowed(address, ""));

        private sealed class Reply : HttpMessageHandler
        {
            public int Calls;
            public HttpResponseMessage Response;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                Calls++;
                return Task.FromResult(Response);
            }
        }

        [TestMethod]
        public async Task RequiresOptInAndValidPngContent()
        {
            const string url = "https://www.plantuml.com/plantuml/png/~h4142";
            var reply = new Reply();
            using var http = new HttpClient(reply);
            Assert.IsNull(await PlantUmlImage.GetPngAsync(url, "", false, http));
            Assert.AreEqual(0, reply.Calls);
            var bytes = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 0 };
            reply.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            reply.Response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            Assert.AreEqual(Convert.ToBase64String(bytes), await PlantUmlImage.GetPngAsync(url, "", true, http));
            reply.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>not an image</html>") };
            reply.Response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            Assert.IsNull(await PlantUmlImage.GetPngAsync(url, "", true, http));
            reply.Response = new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://evil.example/") } };
            Assert.IsNull(await PlantUmlImage.GetPngAsync(url, "", true, http));
        }
    }
}
