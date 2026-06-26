using System.Text;
using System.Xml.Serialization;
using ComparisonTool.Core.DI;
using ComparisonTool.Core.RequestComparison.AlternateContracts;
using ComparisonTool.Core.RequestComparison.Models;
using ComparisonTool.Core.RequestComparison.Services;
using ComparisonTool.Core.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace ComparisonTool.Tests.Unit.RequestComparison;

[TestClass]
public class RequestComparisonAlternateContractTransformationServiceTests : IDisposable
{
    private readonly List<string> createdPaths = new();

    public void Dispose()
    {
        foreach (var path in createdPaths)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [TestMethod]
    public void Registry_ResolvesSingleProfileForCanonicalModel()
    {
        using var serviceProvider = CreateServiceProvider();
        var registry = serviceProvider.GetRequiredService<IRequestComparisonAlternateContractProfileRegistry>();

        var profile = registry.Resolve("CanonicalResponse");

        profile.ProfileId.ShouldBe("alt-json");
        profile.CanonicalRequestType.ShouldBe(typeof(CanonicalRequest));
        profile.AlternateResponseType.ShouldBe(typeof(AlternateResponse));
    }

    [TestMethod]
    public async Task PrepareEndpointBRequestAsync_TransformsCanonicalXmlIntoAlternateJson()
    {
        using var serviceProvider = CreateServiceProvider();
        var transformationService = serviceProvider.GetRequiredService<RequestComparisonAlternateContractTransformationService>();
        var job = CreateJob();
        var request = new RequestFileInfo
        {
            RelativePath = "sample.xml",
            FilePath = "sample.xml",
            ContentType = "application/xml",
            DetectedFormat = SerializationFormat.Xml,
            Headers = new Dictionary<string, string>(),
            FileSize = 0,
        };

        var requestBody = Encoding.UTF8.GetBytes("<CanonicalRequest><Id>42</Id><Name>Alpha</Name></CanonicalRequest>");

        var preparedRequest = await transformationService.PrepareEndpointBRequestAsync(job, request, requestBody);
        var json = Encoding.UTF8.GetString(preparedRequest.Body);

        preparedRequest.ContentType.ShouldBe("application/json");
        preparedRequest.ProfileId.ShouldBe("alt-json");
        json.ShouldContain("\"RequestId\":\"42\"");
        json.ShouldContain("\"DisplayName\":\"Alpha\"");
    }

    [TestMethod]
    public async Task NormalizeEndpointBResponseAsync_MapsAlternateJsonBackToCanonicalXml()
    {
        using var serviceProvider = CreateServiceProvider();
        var transformationService = serviceProvider.GetRequiredService<RequestComparisonAlternateContractTransformationService>();
        var job = CreateJob();

        var responsePath = Path.Combine(Path.GetTempPath(), $"alt-response-{Guid.NewGuid():N}.json");
        createdPaths.Add(responsePath);
        await File.WriteAllTextAsync(responsePath, "{\"ResultCode\":\"00\",\"CustomerName\":\"Alpha\",\"Payload\":{\"RawToken\":\"ABC12345\"}}");

        var executionResult = new RequestExecutionResult
        {
            Request = new RequestFileInfo
            {
                RelativePath = "sample.xml",
                FilePath = "sample.xml",
                ContentType = "application/xml",
                DetectedFormat = SerializationFormat.Xml,
                Headers = new Dictionary<string, string>(),
                FileSize = 0,
            },
            Success = true,
            StatusCodeA = 200,
            StatusCodeB = 200,
            ResponsePathB = responsePath,
            ContentTypeB = "application/json",
            DurationMs = 10,
        };

        var normalized = await transformationService.NormalizeEndpointBResponseAsync(job, executionResult);
        var xml = Encoding.UTF8.GetString(normalized.Body);
        using var xmlStream = new MemoryStream(normalized.Body, writable: false);
        var serializer = new XmlSerializer(typeof(CanonicalResponse));
        var deserialized = (CanonicalResponse?)serializer.Deserialize(xmlStream);

        normalized.ContentType.ShouldBe("application/xml");
        normalized.Format.ShouldBe(SerializationFormat.Xml);
        xml.ShouldContain("CanonicalResponse");
        deserialized.ShouldNotBeNull();
        deserialized.Code.ShouldBe("00");
        deserialized.Name.ShouldBe("Alpha");
        deserialized.SecretToken.ShouldBe("ABC12345");
    }

    [TestMethod]
    public void GetEndpointBRawResponseMaskRules_TranslatesCanonicalPathsForAlternateResponses()
    {
        using var serviceProvider = CreateServiceProvider();
        var transformationService = serviceProvider.GetRequiredService<RequestComparisonAlternateContractTransformationService>();
        var job = CreateJob();
        job.MaskRules.Add(new MaskRuleDto
        {
            PropertyPath = "CanonicalResponse.SecretToken",
            PreserveLastCharacters = 4,
            MaskCharacter = "#",
        });

        var translatedRules = transformationService.GetEndpointBRawResponseMaskRules(job);

        translatedRules.Count.ShouldBe(1);
        translatedRules[0].PropertyPath.ShouldBe("Payload.RawToken");
        translatedRules[0].PreserveLastCharacters.ShouldBe(4);
        translatedRules[0].MaskCharacter.ShouldBe("#");
    }

    [TestMethod]
    public void GetEffectiveIgnoreRules_MergesProfileDefaultsWithRuntimeRules()
    {
        using var serviceProvider = CreateServiceProvider();
        var transformationService = serviceProvider.GetRequiredService<RequestComparisonAlternateContractTransformationService>();
        var job = CreateJob();
        job.IgnoreRules.Add(new IgnoreRuleDto
        {
            PropertyPath = "CanonicalResponse.SecretToken",
            IgnoreCompletely = true,
        });

        var effectiveRules = transformationService.GetEffectiveIgnoreRules(job);

        effectiveRules.Count.ShouldBe(2);
        effectiveRules.Any(rule => string.Equals(rule.PropertyPath, "CanonicalResponse.Name", StringComparison.Ordinal)).ShouldBeTrue();
        effectiveRules.Any(rule => string.Equals(rule.PropertyPath, "CanonicalResponse.SecretToken", StringComparison.Ordinal)).ShouldBeTrue();
    }

    [TestMethod]
    public void AddDefaultIgnoreRulesFromJson_WithExportedConfigurationBundle_LoadsRules()
    {
        var json = """
            {
              "schemaVersion": 1,
              "globalSettings": {
                "ignoreCollectionOrder": true,
                "ignoreStringCase": false,
                "ignoreTrailingWhitespaceAtEnd": false,
                "treatNullAndEmptyCollectionsAsEqual": true,
                "ignoreXmlNamespaces": true
              },
              "ignoreRules": [
                {
                  "propertyPath": "CanonicalResponse.SecretToken",
                  "ignoreCompletely": true
                },
                {
                  "propertyPath": "CanonicalResponse.Payloads",
                  "treatNullAndEmptyCollectionsAsEqual": true
                }
              ]
            }
            """;

        using var serviceProvider = CreateServiceProvider(builder => builder.AddDefaultIgnoreRulesFromJson(json));
        var registry = serviceProvider.GetRequiredService<IRequestComparisonAlternateContractProfileRegistry>();

        var profile = registry.Resolve("CanonicalResponse");

        profile.DefaultIgnoreRules.Count.ShouldBe(3);
        profile.DefaultIgnoreRules.Any(rule =>
            string.Equals(rule.PropertyPath, "CanonicalResponse.SecretToken", StringComparison.Ordinal) &&
            rule.IgnoreCompletely).ShouldBeTrue();
        profile.DefaultIgnoreRules.Any(rule =>
            string.Equals(rule.PropertyPath, "CanonicalResponse.Payloads", StringComparison.Ordinal) &&
            rule.TreatNullAndEmptyCollectionsAsEqual).ShouldBeTrue();
    }

    [TestMethod]
    public async Task AddDefaultIgnoreRulesFromFile_WithExportedConfigurationBundle_LoadsRules()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"ignore-rules-{Guid.NewGuid():N}.json");
        createdPaths.Add(filePath);
        await File.WriteAllTextAsync(filePath, """
            {
              "schemaVersion": 1,
              "globalSettings": {
                "ignoreCollectionOrder": true
              },
              "ignoreRules": [
                {
                  "propertyPath": "CanonicalResponse.SecretToken",
                  "ignoreCollectionOrder": true
                }
              ]
            }
            """);

