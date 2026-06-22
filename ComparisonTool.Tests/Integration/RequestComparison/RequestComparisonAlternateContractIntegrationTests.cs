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
using ComparisonTool.Core.Serialization;
using ComparisonTool.Domain.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace ComparisonTool.Tests.Integration.RequestComparison;

[TestClass]
public sealed class RequestComparisonAlternateContractIntegrationTests : IDisposable
{
    private const string AdvancedExpectedModelName = RequestComparisonExpectedJsonCustomerLookupRegistration.ExpectedModelName;
    private const string AdvancedProfileId = RequestComparisonExpectedJsonCustomerLookupRegistration.ProfileId;

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
        successPair.ContentTypeB.ShouldContain("xml");
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

    [TestMethod]
    public async Task ExecuteJobAsync_WithCustomAlternateProfile_UsesExpectedJsonArtifactsForComparison()
    {
        var handler = new AdvancedAlternateContractTestHttpMessageHandler();
        using var serviceProvider = CreateAdvancedServiceProvider(handler);
        var jobService = serviceProvider.GetRequiredService<RequestComparisonJobService>();

        var batchId = Guid.NewGuid().ToString("N");
        CreateAdvancedRequestBatch(batchId);

        var job = jobService.CreateJob(new CreateRequestComparisonJobRequest
        {
            RequestBatchId = batchId,
            EndpointA = "https://endpoint-a.test/customer-lookup",
            EndpointB = "https://endpoint-b.test/customer-lookup",
            ModelName = AdvancedExpectedModelName,
            UseAlternateContractForEndpointB = true,
            AlternateContractProfileId = AdvancedProfileId,
            HeadersB = new Dictionary<string, string>
            {
                ["X-Client-Header"] = "client-value",
            },
            IgnoreXmlNamespaces = true,
            MaxConcurrency = 2,
            TimeoutMs = 10000,
        });

        createdDirectories.Add(Path.Combine(Path.GetTempPath(), "ComparisonToolJobs", job.JobId));

        await jobService.ExecuteJobAsync(job.JobId);

        var result = jobService.GetResult(job.JobId);
        result.ShouldNotBeNull();
        result.TotalPairsCompared.ShouldBe(2);
        result.Metadata["AlternateContractCanonicalResponseFormat"].ShouldBe("Json");
        result.Metadata["AlternateContractDefaultIgnoreRuleCount"].ShouldBe(1);

        var authRequests = handler.GetCapturedRequests("auth.test");
        authRequests.Count.ShouldBe(2);
        authRequests.Any(request => request.Body.Contains("AUTH-1001", StringComparison.Ordinal)).ShouldBeTrue();

        var endpointBRequests = handler.GetCapturedRequests("endpoint-b.test");
        endpointBRequests.Count.ShouldBe(2);
        endpointBRequests.Any(request => request.Body.Contains("\"lookupId\":\"1001\"", StringComparison.Ordinal)).ShouldBeTrue();
        endpointBRequests.All(request => !request.Body.Contains("authorizationToken", StringComparison.Ordinal)).ShouldBeTrue();
        endpointBRequests.Any(request =>
            request.Headers.TryGetValue("AuthorizationToken", out var headerValue) &&
            string.Equals(headerValue, "AUTHZ-1001", StringComparison.Ordinal)).ShouldBeTrue();
        endpointBRequests.All(request =>
            !request.Headers.TryGetValue("AuthorizationToken", out var headerValue) ||
            !headerValue.Contains("BACKUP-", StringComparison.Ordinal)).ShouldBeTrue();
        endpointBRequests.All(request =>
            request.Headers.TryGetValue("X-Client-Header", out var headerValue) &&
            string.Equals(headerValue, "client-value", StringComparison.Ordinal)).ShouldBeTrue();

        var successPair = result.FilePairResults.Single(pair =>
            string.Equals(pair.RequestRelativePath, "success-request.xml", StringComparison.Ordinal));
        successPair.PairOutcome.ShouldBe(RequestPairOutcome.BothSuccess);
        successPair.AreEqual.ShouldBeTrue();
        successPair.ContentTypeA.ShouldBe("application/json");
        successPair.ContentTypeB.ShouldBe("application/json");
        successPair.File1Path.ShouldNotBeNull();
        successPair.File2Path.ShouldNotBeNull();
        Path.GetExtension(successPair.File1Path!).ShouldBe(".json");
        Path.GetExtension(successPair.File2Path!).ShouldBe(".json");
        successPair.File1Path.ShouldContain(Path.Combine("ComparisonToolJobs", job.JobId, "comparisonA"), Case.Insensitive);
        successPair.File2Path.ShouldContain(Path.Combine("ComparisonToolJobs", job.JobId, "comparisonB"), Case.Insensitive);

        var normalizedEndpointA = await File.ReadAllTextAsync(successPair.File1Path!);
        var normalizedEndpointB = await File.ReadAllTextAsync(successPair.File2Path!);

        normalizedEndpointA.TrimStart().StartsWith("{", StringComparison.Ordinal).ShouldBeTrue();
        normalizedEndpointB.TrimStart().StartsWith("{", StringComparison.Ordinal).ShouldBeTrue();
        normalizedEndpointA.ShouldContain("\"sourceSystem\":\"endpoint-a\"", Case.Sensitive);
        normalizedEndpointB.ShouldContain("\"sourceSystem\":\"endpoint-b\"", Case.Sensitive);

        var rawPair = result.FilePairResults.Single(pair =>
            string.Equals(pair.RequestRelativePath, "error-request.xml", StringComparison.Ordinal));
        rawPair.PairOutcome.ShouldBe(RequestPairOutcome.BothNonSuccess);
        rawPair.AreEqual.ShouldBeFalse();
        rawPair.RawTextDifferences.ShouldNotBeNull();
        rawPair.RawTextDifferences.Count.ShouldBeGreaterThan(0);
        rawPair.ContentTypeA.ShouldContain("xml");
        rawPair.ContentTypeB.ShouldContain("json");
    }

