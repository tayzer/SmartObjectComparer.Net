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
        using HttpRequestMessage request = CreateValidJsonRequest("2001", "trace-2001");

        using HttpResponseMessage response = await client.SendAsync(request);
        ClientCustomerLookupJsonResponse? body = await response.Content.ReadFromJsonAsync<ClientCustomerLookupJsonResponse>();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("OK", body?.Details.ResultCode);
        Assert.AreEqual("trace-2001", body?.Details.TraceId);
        Assert.AreEqual("Riley Morgan", body?.Applicants.FirstOrDefault()?.Profile.FullName);
        Assert.AreEqual("identity", body?.Applicants.FirstOrDefault()?.RuleEvaluations.FirstOrDefault()?.RuleSet);
        Assert.AreEqual("ID_DOC_MATCH", body?.Applicants.FirstOrDefault()?.RuleEvaluations.FirstOrDefault()?.Outcomes.FirstOrDefault()?.Code);
        Assert.IsFalse(body?.IsAThing ?? false);
    }


    [TestMethod]
    public async Task Endpoints_WhenDifferenceScenarioIsRequested_ReturnDifferentCustomerNames()
    {
        using HttpRequestMessage soapRequest = new HttpRequestMessage(HttpMethod.Post, "/client/customer-lookup/soap")
        {
            Content = new StringContent(CreateSoapRequest("2002", "trace-2002"), Encoding.UTF8, "text/xml"),
        };
        soapRequest.Headers.Add("SOAPAction", ClientCustomerLookupEndpoints.SoapAction);

        using HttpResponseMessage soapResponse = await client.SendAsync(soapRequest);
        string soapBody = await soapResponse.Content.ReadAsStringAsync();

        using HttpRequestMessage jsonRequest = CreateValidJsonRequest("2002", "trace-2002");
        using HttpResponseMessage jsonResponse = await client.SendAsync(jsonRequest);
        ClientCustomerLookupJsonResponse? jsonBody = await jsonResponse.Content.ReadFromJsonAsync<ClientCustomerLookupJsonResponse>();

        Assert.AreEqual(HttpStatusCode.OK, soapResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, jsonResponse.StatusCode);
        StringAssert.Contains(soapBody, "<CustomerName>Riley Morgan</CustomerName>");
        Assert.AreEqual("Riley Morgan Updated", jsonBody?.Applicants.FirstOrDefault()?.Profile.FullName);
        Assert.AreEqual("FAIL", jsonBody?.Applicants.FirstOrDefault()?.RuleEvaluations.LastOrDefault()?.Outcomes.FirstOrDefault()?.Result);
    }
    [TestMethod]
    public async Task JsonEndpoint_WhenAddressOrderOnlyCategory_ReturnsSameAddressesInDifferentOrder()
    {
        using HttpRequestMessage request = CreateValidJsonRequest("3008", "trace-3008");

        using HttpResponseMessage response = await client.SendAsync(request);
        ClientCustomerLookupJsonResponse? body = await response.Content.ReadFromJsonAsync<ClientCustomerLookupJsonResponse>();

        string[] cities = body!.Applicants.First().Profile.Addresses.Select(address => address.City).ToArray();
        CollectionAssert.AreEquivalent(new[] { "Seattle", "Tacoma" }, cities);
        CollectionAssert.AreNotEqual(new[] { "Seattle", "Tacoma" }, cities);
    }

    [TestMethod]
    public async Task JsonEndpoint_WhenTriggeredChecksOrderOnlyCategory_ReturnsSameChecksInDifferentOrder()
    {
        using HttpRequestMessage request = CreateValidJsonRequest("3009", "trace-3009");

        using HttpResponseMessage response = await client.SendAsync(request);
        ClientCustomerLookupJsonResponse? body = await response.Content.ReadFromJsonAsync<ClientCustomerLookupJsonResponse>();

        string[] fraudChecks = body!.Applicants.First().RuleEvaluations
            .Single(evaluation => evaluation.RuleSet == "fraud")
            .Outcomes.First().TriggeredChecks;
        CollectionAssert.AreEquivalent(new[] { "ip_velocity", "device_reuse" }, fraudChecks);
        CollectionAssert.AreNotEqual(new[] { "ip_velocity", "device_reuse" }, fraudChecks);
    }

    [TestMethod]
    public async Task JsonEndpoint_WhenNameDiffOnlyCategory_ChangesOnlyFullName()
    {
        using HttpRequestMessage request = CreateValidJsonRequest("3001", "trace-3001");

        using HttpResponseMessage response = await client.SendAsync(request);
        ClientCustomerLookupJsonResponse? body = await response.Content.ReadFromJsonAsync<ClientCustomerLookupJsonResponse>();

        Assert.AreEqual("Riley Morgan Updated", body?.Applicants.First().Profile.FullName);
        Assert.AreEqual("Tacoma", body?.Applicants.First().Profile.Addresses.Last().City);
        Assert.AreEqual("REVIEW", body?.Applicants.First().RuleEvaluations.Last().Outcomes.First().Result);
        CollectionAssert.AreEqual(
            new[] { "KYC_COMPLETE", "MANUAL_REVIEW_REQUIRED" },
            body?.Applicants.First().Flags);
    }

    [TestMethod]
    public async Task JsonEndpoint_WhenCityDiffOnlyCategory_ChangesOnlyMailingCity()
    {
        using HttpRequestMessage request = CreateValidJsonRequest("3002", "trace-3002");

        using HttpResponseMessage response = await client.SendAsync(request);
        ClientCustomerLookupJsonResponse? body = await response.Content.ReadFromJsonAsync<ClientCustomerLookupJsonResponse>();

        Assert.AreEqual("Riley Morgan", body?.Applicants.First().Profile.FullName);
        Assert.AreEqual("Bellevue", body?.Applicants.First().Profile.Addresses.Last().City);
    }

    [TestMethod]
    public async Task JsonEndpoint_WhenFraudResultDiffOnlyCategory_ChangesOnlyFraudResult()
    {
        using HttpRequestMessage request = CreateValidJsonRequest("3003", "trace-3003");

        using HttpResponseMessage response = await client.SendAsync(request);
        ClientCustomerLookupJsonResponse? body = await response.Content.ReadFromJsonAsync<ClientCustomerLookupJsonResponse>();

        Assert.AreEqual("Riley Morgan", body?.Applicants.First().Profile.FullName);
        Assert.AreEqual("FAIL", body?.Applicants.First().RuleEvaluations.Last().Outcomes.First().Result);
    }

    [TestMethod]
    public async Task JsonEndpoint_WhenFlagsDiffOnlyCategory_ChangesOnlyFlags()
    {
        using HttpRequestMessage request = CreateValidJsonRequest("3004", "trace-3004");

        using HttpResponseMessage response = await client.SendAsync(request);
        ClientCustomerLookupJsonResponse? body = await response.Content.ReadFromJsonAsync<ClientCustomerLookupJsonResponse>();

        Assert.AreEqual("Riley Morgan", body?.Applicants.First().Profile.FullName);
        CollectionAssert.AreEqual(new[] { "KYC_COMPLETE", "FRAUD_ALERT" }, body?.Applicants.First().Flags);
    }

    [TestMethod]
    public async Task JsonEndpoint_WhenIgnoredFieldsOnlyCategory_MatchesBaselineApplicantData()
    {
        using HttpRequestMessage request = CreateValidJsonRequest("3007", "trace-3007");

        using HttpResponseMessage response = await client.SendAsync(request);
        ClientCustomerLookupJsonResponse? body = await response.Content.ReadFromJsonAsync<ClientCustomerLookupJsonResponse>();

        Assert.AreEqual("trace-3007", body?.Details.TraceId);
        Assert.AreEqual("Riley Morgan", body?.Applicants.First().Profile.FullName);
        CollectionAssert.AreEqual(
            new[] { "Seattle", "Tacoma" },
            body?.Applicants.First().Profile.Addresses.Select(address => address.City).ToArray());
    }

    private static string CreateSoapRequest() => CreateSoapRequest("2001", "trace-2001");

    private static string CreateSoapRequest(string customerId, string correlationId) =>
        $"<Envelope><Body><LookupRequest><UserName>demo-user</UserName><Password>demo-password</Password><CustomerId>{customerId}</CustomerId><CorrelationId>{correlationId}</CorrelationId></LookupRequest></Body></Envelope>";

    private static HttpRequestMessage CreateValidJsonRequest(string customerId, string correlationId)
    {
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "/client/customer-lookup/json")
        {
            Content = JsonContent.Create(new ClientCustomerLookupJsonRequest
            {
                CustomerId = customerId,
                CorrelationId = correlationId,
            }),
        };
        request.Headers.Add(
            ClientCustomerLookupTokenProvider.SubscriptionKeyHeaderName,
            ClientCustomerLookupEndpoints.EndpointBSubscriptionKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ClientCustomerLookupEndpoints.FinalToken);
        return request;
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; init; } = string.Empty;
    }
}