        using var serviceProvider = CreateServiceProvider(builder => builder.AddDefaultIgnoreRulesFromFile(filePath));
        var registry = serviceProvider.GetRequiredService<IRequestComparisonAlternateContractProfileRegistry>();

        var profile = registry.Resolve("CanonicalResponse");

        profile.DefaultIgnoreRules.Count.ShouldBe(2);
        profile.DefaultIgnoreRules.Any(rule =>
            string.Equals(rule.PropertyPath, "CanonicalResponse.SecretToken", StringComparison.Ordinal) &&
            rule.IgnoreCollectionOrder).ShouldBeTrue();
    }

    private static ServiceProvider CreateServiceProvider(Action<RequestComparisonAlternateContractProfileBuilder<CanonicalRequest, AlternateRequest, CanonicalResponse, AlternateResponse>>? configureProfile = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddDebug().SetMinimumLevel(LogLevel.Debug));
        services.AddUnifiedComparisonServices(options =>
        {
            options.RegisterDomainModel<CanonicalResponse>("CanonicalResponse");
        });
        services.AddRequestComparisonAlternateContractProfiles(options =>
        {
            options.RegisterProfile<CanonicalRequest, AlternateRequest, CanonicalResponse, AlternateResponse>(
                canonicalModelName: "CanonicalResponse",
                profileId: "alt-json",
                requestMapper: request => new AlternateRequest
                {
                    RequestId = request.Id,
                    DisplayName = request.Name,
                },
                responseMapper: response => new CanonicalResponse
                {
                    Code = response.ResultCode,
                    Name = response.CustomerName,
                    SecretToken = response.Payload.RawToken,
                },
                configure: builder =>
                {
                    builder
                        .SupportSourceRequestFormats(SerializationFormat.Xml)
                        .UseAlternateRequestFormat(SerializationFormat.Json, "application/json")
                        .UseAlternateResponseFormat(SerializationFormat.Json)
                        .AddDefaultIgnoreRule(new IgnoreRuleDto
                        {
                            PropertyPath = "CanonicalResponse.Name",
                            IgnoreCompletely = true,
                        })
                        .MapCanonicalResponsePropertyPath("CanonicalResponse.SecretToken", "Payload.RawToken");

                    configureProfile?.Invoke(builder);
                });
        });

        return services.BuildServiceProvider();
    }

    private static RequestComparisonJob CreateJob() => new()
    {
        JobId = "job123",
        RequestBatchId = "batch123",
        EndpointA = new Uri("https://endpoint-a.test"),
        EndpointB = new Uri("https://endpoint-b.test"),
        HeadersA = new Dictionary<string, string>(),
        HeadersB = new Dictionary<string, string>(),
        ModelName = "CanonicalResponse",
        UseAlternateContractForEndpointB = true,
        AlternateContractProfileId = "alt-json",
    };

    [XmlRoot("CanonicalRequest")]
    public class CanonicalRequest
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;
    }

    public class AlternateRequest
    {
        public string RequestId { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;
    }

    [XmlRoot("CanonicalResponse")]
    public class CanonicalResponse
    {
        public string Code { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string SecretToken { get; set; } = string.Empty;
    }

    public class AlternateResponse
    {
        public string ResultCode { get; set; } = string.Empty;

        public string CustomerName { get; set; } = string.Empty;

        public AlternateResponsePayload Payload { get; set; } = new();
    }

    public class AlternateResponsePayload
    {
        public string RawToken { get; set; } = string.Empty;
    }
}
