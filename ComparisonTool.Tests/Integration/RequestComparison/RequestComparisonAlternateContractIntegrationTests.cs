using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml.Serialization;
using ComparisonTool.Core.Comparison.Results;
using ComparisonTool.Core.DI;
using ComparisonTool.Core.RequestComparison.AlternateContracts;
using ComparisonTool.Core.RequestComparison.Models;
using ComparisonTool.Core.RequestComparison.Services;
using ComparisonTool.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace ComparisonTool.Tests.Integration.RequestComparison;

[TestClass]
public sealed class RequestComparisonAlternateContractIntegrationTests : IDisposable
{
    private readonly List<string> createdDirectories = new();

    public void Dispose()
    {
        foreach (var directory in createdDirectories)
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task ExecuteJobAsync_WithSoapEndpointAAndAlternateJsonEndpointB_NormalizesSuccessAndMasksNonSuccessRawResponses()
    {
        var handler = new AlternateContractTestHttpMessageHandler();
        using var serviceProvider = CreateServiceProvider(handler);
        var jobService = serviceProvider.GetRequiredService<RequestComparisonJobService>();

        var batchId = Guid.NewGuid().ToString("N");
        CreateRequestBatch(batchId);

        var job = jobService.CreateJob(new CreateRequestComparisonJobRequest
        {
            RequestBatchId = batchId,
            EndpointA = "https://endpoint-a.test/customer-lookup",
            EndpointB = "https://endpoint-b.test/customer-lookup",
            ModelName = RequestComparisonAlternateContractSampleRegistration.SampleModelName,
            UseAlternateContractForEndpointB = true,
            AlternateContractProfileId = RequestComparisonAlternateContractSampleRegistration.SampleProfileId,
            IgnoreXmlNamespaces = true,
            MaxConcurrency = 2,
            TimeoutMs = 10000,
            MaskRules = new List<MaskRuleDto>
            {
                new()
                {
                    PropertyPath = "Envelope.Body.CustomerLookupResponse.SensitiveToken",
                    PreserveLastCharacters = 4,
                    MaskCharacter = "*",
                },
            },
        });

        createdDirectories.Add(Path.Combine(Path.GetTempPath(), "ComparisonToolJobs", job.JobId));

        await jobService.ExecuteJobAsync(job.JobId);

        var result = jobService.GetResult(job.JobId);
        result.ShouldNotBeNull();
        result.TotalPairsCompared.ShouldBe(2);

        var endpointARequests = handler.GetCapturedRequests("endpoint-a.test");
        var endpointBRequests = handler.GetCapturedRequests("endpoint-b.test");

        endpointARequests.Count.ShouldBe(2);
        endpointARequests.All(request => string.Equals(request.ContentType, "application/xml", StringComparison.OrdinalIgnoreCase))
            .ShouldBeTrue();
        endpointARequests.Any(request => request.Body.Contains("<Envelope", StringComparison.Ordinal))
            .ShouldBeTrue();

        endpointBRequests.Count.ShouldBe(2);
        endpointBRequests.All(request => string.Equals(request.ContentType, "application/json", StringComparison.OrdinalIgnoreCase))
            .ShouldBeTrue();
        endpointBRequests.Any(request => request.Body.Contains("\"lookupId\":\"1001\"", StringComparison.Ordinal))
            .ShouldBeTrue();
        endpointBRequests.Any(request => request.Body.Contains("\"raw_token\":\"SUCCESS-SECRET-1234\"", StringComparison.Ordinal))
            .ShouldBeTrue();

        result.Metadata["UseAlternateContractForEndpointB"].ShouldBe(true);
        result.Metadata["AlternateContractProfileId"].ShouldBe(RequestComparisonAlternateContractSampleRegistration.SampleProfileId);

        var successPair = result.FilePairResults.Single(pair =>
            string.Equals(pair.RequestRelativePath, "success-request.xml", StringComparison.Ordinal));
        successPair.PairOutcome.ShouldBe(RequestPairOutcome.BothSuccess);
        successPair.HttpStatusCodeA.ShouldBe(200);
        successPair.HttpStatusCodeB.ShouldBe(200);
        successPair.ContentTypeA.ShouldContain("xml");
        successPair.ContentTypeB.ShouldContain("json");
        successPair.AreEqual.ShouldBeTrue();

        var rawPair = result.FilePairResults.Single(pair =>
            string.Equals(pair.RequestRelativePath, "error-request.xml", StringComparison.Ordinal));
        rawPair.PairOutcome.ShouldBe(RequestPairOutcome.BothNonSuccess);
        rawPair.HttpStatusCodeA.ShouldBe(400);
        rawPair.HttpStatusCodeB.ShouldBe(400);
        rawPair.AreEqual.ShouldBeFalse();
        rawPair.RawTextDifferences.ShouldNotBeNull();
        rawPair.RawTextDifferences.Count.ShouldBeGreaterThan(0);
        rawPair.File1Path.ShouldNotBeNull();
        rawPair.File2Path.ShouldNotBeNull();

        var maskedEndpointABody = await File.ReadAllTextAsync(rawPair.File1Path!);
        var maskedEndpointBBody = await File.ReadAllTextAsync(rawPair.File2Path!);

        maskedEndpointABody.ShouldNotContain("ERROR-SECRET-5678");
        maskedEndpointABody.ShouldContain("*************5678");
        maskedEndpointBBody.ShouldNotContain("ERROR-SECRET-5678");
        maskedEndpointBBody.ShouldContain("*************5678");
    }

    private static ServiceProvider CreateServiceProvider(AlternateContractTestHttpMessageHandler handler)
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.AddDebug().SetMinimumLevel(LogLevel.Warning));
        services.AddUnifiedComparisonServices(options =>
        {
            RequestComparisonAlternateContractSampleRegistration.RegisterComparisonModels(options);
        });
        services.AddRequestComparisonAlternateContractProfiles(
            RequestComparisonAlternateContractSampleRegistration.RegisterProfiles);
        services.AddSingleton<IHttpClientFactory>(new TestHttpClientFactory(handler));
        services.AddSingleton<RequestFileParserService>();
        services.AddSingleton<ResponseMaskingService>();
        services.AddSingleton<RequestExecutionService>();
        services.AddSingleton<RawTextComparisonService>();
        services.AddSingleton<IComparisonProgressPublisher, NoOpComparisonProgressPublisher>();
        services.AddSingleton<RequestComparisonJobService>();

