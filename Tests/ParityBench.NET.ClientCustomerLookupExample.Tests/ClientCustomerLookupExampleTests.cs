using System.Net;
using System.Text;
using System.Text.Json;

using Mapster;

using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Application.ContractProfiles;
using ParityBench.NET.ClientCustomerLookupExample;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.ContractProfiles;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Infrastructure;

namespace ParityBench.NET.ClientCustomerLookupExample.Tests;

[TestClass]
public sealed class ClientCustomerLookupExampleTests
{
    [TestMethod]
    public void Create_WhenNoExternalDefaults_IncludesBaselineDefaultComparisonRules()
    {
        IContractProfile profile = CreateProfile();
        ComparisonRuleDefaults defaults = profile.DefaultComparisonRules;

        Assert.IsTrue(defaults.IgnoreXmlNamespaces);

        AssertRule(
            defaults,
            "details.traceId",
            ignoreCompletely: true,
            ignoreCollectionOrder: false);
        AssertRule(
            defaults,
            "details.decisionEngine",
            ignoreCompletely: true,
            ignoreCollectionOrder: false);
        AssertRule(
            defaults,
            "apps.profile.addresses",
            ignoreCompletely: false,
            ignoreCollectionOrder: true);
        AssertRule(
            defaults,
            "apps.ruleEvaluations.outcomes.triggeredChecks",
            ignoreCompletely: false,
            ignoreCollectionOrder: true);
    }

    [TestMethod]
    public void Mapster_WhenMappingSoapRequest_ProducesEndpointBJsonRequest()
    {
        var config = ClientCustomerLookupMapsterConfig.CreateConfig();
        var request = new ClientCustomerLookupSoapRequestEnvelope
        {
            Body = new ClientCustomerLookupSoapRequestBody
            {
                LookupRequest = new ClientCustomerLookupRequest
                {
                    CustomerId = "2001",
                    CorrelationId = "trace-2001",
                },
            },
        };

        ClientCustomerLookupJsonRequest mapped = request.Adapt<ClientCustomerLookupJsonRequest>(config);

        Assert.AreEqual("2001", mapped.CustomerId);
        Assert.AreEqual("trace-2001", mapped.CorrelationId);
    }

    [TestMethod]
    public async Task TokenProvider_WhenCalled_SendsBothSubscriptionKeysAndReturnsFinalToken()
    {
        RecordingTokenHandler handler = new RecordingTokenHandler();
        HttpClient httpClient = new HttpClient(handler);
        ClientCustomerLookupTokenProvider provider = new ClientCustomerLookupTokenProvider(
            httpClient,
            Options.Create(CreateTokenOptions()));

        ClientCustomerLookupTokenResult token = await provider.GetFinalTokenAsync(new ClientCustomerLookupRequest
        {
            UserName = "demo-user",
            Password = "demo-password",
            CustomerId = "2001",
            CorrelationId = "trace-2001",
        });

        Assert.AreEqual("final-token", token.AccessToken);
        CollectionAssert.AreEqual(
            new[] { "primary-subscription", "final-subscription" },
            handler.SubscriptionKeys.ToArray());
    }

    [TestMethod]
    public async Task PrepareRequestAsync_WhenProfileRuns_AddsBearerTokenAndJsonBody()
    {
        IContractProfile profile = ClientCustomerLookupProfileFactory.Create(
            new JsonXmlContractPayloadSerializer(),
            new FixedTokenProvider("final-token"),
            ClientCustomerLookupMapsterConfig.CreateConfig());
        byte[] requestBody = Encoding.UTF8.GetBytes(CreateSoapRequest());

        PreparedContractRequest prepared = await profile.PrepareRequestAsync(
            EndpointSlot.B,
            new ContractRequestPreparationContext(
                new RequestItem("one.xml", "text/xml", requestBody.Length),
                token => OpenBytesAsync(requestBody, token),
                PayloadFormat.Xml,
                "text/xml"));

        string json = await ReadPayloadAsStringAsync(prepared.Body);
        Assert.AreEqual("application/json", prepared.ContentType);
        Assert.AreEqual("Bearer final-token", prepared.Headers?["Authorization"]);
        StringAssert.Contains(json, "\"customerId\":\"2001\"");
        StringAssert.Contains(json, "\"correlationId\":\"trace-2001\"");
    }

