using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Application.ContractProfiles;
using ParityBench.NET.Application.Requests;
using ParityBench.NET.Application.Workflow;
using ParityBench.NET.ClientCustomerLookupExample;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.ContractProfiles;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Engine;
using ParityBench.NET.Engine.Comparers;
using ParityBench.NET.Infrastructure;

namespace ParityBench.NET.Fitness.Tests;

[TestClass]
[TestCategory("Fitness")]
public sealed class ClientScenarioFitnessTests
{
    [TestMethod]
    public void ClientDefaults_WhenEndpointOptionsAreConfigured_RegistersRunnableProfilePreset()
    {
        IConfiguration configuration = CreateClientConfiguration();
        InMemoryRequestComparisonEndpointRegistry endpoints = new InMemoryRequestComparisonEndpointRegistry();
        InMemoryRequestComparisonPresetRegistry presets = new InMemoryRequestComparisonPresetRegistry();

        ClientCustomerLookupExampleDefaults.Register(endpoints, presets, configuration, Path.Combine("Examples", "ParityBench.NET.ManualRuns"));

        EndpointOption endpointA = endpoints.ListEndpoints().Single(endpoint => endpoint.EndpointId == ClientCustomerLookupProfileFactory.SuggestedEndpointAId);
        EndpointOption endpointB = endpoints.ListEndpoints().Single(endpoint => endpoint.EndpointId == ClientCustomerLookupProfileFactory.SuggestedEndpointBId);
        RequestComparisonPresetOption preset = presets.ListPresets().Single(preset => preset.PresetId == ClientCustomerLookupExampleDefaults.PresetId);

        Assert.AreEqual(new Uri("https://client.example.test/soap"), endpointA.Url);
        Assert.AreEqual("text/xml", endpointA.DefaultHeaders!["Content-Type"]);
        Assert.AreEqual("urn:ClientCustomerLookup", endpointA.DefaultHeaders["SOAPAction"]);
        Assert.AreEqual(new Uri("https://client.example.test/json"), endpointB.Url);
        Assert.AreEqual("application/json", endpointB.DefaultHeaders!["Content-Type"]);
        Assert.AreEqual("sub-key", endpointB.DefaultHeaders["Ocp-Apim-Subscription-Key"]);
        Assert.AreEqual(ClientCustomerLookupProfileFactory.ResponseModelName, preset.ModelName);
        Assert.AreEqual(ClientCustomerLookupProfileFactory.ProfileId, preset.ContractProfileId);
        CollectionAssert.Contains(preset.EndpointAHeaders!.Keys.ToArray(), "SOAPAction");
        CollectionAssert.Contains(preset.EndpointBHeaders!.Keys.ToArray(), "Ocp-Apim-Subscription-Key");
    }

    [TestMethod]
    public async Task ClientProfile_WhenSoapAndJsonResponsesNormalizeToSameCustomer_ComparesEqualWithDefaultRules()
    {
        IContractProfile profile = ClientCustomerLookupProfileFactory.Create(
            new JsonXmlContractPayloadSerializer(),
            new FixedTokenProvider("final-token"),
            ClientCustomerLookupMapsterConfig.CreateConfig());
        string normalizedA = await NormalizeResponseAsync(
            profile,
            EndpointSlot.A,
            "text/xml",
            PayloadFormat.Xml,
            "<Envelope><Body><LookupResponse><StatusCode>OK</StatusCode><CustomerName>Riley Morgan</CustomerName><TraceId>trace-2001</TraceId></LookupResponse></Body></Envelope>")
            .ConfigureAwait(false);
        string normalizedB = await NormalizeResponseAsync(
            profile,
            EndpointSlot.B,
            "application/json",
            PayloadFormat.Json,
            """
            {
              "details": {
                "resultCode": "OK",
                "traceId": "different-trace",
                "decisionEngine": "EndpointB-JSON-Service"
              },
              "apps": [
                {
                  "applicantId": "trace-2001",
                  "profile": {
                    "fullName": "Riley Morgan",
                    "addresses": [
                      { "type": "MAILING", "city": "Tacoma", "country": "US" },
                      { "type": "HOME", "city": "Seattle", "country": "US" }
                    ]
                  },
                  "ruleEvaluations": [
                    {
                      "ruleSet": "identity",
                      "outcomes": [
                        { "code": "ID_DOC_MATCH", "result": "PASS", "triggeredChecks": [ "dob_match", "name_match" ] },
                        { "code": "SANCTIONS_SCREEN", "result": "PASS", "triggeredChecks": [ "pep", "ofac" ] }
                      ]
                    },
                    {
                      "ruleSet": "fraud",
                      "outcomes": [
                        { "code": "DEVICE_RISK", "result": "REVIEW", "triggeredChecks": [ "device_reuse", "ip_velocity" ] }
                      ]
                    }
                  ],
                  "flags": [ "KYC_COMPLETE", "MANUAL_REVIEW_REQUIRED" ]
                }
              ],
              "isAThing": true
            }
            """)
            .ConfigureAwait(false);
        InMemoryArtifactStore artifactStore = new InMemoryArtifactStore(new Dictionary<string, string>
        {
            ["a"] = normalizedA,
            ["b"] = normalizedB,
        });
        ResponseModelRegistry registry = new ResponseModelRegistry();
        registry.Register<ClientCustomerLookupResponse>(ClientCustomerLookupProfileFactory.ResponseModelName);
        CompareNetObjectsResponseComparer comparer = new CompareNetObjectsResponseComparer(
            artifactStore,
            new JsonXmlResponseBodyDeserializer(registry));

        RequestPairResult result = await comparer
            .CompareAsync(
                new RequestItem("customer.xml", "text/xml", 1),
                CreateRunOptions(profile),
                CreateResponse(EndpointSlot.A, "a", normalizedA),
                CreateResponse(EndpointSlot.B, "b", normalizedB),
                null)
            .ConfigureAwait(false);

        string differences = string.Join(" | ", result.Differences.Select(difference => $"{difference.PropertyPath}:{difference.ValueA}->{difference.ValueB}"));
        Assert.AreEqual(RequestPairOutcome.Equal, result.Outcome, differences);
    }

