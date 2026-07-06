using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Application.ContractProfiles;
using ParityBench.NET.Application.Requests;
using ParityBench.NET.Application.Workflow;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.ContractProfiles;
using ParityBench.NET.Domain.Requests;

namespace ParityBench.NET.Application.Tests;

[TestClass]
public sealed class RequestComparisonDefaultsServiceTests
{
    [TestMethod]
    public async Task LoadDefaults_WhenModelsAreRegistered_ReturnsModelOptions()
    {
        FakeResponseModelRegistry modelRegistry = new FakeResponseModelRegistry("SecondModel", "FirstModel");
        RequestComparisonDefaultsService service = CreateService(modelRegistry);

        RequestComparisonDefaults defaults = await service.LoadDefaultsAsync().ConfigureAwait(false);

        CollectionAssert.AreEqual(
            new[] { "FirstModel", "SecondModel" },
            defaults.ResponseModels.Select(model => model.ModelName).ToArray());
    }

    [TestMethod]
    public async Task LoadDefaults_WhenProfileExists_ReturnsProfileDefaultsAndSuggestedEndpointIds()
    {
        FakeResponseModelRegistry modelRegistry = new FakeResponseModelRegistry("CustomerResponse");
        FakeContractProfileRegistry profileRegistry = new FakeContractProfileRegistry();
        profileRegistry.Register(new FakeContractProfile(
            "CustomerResponse",
            "customer-soap-to-json",
            "customer/soap",
            "customer/json",
            new[] { new IgnoreRuleDefinition("TraceId") }));
        RequestComparisonDefaultsService service = CreateService(modelRegistry, profileRegistry);

        RequestComparisonDefaults defaults = await service.LoadDefaultsAsync().ConfigureAwait(false);

        ContractProfileOption profile = defaults.ContractProfiles.Single();
        Assert.AreEqual("CustomerResponse", profile.ResponseModelName);
        Assert.AreEqual("customer-soap-to-json", profile.ProfileId);
        Assert.AreEqual("customer/soap", profile.EndpointASuggestedEndpointId);
        Assert.AreEqual("customer/json", profile.EndpointBSuggestedEndpointId);
        Assert.AreEqual("TraceId", profile.DefaultIgnoreRules.Single().PropertyPath);
    }

    [TestMethod]
    public async Task LoadDefaults_WhenEndpointIdsAreRegistered_ReturnsEndpointUrls()
    {
        InMemoryRequestComparisonEndpointRegistry endpointRegistry = new InMemoryRequestComparisonEndpointRegistry();
        endpointRegistry.Register(new EndpointOption("customer/soap", "Customer SOAP", new Uri("http://localhost:5056/customer/soap")));
        RequestComparisonDefaultsService service = CreateService(endpointRegistry: endpointRegistry);

        RequestComparisonDefaults defaults = await service.LoadDefaultsAsync().ConfigureAwait(false);

        EndpointOption endpoint = defaults.Endpoints.Single();
        Assert.AreEqual("customer/soap", endpoint.EndpointId);
        Assert.AreEqual("Customer SOAP", endpoint.Label);
        Assert.AreEqual(new Uri("http://localhost:5056/customer/soap"), endpoint.Url);
    }

    [TestMethod]
    public async Task LoadDefaults_WhenManualPresetsAreRegistered_ReturnsExamplePresets()
    {
        InMemoryRequestComparisonPresetRegistry presetRegistry = new InMemoryRequestComparisonPresetRegistry();
        presetRegistry.Register(new RequestComparisonPresetOption(
            "json-json-consumer-report",
            "JSON/JSON consumer report",
            "Examples/ParityBench.NET.ManualRuns/json-json",
            new Uri("http://localhost:5056/consumer-report/json/a"),
            new Uri("http://localhost:5056/consumer-report/json/b"),
            "ConsumerReportJsonResponse",
            null,
            new ComparisonOptions(ignoreStringCase: true),
            new RequestExecutionOptions("application/json")));
        RequestComparisonDefaultsService service = CreateService(presetRegistry: presetRegistry);

        RequestComparisonDefaults defaults = await service.LoadDefaultsAsync().ConfigureAwait(false);

        RequestComparisonPresetOption preset = defaults.Presets.Single();
        Assert.AreEqual("json-json-consumer-report", preset.PresetId);
        Assert.AreEqual("ConsumerReportJsonResponse", preset.ModelName);
        Assert.AreEqual("application/json", preset.RequestExecutionOptions.ContentTypeOverride);
        Assert.IsTrue(preset.ComparisonOptions.IgnoreStringCase);
    }

