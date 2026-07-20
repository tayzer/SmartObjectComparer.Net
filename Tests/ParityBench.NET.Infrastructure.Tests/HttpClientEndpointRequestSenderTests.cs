using System.Net;
using System.Text;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Application.Requests;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Infrastructure;

namespace ParityBench.NET.Infrastructure.Tests;

[TestClass]
public sealed class HttpClientEndpointRequestSenderTests
{
    [TestMethod]
    public async Task SendAsync_WhenRequestIsSent_UsesPostAndConfiguredHeaders()
    {
        CapturingHandler handler = new CapturingHandler();
        HttpClientEndpointRequestSender sender = new HttpClientEndpointRequestSender(new HttpClient(handler));
        EndpointRequest request = CreateRequest(new Dictionary<string, string> { ["X-Test"] = "value" });

        await using EndpointResponse response = await sender.SendAsync(request);

        Assert.AreEqual(HttpMethod.Post, handler.CapturedRequest?.Method);
        Assert.AreEqual(new Uri("https://service.example.test/compare"), handler.CapturedRequest?.RequestUri);
        Assert.AreEqual("value", handler.CapturedRequest?.Headers.GetValues("X-Test").Single());
        Assert.AreEqual("{\"id\":1}", handler.CapturedRequestBody);
    }

    [TestMethod]
    public async Task SendAsync_WhenResponseIsReturned_ExposesStatusContentTypeAndReadableStream()
    {
        CapturingHandler handler = new CapturingHandler("hello", HttpStatusCode.Accepted, "text/plain");
        HttpClientEndpointRequestSender sender = new HttpClientEndpointRequestSender(new HttpClient(handler));

        await using EndpointResponse response = await sender.SendAsync(CreateRequest());
        using StreamReader reader = new StreamReader(response.Body, Encoding.UTF8);
        string body = await reader.ReadToEndAsync();

        Assert.AreEqual(202, response.StatusCode);
        Assert.IsTrue(response.ContentType?.StartsWith("text/plain", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual("hello", body);
    }

    private static EndpointRequest CreateRequest(IReadOnlyDictionary<string, string>? headers = null) =>
        new EndpointRequest(
            EndpointSlot.A,
            new EndpointDefinition(new Uri("https://service.example.test/compare")),
            new RequestItem("one.json", "application/json", 8),
            new MemoryStream(Encoding.UTF8.GetBytes("{\"id\":1}")),
            "application/json",
            TimeSpan.FromSeconds(30),
            headers ?? new Dictionary<string, string>());

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly string responseBody;
        private readonly HttpStatusCode statusCode;
        private readonly string responseContentType;

        public CapturingHandler(
            string responseBody = "{}",
            HttpStatusCode statusCode = HttpStatusCode.OK,
            string responseContentType = "application/json")
        {
            this.responseBody = responseBody;
            this.statusCode = statusCode;
            this.responseContentType = responseContentType;
        }

        public HttpRequestMessage? CapturedRequest { get; private set; }

        public string? CapturedRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CapturedRequest = request;
            CapturedRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, responseContentType),
            };
        }
    }
}
