using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.ClientCustomerLookupExample;
using ParityBench.NET.TestEndpoints.ClientCustomerLookup;

namespace ParityBench.NET.TestEndpoints.Tests;

[TestClass]
public sealed class ClientCustomerLookupEndpointTests
{
    private WebApplicationFactory<Program> factory = null!;
    private HttpClient client = null!;

    [TestInitialize]
    public void SetUp()
    {
        factory = new WebApplicationFactory<Program>();
        client = factory.CreateClient();
    }

    [TestCleanup]
    public void TearDown()
    {
        client.Dispose();
        factory.Dispose();
    }

    [TestMethod]
    public async Task SoapEndpoint_WhenSoapActionIsProvided_ReturnsSoapResponse()
    {
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "/client/customer-lookup/soap")
        {
            Content = new StringContent(CreateSoapRequest(), Encoding.UTF8, "text/xml"),
        };
        request.Headers.Add("SOAPAction", ClientCustomerLookupEndpoints.SoapAction);

        using HttpResponseMessage response = await client.SendAsync(request);
        string body = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        StringAssert.Contains(body, "<CustomerName>Riley Morgan</CustomerName>");
    }

    [TestMethod]
    public async Task PrimaryTokenEndpoint_WhenSubscriptionKeyIsMissing_ReturnsUnauthorized()
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/client/token/primary",
            new { username = "demo-user", password = "demo-password", customerId = "2001" });

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task FinalTokenEndpoint_WhenSubscriptionKeyIsProvided_ReturnsFinalToken()
    {
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "/client/token/final")
        {
            Content = JsonContent.Create(new
            {
                primaryToken = ClientCustomerLookupEndpoints.PrimaryToken,
                customerId = "2001",
                correlationId = "trace-2001",
            }),
        };
        request.Headers.Add(
            ClientCustomerLookupTokenProvider.SubscriptionKeyHeaderName,
            ClientCustomerLookupEndpoints.FinalTokenSubscriptionKey);

        using HttpResponseMessage response = await client.SendAsync(request);
        TokenResponse? token = await response.Content.ReadFromJsonAsync<TokenResponse>();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(ClientCustomerLookupEndpoints.FinalToken, token?.AccessToken);
    }

    [TestMethod]
    public async Task JsonEndpoint_WhenBearerTokenIsMissing_ReturnsUnauthorized()
    {
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "/client/customer-lookup/json")
        {
            Content = JsonContent.Create(new ClientCustomerLookupJsonRequest
            {
                CustomerId = "2001",
                CorrelationId = "trace-2001",
            }),
        };
        request.Headers.Add(
            ClientCustomerLookupTokenProvider.SubscriptionKeyHeaderName,
            ClientCustomerLookupEndpoints.EndpointBSubscriptionKey);

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task JsonEndpoint_WhenHeadersAreValid_ReturnsCompatibleJsonResponse()
    {
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "/client/customer-lookup/json")
        {
            Content = JsonContent.Create(new ClientCustomerLookupJsonRequest
            {
                CustomerId = "2001",
                CorrelationId = "trace-2001",
            }),
        };
        request.Headers.Add(
            ClientCustomerLookupTokenProvider.SubscriptionKeyHeaderName,
            ClientCustomerLookupEndpoints.EndpointBSubscriptionKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ClientCustomerLookupEndpoints.FinalToken);

        using HttpResponseMessage response = await client.SendAsync(request);
        ClientCustomerLookupJsonResponse? body = await response.Content.ReadFromJsonAsync<ClientCustomerLookupJsonResponse>();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("OK", body?.ResultCode);
        Assert.AreEqual("Riley Morgan", body?.CustomerName);
        Assert.AreEqual("trace-2001", body?.TraceId);
    }

    private static string CreateSoapRequest() =>
        "<Envelope><Body><LookupRequest><UserName>demo-user</UserName><Password>demo-password</Password><CustomerId>2001</CustomerId><CorrelationId>trace-2001</CorrelationId></LookupRequest></Body></Envelope>";

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; init; } = string.Empty;
    }
}
