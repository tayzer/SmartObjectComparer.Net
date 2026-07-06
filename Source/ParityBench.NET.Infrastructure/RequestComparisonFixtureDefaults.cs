using ParityBench.NET.Application.Workflow;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;

namespace ParityBench.NET.Infrastructure;

public static class RequestComparisonFixtureDefaults
{
    public const string DefaultFixtureBaseUrl = "http://localhost:5056";

    public static void Register(
        IRequestComparisonEndpointRegistry endpointRegistry,
        IRequestComparisonPresetRegistry presetRegistry,
        string? fixtureBaseUrl = null)
    {
        ArgumentNullException.ThrowIfNull(endpointRegistry);
        ArgumentNullException.ThrowIfNull(presetRegistry);

        Uri baseUri = CreateBaseUri(fixtureBaseUrl);
        RegisterEndpoints(endpointRegistry, baseUri);
        RegisterPresets(presetRegistry, baseUri);
    }

    private static void RegisterEndpoints(
        IRequestComparisonEndpointRegistry endpointRegistry,
        Uri baseUri)
    {
        endpointRegistry.Register(new EndpointOption("consumer-report/soap/a", "Consumer Report SOAP A", CreateUri(baseUri, "consumer-report/soap/a")));
        endpointRegistry.Register(new EndpointOption("consumer-report/soap/b", "Consumer Report SOAP B", CreateUri(baseUri, "consumer-report/soap/b")));
        endpointRegistry.Register(new EndpointOption("consumer-report/json/a", "Consumer Report JSON A", CreateUri(baseUri, "consumer-report/json/a")));
        endpointRegistry.Register(new EndpointOption("consumer-report/json/b", "Consumer Report JSON B", CreateUri(baseUri, "consumer-report/json/b")));
        endpointRegistry.Register(new EndpointOption("sample/customer-lookup/soap", "Sample Customer Lookup SOAP", CreateUri(baseUri, "sample/customer-lookup/soap/a")));
        endpointRegistry.Register(new EndpointOption("sample/customer-lookup/json", "Sample Customer Lookup JSON", CreateUri(baseUri, "sample/customer-lookup/json/b")));
    }

    private static void RegisterPresets(
        IRequestComparisonPresetRegistry presetRegistry,
        Uri baseUri)
    {
        string manualRunRoot = FindManualRunRoot();

        presetRegistry.Register(new RequestComparisonPresetOption(
            "xml-xml-consumer-report",
            "XML/XML consumer report",
            Path.Combine(manualRunRoot, "xml-xml"),
            CreateUri(baseUri, "consumer-report/soap/a"),
            CreateUri(baseUri, "consumer-report/soap/b"),
            ConsumerReportFixtureResponseModelRegistration.SoapModelName,
            null,
            new ComparisonOptions(
                ignoreCollectionOrder: true,
                ignoreStringCase: true,
                ignoreTrailingWhitespaceAtEnd: true,
                ignoreXmlNamespaces: true,
                smartIgnoreRules: CreateConsumerReportSmartIgnoreRules(),
                maskRules: new[] { new MaskRuleDefinition("Body.ConsumerReportResponse.Subject.NationalIdentifier", 4) }),
            new RequestExecutionOptions()));

        presetRegistry.Register(new RequestComparisonPresetOption(
            "json-json-consumer-report",
            "JSON/JSON consumer report",
            Path.Combine(manualRunRoot, "json-json"),
            CreateUri(baseUri, "consumer-report/json/a"),
            CreateUri(baseUri, "consumer-report/json/b"),
            ConsumerReportFixtureResponseModelRegistration.JsonModelName,
            null,
            new ComparisonOptions(
                ignoreCollectionOrder: true,
                ignoreStringCase: true,
                ignoreTrailingWhitespaceAtEnd: true,
                treatNullAndEmptyCollectionsAsEqual: true,
                ignoreXmlNamespaces: true,
                smartIgnoreRules: CreateConsumerReportSmartIgnoreRules(),
                maskRules: new[] { new MaskRuleDefinition("Subject.NationalIdentifier", 4) }),
            new RequestExecutionOptions()));

        presetRegistry.Register(new RequestComparisonPresetOption(
            "xml-json-sample-customer-lookup",
            "XML/JSON sample customer lookup",
            Path.Combine(manualRunRoot, "xml-json"),
            CreateUri(baseUri, "sample/customer-lookup/soap/a"),
            CreateUri(baseUri, "sample/customer-lookup/json/b"),
            BuiltInContractProfiles.SampleModelName,
            BuiltInContractProfiles.SampleProfileId,
            new ComparisonOptions(
                ignoreXmlNamespaces: true,
                maskRules: new[] { new MaskRuleDefinition("Body.CustomerLookupResponse.SensitiveToken", 4) }),
            new RequestExecutionOptions()));
    }

    private static string FindManualRunRoot()
    {
        foreach (string candidateRoot in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            DirectoryInfo? directory = new DirectoryInfo(candidateRoot);
            while (directory is not null)
            {
                string manualRunRoot = Path.Combine(directory.FullName, "Examples", "ParityBench.NET.ManualRuns");
                if (Directory.Exists(manualRunRoot))
                {
                    return manualRunRoot;
                }

                directory = directory.Parent;
            }
        }

        return Path.Combine("Examples", "ParityBench.NET.ManualRuns");
    }
    private static IReadOnlyList<SmartIgnoreRuleDefinition> CreateConsumerReportSmartIgnoreRules() =>
        new[]
        {
            new SmartIgnoreRuleDefinition(SmartIgnoreRuleKind.PropertyName, "ReportId"),
            new SmartIgnoreRuleDefinition(SmartIgnoreRuleKind.PropertyName, "GeneratedAt"),
            new SmartIgnoreRuleDefinition(SmartIgnoreRuleKind.PropertyName, "ProviderTraceId"),
            new SmartIgnoreRuleDefinition(SmartIgnoreRuleKind.PropertyName, "ProcessingMilliseconds"),
            new SmartIgnoreRuleDefinition(SmartIgnoreRuleKind.PropertyName, "SourceSystem"),
        };

    private static Uri CreateBaseUri(string? fixtureBaseUrl)
    {
        string value = string.IsNullOrWhiteSpace(fixtureBaseUrl) ? DefaultFixtureBaseUrl : fixtureBaseUrl.Trim();
        return Uri.TryCreate(value.EndsWith("/", StringComparison.Ordinal) ? value : value + "/", UriKind.Absolute, out Uri? uri)
            ? uri
            : throw new InvalidOperationException($"Fixture base URL '{value}' is not an absolute URL.");
    }

    private static Uri CreateUri(Uri baseUri, string relativePath) => new Uri(baseUri, relativePath);
}