    private static IConfiguration CreateClientConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RequestComparison:EndpointOptions:Endpoints:0:Id"] = ClientCustomerLookupProfileFactory.SuggestedEndpointAId,
                ["RequestComparison:EndpointOptions:Endpoints:0:Name"] = "Client SOAP",
                ["RequestComparison:EndpointOptions:Endpoints:0:Url"] = "https://client.example.test/soap",
                ["RequestComparison:EndpointOptions:Endpoints:0:ContentType"] = "text/xml",
                ["RequestComparison:EndpointOptions:Endpoints:0:DefaultHeaders:SOAPAction"] = "urn:ClientCustomerLookup",
                ["RequestComparison:EndpointOptions:Endpoints:1:Id"] = ClientCustomerLookupProfileFactory.SuggestedEndpointBId,
                ["RequestComparison:EndpointOptions:Endpoints:1:Name"] = "Client JSON",
                ["RequestComparison:EndpointOptions:Endpoints:1:Url"] = "https://client.example.test/json",
                ["RequestComparison:EndpointOptions:Endpoints:1:ContentType"] = "application/json",
                ["RequestComparison:EndpointOptions:Endpoints:1:DefaultHeaders:Ocp-Apim-Subscription-Key"] = "sub-key",
                ["RequestComparison:Profiles:ClientCustomerLookup:PresetId"] = ClientCustomerLookupExampleDefaults.PresetId,
                ["RequestComparison:Profiles:ClientCustomerLookup:RequestDirectory"] = "Examples/ParityBench.NET.ManualRuns/client-soap-json-token",
                ["RequestComparison:Profiles:ClientCustomerLookup:ResponseModel"] = ClientCustomerLookupProfileFactory.ResponseModelName,
                ["RequestComparison:Profiles:ClientCustomerLookup:ProfileId"] = ClientCustomerLookupProfileFactory.ProfileId,
            })
            .Build();

    private static async Task<string> NormalizeResponseAsync(
        IContractProfile profile,
        EndpointSlot endpoint,
        string contentType,
        PayloadFormat format,
        string body)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(body);
        NormalizedContractResponse normalized = await profile
            .NormalizeResponseAsync(
                endpoint,
                new ContractResponseNormalizationContext(
                    new RequestItem("customer.xml", contentType, bytes.Length),
                    endpoint,
                    token => new ValueTask<Stream>(new MemoryStream(bytes, writable: false)),
                    contentType,
                    format))
            .ConfigureAwait(false);

        await using ContractPayload payload = normalized.Body;
        await using Stream stream = await payload.OpenReadAsync().ConfigureAwait(false);
        using StreamReader reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }

    private static RunOptions CreateRunOptions(IContractProfile profile)
    {
        ComparisonRuleDefaults defaults = profile.DefaultComparisonRules;
        return new RunOptions(
            new RequestBatchReference("batch-1"),
            new EndpointDefinition(new Uri("https://a.example.test")),
            new EndpointDefinition(new Uri("https://b.example.test")),
            TimeSpan.FromSeconds(30),
            2,
            profile.ResponseModelName,
            new ComparisonOptions(
                defaults.IgnoreCollectionOrder,
                defaults.IgnoreStringCase,
                defaults.IgnoreTrailingWhitespaceAtEnd,
                defaults.TreatNullAndEmptyCollectionsAsEqual,
                defaults.IgnoreXmlNamespaces,
                ignoreRules: defaults.IgnoreRules,
                smartIgnoreRules: defaults.SmartIgnoreRules,
                maskRules: defaults.MaskRules),
            contractProfileSelection: new ContractProfileSelection(profile.ProfileId));
    }

    private static ResponseArtifactMetadata CreateResponse(EndpointSlot endpoint, string artifactId, string content)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(content);
        return new ResponseArtifactMetadata(
            endpoint,
            new ArtifactReference(artifactId, "application/json"),
            200,
            "application/json",
            bytes.Length,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
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

    private sealed class InMemoryArtifactStore : IRunArtifactStore
    {
        private readonly IReadOnlyDictionary<string, byte[]> contentByArtifactId;

        public InMemoryArtifactStore(IReadOnlyDictionary<string, string> contentByArtifactId)
        {
            this.contentByArtifactId = contentByArtifactId.ToDictionary(
                pair => pair.Key,
                pair => Encoding.UTF8.GetBytes(pair.Value),
                StringComparer.Ordinal);
        }

        public Task<ResponseArtifactMetadata> SaveResponseAsync(
            RunId runId,
            EndpointSlot endpoint,
            RequestItem request,
            int statusCode,
            string? contentType,
            Stream body,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Stream> OpenReadAsync(
            ArtifactReference artifact,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream(contentByArtifactId[artifact.ArtifactId], writable: false));
    }
}