        return services.BuildServiceProvider();
    }

    private void CreateRequestBatch(string batchId)
    {
        var batchPath = Path.Combine(Path.GetTempPath(), "ComparisonToolRequests", batchId);
        Directory.CreateDirectory(batchPath);
        createdDirectories.Add(batchPath);

        var successRequest = new SampleSoapCustomerLookupRequestEnvelope
        {
            Body = new SampleSoapCustomerLookupRequestBody
            {
                CustomerLookupRequest = new SampleSoapCustomerLookupRequest
                {
                    CustomerId = "1001",
                    SensitiveToken = "SUCCESS-SECRET-1234",
                },
            },
        };

        var errorRequest = new SampleSoapCustomerLookupRequestEnvelope
        {
            Body = new SampleSoapCustomerLookupRequestBody
            {
                CustomerLookupRequest = new SampleSoapCustomerLookupRequest
                {
                    CustomerId = "4000",
                    SensitiveToken = "ERROR-SECRET-5678",
                },
            },
        };

        File.WriteAllText(Path.Combine(batchPath, "success-request.xml"), SerializeXml(successRequest));
        File.WriteAllText(Path.Combine(batchPath, "error-request.xml"), SerializeXml(errorRequest));
    }

    private static string SerializeXml<T>(T value)
    {
        var serializer = new XmlSerializer(typeof(T));
        using var writer = new Utf8StringWriter();
        serializer.Serialize(writer, value);
        return writer.ToString();
    }

    private sealed class Utf8StringWriter : StringWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
    }

    private sealed class AlternateContractTestHttpMessageHandler : HttpMessageHandler
    {
        private readonly ConcurrentBag<CapturedRequest> capturedRequests = new();

        public IReadOnlyList<CapturedRequest> GetCapturedRequests(string host) =>
            capturedRequests
                .Where(request => string.Equals(request.Host, host, StringComparison.OrdinalIgnoreCase))
                .OrderBy(request => request.Body, StringComparer.Ordinal)
                .ToArray();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request.RequestUri);

            var body = request.Content == null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var contentType = request.Content?.Headers.ContentType?.MediaType ?? string.Empty;

            capturedRequests.Add(new CapturedRequest(
                request.RequestUri.Host,
                request.RequestUri.AbsolutePath,
                contentType,
                body));

            return request.RequestUri.Host switch
            {
                "endpoint-a.test" => CreateSoapResponse(body),
                "endpoint-b.test" => CreateJsonResponse(body),
                _ => throw new InvalidOperationException($"Unhandled endpoint host '{request.RequestUri.Host}'."),
            };
        }

        private static HttpResponseMessage CreateSoapResponse(string requestBody)
        {
            var request = DeserializeXml<SampleSoapCustomerLookupRequestEnvelope>(requestBody);
            var customerId = request.Body.CustomerLookupRequest.CustomerId;
            var token = request.Body.CustomerLookupRequest.SensitiveToken;
            var isSuccess = string.Equals(customerId, "1001", StringComparison.Ordinal);

            var response = new SampleSoapCustomerLookupResponseEnvelope
            {
                Body = new SampleSoapCustomerLookupResponseBody
                {
                    CustomerLookupResponse = new SampleSoapCustomerLookupResponse
                    {
                        StatusCode = isSuccess ? "00" : "BAD",
                        CustomerName = isSuccess ? "Alpha" : "Invalid request",
                        SensitiveToken = token,
                    },
                },
            };

            return new HttpResponseMessage(isSuccess ? HttpStatusCode.OK : HttpStatusCode.BadRequest)
            {
                Content = new StringContent(SerializeXml(response), Encoding.UTF8, "application/xml"),
            };
        }

        private static HttpResponseMessage CreateJsonResponse(string requestBody)
        {
            var request = JsonSerializer.Deserialize<SampleAlternateJsonCustomerLookupRequest>(requestBody)
                ?? throw new InvalidOperationException("Alternate JSON request could not be deserialized.");
            var isSuccess = string.Equals(request.LookupId, "1001", StringComparison.Ordinal);

            var response = new SampleAlternateJsonCustomerLookupResponse
            {
                StatusCode = isSuccess ? "00" : "BAD",
                CustomerName = isSuccess ? "Alpha" : "Invalid request",
                Payload = new SampleAlternateJsonCustomerLookupPayload
                {
                    RawToken = request.RawToken,
                },
            };

            return new HttpResponseMessage(isSuccess ? HttpStatusCode.OK : HttpStatusCode.BadRequest)
            {
                Content = new StringContent(JsonSerializer.Serialize(response), Encoding.UTF8, "application/json"),
            };
        }

        private static T DeserializeXml<T>(string xml)
        {
            var serializer = new XmlSerializer(typeof(T));
            using var reader = new StringReader(xml);
            return (T)(serializer.Deserialize(reader) ?? throw new InvalidOperationException(
                $"Deserialization for '{typeof(T).Name}' returned null."));
        }
    }

    private sealed record CapturedRequest(
        string Host,
        string Path,
        string ContentType,
        string Body);

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler handler;

        public TestHttpClientFactory(HttpMessageHandler handler)
        {
            this.handler = handler;
        }

        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class NoOpComparisonProgressPublisher : IComparisonProgressPublisher
    {
        public Task PublishAsync(ComparisonProgressUpdate update, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}