    [TestMethod]
    public async Task ExecuteJobAsync_WithAlternateContractAndDuplicateRequestFileNames_PreservesOriginalRequestRelativePaths()
    {
        var handler = new AdvancedAlternateContractTestHttpMessageHandler();
        using var serviceProvider = CreateAdvancedServiceProvider(handler);
        var jobService = serviceProvider.GetRequiredService<RequestComparisonJobService>();

        var batchId = Guid.NewGuid().ToString("N");
        CreateAdvancedDuplicateFileNameRequestBatch(batchId);

        var job = jobService.CreateJob(new CreateRequestComparisonJobRequest
        {
            RequestBatchId = batchId,
            EndpointA = "https://endpoint-a.test/customer-lookup",
            EndpointB = "https://endpoint-b.test/customer-lookup",
            ModelName = AdvancedExpectedModelName,
            UseAlternateContractForEndpointB = true,
            AlternateContractProfileId = AdvancedProfileId,
            IgnoreXmlNamespaces = true,
            MaxConcurrency = 2,
            TimeoutMs = 10000,
        });

        createdDirectories.Add(Path.Combine(Path.GetTempPath(), "ComparisonToolJobs", job.JobId));

        await jobService.ExecuteJobAsync(job.JobId);

        var result = jobService.GetResult(job.JobId);
        result.ShouldNotBeNull();
        result.TotalPairsCompared.ShouldBe(2);

        var expectedPaths = new[]
        {
            Path.Combine("alpha", "lookup.xml"),
            Path.Combine("beta", "lookup.xml"),
        };
        var actualPaths = result.FilePairResults
            .Select(pair => pair.RequestRelativePath ?? string.Empty)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        actualPaths.ShouldBe(expectedPaths);
        actualPaths.Distinct(StringComparer.Ordinal).Count().ShouldBe(2);

        foreach (var pair in result.FilePairResults)
        {
            pair.PairOutcome.ShouldBe(RequestPairOutcome.BothSuccess);
            pair.HttpStatusCodeA.ShouldBe(200);
            pair.HttpStatusCodeB.ShouldBe(200);
            pair.ContentTypeA.ShouldBe("application/json");
            pair.ContentTypeB.ShouldBe("application/json");
            pair.File1Path.ShouldNotBeNull();
            pair.File2Path.ShouldNotBeNull();
            Path.GetExtension(pair.File1Path!).ShouldBe(".json");
            Path.GetExtension(pair.File2Path!).ShouldBe(".json");
        }

        var alphaPair = result.FilePairResults.Single(pair =>
            string.Equals(pair.RequestRelativePath, Path.Combine("alpha", "lookup.xml"), StringComparison.Ordinal));
        alphaPair.File1Path.ShouldContain(Path.Combine("comparisonA", "alpha", "lookup.json"), Case.Insensitive);
        alphaPair.File2Path.ShouldContain(Path.Combine("comparisonB", "alpha", "lookup.json"), Case.Insensitive);

        var betaPair = result.FilePairResults.Single(pair =>
            string.Equals(pair.RequestRelativePath, Path.Combine("beta", "lookup.xml"), StringComparison.Ordinal));
        betaPair.File1Path.ShouldContain(Path.Combine("comparisonA", "beta", "lookup.json"), Case.Insensitive);
        betaPair.File2Path.ShouldContain(Path.Combine("comparisonB", "beta", "lookup.json"), Case.Insensitive);
    }