    [TestMethod]
    public async Task NormalizeResponseAsync_WhenEndpointAIsSoap_ProducesCanonicalJson()
    {
        IContractProfile profile = CreateProfile();
        byte[] responseBody = Encoding.UTF8.GetBytes(
            "<Envelope><Body><LookupResponse><StatusCode>OK</StatusCode><CustomerName>Riley Morgan</CustomerName><TraceId>trace-2001</TraceId></LookupResponse></Body></Envelope>");

        NormalizedContractResponse normalized = await profile.NormalizeResponseAsync(
            EndpointSlot.A,
            new ContractResponseNormalizationContext(
                new RequestItem("one.xml", "text/xml", 1),
                EndpointSlot.A,
                token => OpenBytesAsync(responseBody, token),
                "text/xml",
                PayloadFormat.Xml));

        string json = await ReadPayloadAsStringAsync(normalized.Body);
        Assert.AreEqual("application/json", normalized.ContentType);
        StringAssert.Contains(json, "\"details\":{");
        StringAssert.Contains(json, "\"resultCode\":\"OK\"");
        StringAssert.Contains(json, "\"traceId\":\"trace-2001\"");
        StringAssert.Contains(json, "\"apps\":[{");
        StringAssert.Contains(json, "\"fullName\":\"Riley Morgan\"");
        StringAssert.Contains(json, "\"ruleEvaluations\":[");
        Assert.IsFalse(json.Contains("\"isAThing\"", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task NormalizeResponseAsync_WhenEndpointBIsJson_ProducesCanonicalJson()
    {
        IContractProfile profile = CreateProfile();
        byte[] responseBody = Encoding.UTF8.GetBytes(
            "{\"details\":{\"resultCode\":\"OK\",\"traceId\":\"trace-2001\",\"decisionEngine\":\"EndpointB-JSON-Service\"},\"apps\":[{\"applicantId\":\"trace-2001\",\"profile\":{\"fullName\":\"Riley Morgan\",\"addresses\":[{\"type\":\"HOME\",\"city\":\"Seattle\",\"country\":\"US\"}]},\"ruleEvaluations\":[{\"ruleSet\":\"identity\",\"outcomes\":[{\"code\":\"ID_DOC_MATCH\",\"result\":\"PASS\",\"triggeredChecks\":[\"name_match\"]}]}],\"flags\":[\"KYC_COMPLETE\"]}],\"isAThing\":true}");

        NormalizedContractResponse normalized = await profile.NormalizeResponseAsync(
            EndpointSlot.B,
            new ContractResponseNormalizationContext(
                new RequestItem("one.xml", "text/xml", 1),
                EndpointSlot.B,
                token => OpenBytesAsync(responseBody, token),
                "application/json",
                PayloadFormat.Json));

        string json = await ReadPayloadAsStringAsync(normalized.Body);
        Assert.AreEqual("application/json", normalized.ContentType);
        StringAssert.Contains(json, "\"details\":{");
        StringAssert.Contains(json, "\"resultCode\":\"OK\"");
        StringAssert.Contains(json, "\"traceId\":\"trace-2001\"");
        StringAssert.Contains(json, "\"apps\":[{");
        StringAssert.Contains(json, "\"fullName\":\"Riley Morgan\"");
        StringAssert.Contains(json, "\"ruleEvaluations\":[");
        Assert.IsFalse(json.Contains("\"isAThing\"", StringComparison.Ordinal));
    }

    private static IContractProfile CreateProfile() =>
        ClientCustomerLookupProfileFactory.Create(
            new JsonXmlContractPayloadSerializer(),
            new FixedTokenProvider("final-token"),
            ClientCustomerLookupMapsterConfig.CreateConfig());

    private static ClientCustomerLookupTokenOptions CreateTokenOptions() =>
        new ClientCustomerLookupTokenOptions
        {
            PrimaryTokenUrl = "https://tokens.example.test/primary",
            PrimaryTokenSubscriptionKey = "primary-subscription",
            FinalTokenUrl = "https://tokens.example.test/final",
            FinalTokenSubscriptionKey = "final-subscription",
        };

    private static string CreateSoapRequest() =>
        "<Envelope><Body><LookupRequest><UserName>demo-user</UserName><Password>demo-password</Password><CustomerId>2001</CustomerId><CorrelationId>trace-2001</CorrelationId></LookupRequest></Body></Envelope>";

    private static ValueTask<Stream> OpenBytesAsync(byte[] bytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<Stream>(new MemoryStream(bytes, writable: false));
    }

    private static async Task<string> ReadPayloadAsStringAsync(ContractPayload payload)
    {
        await using (payload)
        await using (Stream stream = await payload.OpenReadAsync().ConfigureAwait(false))
        using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
        {
            return await reader.ReadToEndAsync().ConfigureAwait(false);
        }
    }

    private static void AssertRule(
        ComparisonRuleDefaults defaults,
        string path,
        bool ignoreCompletely,
        bool ignoreCollectionOrder)
    {
        IgnoreRuleDefinition rule = defaults.IgnoreRules.Single(candidate =>
            string.Equals(candidate.PropertyPath, path, StringComparison.OrdinalIgnoreCase));

        Assert.AreEqual(ignoreCompletely, rule.IgnoreCompletely);
        Assert.AreEqual(ignoreCollectionOrder, rule.IgnoreCollectionOrder);
    }

    private sealed class FixedTokenProvider : IClientCustomerLookupTokenProvider
    {
        private readonly string token;

        public FixedTokenProvider(string token)
        {
            this.token = token;
        }

        public Task<ClientCustomerLookupTokenResult> GetFinalTokenAsync(
            ClientCustomerLookupRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ClientCustomerLookupTokenResult(token));
    }

    private sealed class RecordingTokenHandler : HttpMessageHandler
    {
        public List<string> SubscriptionKeys { get; } = new List<string>();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Headers.TryGetValues(ClientCustomerLookupTokenProvider.SubscriptionKeyHeaderName, out IEnumerable<string>? values))
            {
                SubscriptionKeys.Add(values.Single());
            }

            string token = request.RequestUri?.AbsolutePath.EndsWith("/primary", StringComparison.Ordinal) == true
                ? "primary-token"
                : "final-token";
            HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new Dictionary<string, string> { ["access_token"] = token }),
                    Encoding.UTF8,
                    "application/json"),
            };
            return Task.FromResult(response);
        }
    }
}