    private static RequestComparisonDefaultsService CreateService(
        IResponseModelRegistry? modelRegistry = null,
        IContractProfileRegistry? profileRegistry = null,
        IRequestComparisonEndpointRegistry? endpointRegistry = null,
        IRequestComparisonPresetRegistry? presetRegistry = null) =>
        new RequestComparisonDefaultsService(
            modelRegistry ?? new FakeResponseModelRegistry(),
            profileRegistry ?? new FakeContractProfileRegistry(),
            endpointRegistry ?? new InMemoryRequestComparisonEndpointRegistry(),
            presetRegistry ?? new InMemoryRequestComparisonPresetRegistry());

    private sealed class FakeResponseModelRegistry : IResponseModelRegistry
    {
        private readonly IReadOnlyList<string> modelNames;

        public FakeResponseModelRegistry(params string[] modelNames)
        {
            this.modelNames = modelNames;
        }

        public void Register<T>(string modelName) where T : class
        {
        }

        public Type Resolve(string modelName) => typeof(object);

        public IReadOnlyList<string> ListModelNames() => modelNames;
    }

    private sealed class FakeContractProfileRegistry : IContractProfileRegistry
    {
        private readonly Dictionary<string, IContractProfile> profilesById = new Dictionary<string, IContractProfile>(StringComparer.Ordinal);

        public void Register(IContractProfile profile) => profilesById[profile.ProfileId] = profile;

        public IContractProfile Resolve(string responseModelName, ContractProfileSelection? selection = null)
        {
            Assert.IsNotNull(selection);
            return profilesById[selection!.ProfileId];
        }

        public bool TryResolve(
            string responseModelName,
            ContractProfileSelection? selection,
            out IContractProfile? profile,
            out string? errorMessage)
        {
            profile = null;
            errorMessage = null;
            if (selection is null)
            {
                return false;
            }

            return profilesById.TryGetValue(selection.ProfileId, out profile);
        }

        public IReadOnlyList<string> GetProfileIds(string responseModelName) =>
            profilesById.Values
                .Where(profile => string.Equals(profile.ResponseModelName, responseModelName, StringComparison.Ordinal))
                .Select(profile => profile.ProfileId)
                .ToArray();
    }

    private sealed class FakeContractProfile : IContractProfile
    {
        public FakeContractProfile(
            string responseModelName,
            string profileId,
            string suggestedEndpointAId,
            string suggestedEndpointBId,
            IReadOnlyList<IgnoreRuleDefinition> defaultIgnoreRules)
        {
            ResponseModelName = responseModelName;
            ProfileId = profileId;
            EndpointA = new ContractEndpointProfile(PayloadFormat.Xml, "application/xml", PayloadFormat.Xml, suggestedEndpointAId);
            EndpointB = new ContractEndpointProfile(PayloadFormat.Json, "application/json", PayloadFormat.Json, suggestedEndpointBId);
            DefaultIgnoreRules = defaultIgnoreRules;
        }

        public string ProfileId { get; }

        public string ResponseModelName { get; }

        public string? ProfileVersion => "1";

        public Type EndpointARequestType => typeof(object);

        public Type EndpointBRequestType => typeof(object);

        public Type CanonicalResponseType => typeof(object);

        public Type EndpointBResponseType => typeof(object);

        public ContractEndpointProfile EndpointA { get; }

        public ContractEndpointProfile EndpointB { get; }

        public PayloadFormat CanonicalResponseFormat => PayloadFormat.Json;

        public string CanonicalResponseContentType => "application/json";

        public IReadOnlyList<IgnoreRuleDefinition> DefaultIgnoreRules { get; }

        public IReadOnlyDictionary<string, string> CanonicalToEndpointResponseMaskPathMap => new Dictionary<string, string>();

        public ValueTask<PreparedContractRequest> PrepareRequestAsync(
            EndpointSlot endpoint,
            ContractRequestPreparationContext context,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<NormalizedContractResponse> NormalizeResponseAsync(
            EndpointSlot endpoint,
            ContractResponseNormalizationContext context,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