    private static ServiceProvider CreateServiceProvider(AlternateContractTestHttpMessageHandler handler)
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.AddDebug().SetMinimumLevel(LogLevel.Warning));
        RequestComparisonAlternateContractBuiltInRegistration.RegisterSharedComparisonModels(services);
        services.AddUnifiedComparisonServices(options =>
        {
            RequestComparisonAlternateContractBuiltInRegistration.RegisterXmlComparisonModels(options);
        });
        services.AddBuiltInRequestComparisonAlternateContracts();
        services.AddSingleton<IHttpClientFactory>(new TestHttpClientFactory(handler));
        services.AddSingleton<RequestFileParserService>();
        services.AddSingleton<ResponseMaskingService>();
        services.AddSingleton<RequestExecutionService>();
        services.AddSingleton<RawTextComparisonService>();
        services.AddSingleton<IComparisonProgressPublisher, NoOpComparisonProgressPublisher>();
        services.AddSingleton<RequestComparisonJobService>();

        return services.BuildServiceProvider();
    }

    private static ServiceProvider CreateAdvancedServiceProvider(AdvancedAlternateContractTestHttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{ExpectedJsonCustomerLookupAlternateContractOptions.ConfigurationSectionName}:AuthorizationTokenUrl"] = "https://auth.test/authorisation-token",
            })
            .Build();

        services.AddLogging(builder => builder.AddDebug().SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<IConfiguration>(configuration);
        RequestComparisonAlternateContractBuiltInRegistration.RegisterSharedComparisonModels(services);
        services.AddUnifiedComparisonServices(configuration, options =>
        {
            RequestComparisonAlternateContractBuiltInRegistration.RegisterXmlComparisonModels(options);
        });
        services.AddBuiltInRequestComparisonAlternateContracts(configuration);
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

    private void CreateAdvancedRequestBatch(string batchId)
    {
        var batchPath = Path.Combine(Path.GetTempPath(), "ComparisonToolRequests", batchId);
        Directory.CreateDirectory(batchPath);
        createdDirectories.Add(batchPath);

        var successRequest = new ExpectedJsonCustomerLookupSoapRequestEnvelope
        {
            Body = new ExpectedJsonCustomerLookupSoapRequestBody
            {
                CustomerLookupRequest = new ExpectedJsonCustomerLookupSoapRequest
                {
                    CustomerId = "1001",
                    AuthenticationToken = "AUTH-1001",
                },
            },
        };

        var errorRequest = new ExpectedJsonCustomerLookupSoapRequestEnvelope
        {
            Body = new ExpectedJsonCustomerLookupSoapRequestBody
            {
                CustomerLookupRequest = new ExpectedJsonCustomerLookupSoapRequest
                {
                    CustomerId = "4000",
                    AuthenticationToken = "AUTH-4000",
                },
            },
        };

        File.WriteAllText(Path.Combine(batchPath, "success-request.xml"), SerializeXml(successRequest));
        File.WriteAllText(Path.Combine(batchPath, "error-request.xml"), SerializeXml(errorRequest));
    }

    private void CreateAdvancedDuplicateFileNameRequestBatch(string batchId)
    {
        var batchPath = Path.Combine(Path.GetTempPath(), "ComparisonToolRequests", batchId);
        var alphaPath = Path.Combine(batchPath, "alpha");
        var betaPath = Path.Combine(batchPath, "beta");

        Directory.CreateDirectory(alphaPath);
        Directory.CreateDirectory(betaPath);
        createdDirectories.Add(batchPath);

        var request = new ExpectedJsonCustomerLookupSoapRequestEnvelope
        {
            Body = new ExpectedJsonCustomerLookupSoapRequestBody
            {
                CustomerLookupRequest = new ExpectedJsonCustomerLookupSoapRequest
                {
                    CustomerId = "1001",
                    AuthenticationToken = "AUTH-1001",
                },
            },
        };

        File.WriteAllText(Path.Combine(alphaPath, "lookup.xml"), SerializeXml(request));
        File.WriteAllText(Path.Combine(betaPath, "lookup.xml"), SerializeXml(request));
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
            var headers = CaptureHeaders(request);

            capturedRequests.Add(new CapturedRequest(
                request.RequestUri.Host,
                request.RequestUri.AbsolutePath,
                contentType,
                body,
                headers));

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

    private sealed class AdvancedAlternateContractTestHttpMessageHandler : HttpMessageHandler
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
            var headers = CaptureHeaders(request);

            capturedRequests.Add(new CapturedRequest(
                request.RequestUri.Host,
                request.RequestUri.AbsolutePath,
                contentType,
                body,
                headers));

            return request.RequestUri.Host switch
            {
                "auth.test" => CreateAuthResponse(body),
                "endpoint-a.test" => CreateSoapResponse(body),
                "endpoint-b.test" => CreateJsonResponse(request, body),
                _ => throw new InvalidOperationException($"Unhandled endpoint host '{request.RequestUri.Host}'."),
            };
        }

        private static HttpResponseMessage CreateAuthResponse(string requestBody)
        {
            var request = JsonSerializer.Deserialize<ExpectedJsonCustomerLookupAuthorizationTokenRequest>(requestBody)
                ?? throw new InvalidOperationException("Auth request could not be deserialized.");

            var response = new ExpectedJsonCustomerLookupAuthorizationTokenResponse
            {
                AuthorizationToken = $"AUTHZ-{request.CustomerId}",
                BackupAuthorizationToken = $"BACKUP-{request.CustomerId}",
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(response), Encoding.UTF8, "application/json"),
            };
        }

        private static HttpResponseMessage CreateSoapResponse(string requestBody)
        {
            var request = DeserializeXml<ExpectedJsonCustomerLookupSoapRequestEnvelope>(requestBody);
            var customerId = request.Body.CustomerLookupRequest.CustomerId;
            var isSuccess = string.Equals(customerId, "1001", StringComparison.Ordinal);

            if (!isSuccess)
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("<error><message>Invalid request</message></error>", Encoding.UTF8, "application/xml"),
                };
            }

            var response = new ExpectedJsonCustomerLookupSoapResponseEnvelope
            {
                Body = new ExpectedJsonCustomerLookupSoapResponseBody
                {
                    CustomerLookupResponse = new ExpectedJsonCustomerLookupSoapResponse
                    {
                        StatusCode = "00",
                        CustomerName = "Alpha",
                        TraceId = "trace-1001",
                    },
                },
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SerializeXml(response), Encoding.UTF8, "application/xml"),
            };
        }

        private static HttpResponseMessage CreateJsonResponse(HttpRequestMessage requestMessage, string requestBody)
        {
            var payload = JsonSerializer.Deserialize<ExpectedJsonCustomerLookupAlternateRequest>(requestBody)
                ?? throw new InvalidOperationException("Alternate JSON request could not be deserialized.");
            var authorizationToken = requestMessage.Headers.TryGetValues("AuthorizationToken", out var values)
                ? values.SingleOrDefault()
                : null;
            var isSuccess = string.Equals(payload.LookupId, "1001", StringComparison.Ordinal);

            if (string.IsNullOrWhiteSpace(authorizationToken))
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("{\"error\":\"missing auth header\"}", Encoding.UTF8, "application/json"),
                };
            }

            if (!isSuccess)
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("{\"error\":\"invalid request\"}", Encoding.UTF8, "application/json"),
                };
            }

            var response = new ExpectedJsonCustomerLookupAlternateResponse
            {
                ResultCode = "00",
                CustomerName = "Alpha",
                TraceId = "trace-1001",
                SourceSystem = "endpoint-b",
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
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
        string Body,
        IReadOnlyDictionary<string, string> Headers);

    private static IReadOnlyDictionary<string, string> CaptureHeaders(HttpRequestMessage request)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in request.Headers)
        {
            headers[header.Key] = string.Join(",", header.Value);
        }

        if (request.Content != null)
        {
            foreach (var header in request.Content.Headers)
            {
                headers[header.Key] = string.Join(",", header.Value);
            }
        }

        return headers;
    }

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