using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml.Serialization;
using ComparisonTool.Core.Comparison;
using ComparisonTool.Core.Comparison.Analysis;
using ComparisonTool.Core.Comparison.Results;
using ComparisonTool.Core.Comparison.Configuration;
using ComparisonTool.Core.DI;
using ComparisonTool.Core.RequestComparison.AlternateContracts;
using ComparisonTool.Core.RequestComparison.Models;
using ComparisonTool.Core.RequestComparison.Services;
using ComparisonTool.Core.Serialization;
using ComparisonTool.Core.Utilities;
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
    public async Task ExecuteJobAsync_WithRequestAnalysisEnabled_PopulatesFinalReportAnalysisMetadata()
    {
        var handler = new AdvancedAlternateContractTestHttpMessageHandler();
        var spyState = new SpyComparisonServiceState();
        using var serviceProvider = CreateAdvancedServiceProvider(
            handler,
            configureServices: services =>
            {
                services.AddSingleton(spyState);
                services.Replace(ServiceDescriptor.Scoped<IComparisonService>(provider =>
                    new SpyComparisonService(CreateInnerComparisonService(provider), provider.GetRequiredService<SpyComparisonServiceState>())));
            });
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
            IgnoreXmlNamespaces = true,
            MaxConcurrency = 2,
            TimeoutMs = 10000,
            EnableSemanticAnalysis = true,
            EnableEnhancedStructuralAnalysis = true,
        });

        createdDirectories.Add(Path.Combine(Path.GetTempPath(), "ComparisonToolJobs", job.JobId));

        await jobService.ExecuteJobAsync(job.JobId);

        var result = jobService.GetResult(job.JobId);
        result.ShouldNotBeNull();
        result.AllEqual.ShouldBeFalse();
        result.Metadata.ContainsKey("PatternAnalysis").ShouldBeTrue();
        result.Metadata.ContainsKey("SemanticAnalysis").ShouldBeTrue();
        result.Metadata.ContainsKey("EnhancedStructuralAnalysis").ShouldBeTrue();
        spyState.PatternAnalysisCalls.ShouldBe(1);
        spyState.SemanticAnalysisCalls.ShouldBe(1);
        spyState.EnhancedStructuralAnalysisCalls.ShouldBe(1);
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
            "alpha/lookup.xml",
            "beta/lookup.xml",
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
            string.Equals(pair.RequestRelativePath, "alpha/lookup.xml", StringComparison.Ordinal));
        alphaPair.File1Path.ShouldContain(Path.Combine("comparisonA", "alpha", "lookup.json"), Case.Insensitive);
        alphaPair.File2Path.ShouldContain(Path.Combine("comparisonB", "alpha", "lookup.json"), Case.Insensitive);

        var betaPair = result.FilePairResults.Single(pair =>
            string.Equals(pair.RequestRelativePath, "beta/lookup.xml", StringComparison.Ordinal));
        betaPair.File1Path.ShouldContain(Path.Combine("comparisonA", "beta", "lookup.json"), Case.Insensitive);
        betaPair.File2Path.ShouldContain(Path.Combine("comparisonB", "beta", "lookup.json"), Case.Insensitive);
    }

    [TestMethod]
    public void CreateJob_WithEndpointLabelsAndNullEmptyOptions_ShouldPersistRequestSettings()
    {
        using var serviceProvider = CreateServiceProvider(new AlternateContractTestHttpMessageHandler());
        var jobService = serviceProvider.GetRequiredService<RequestComparisonJobService>();

        var job = jobService.CreateJob(new CreateRequestComparisonJobRequest
        {
            RequestBatchId = Guid.NewGuid().ToString("N"),
            EndpointA = "https://endpoint-a.test/customer-lookup",
            EndpointALabel = "Primary SOAP",
            EndpointB = "https://endpoint-b.test/customer-lookup",
            EndpointBLabel = "Canary JSON",
            ModelName = RequestComparisonAlternateContractSampleRegistration.SampleModelName,
            TreatNullAndEmptyCollectionsAsEqual = true,
            IgnoreRules = new List<IgnoreRule>
            {
                new()
                {
                    PropertyPath = "Order.Items",
                    TreatNullAndEmptyCollectionsAsEqual = true,
                },
            },
        });

        job.EndpointALabel.ShouldBe("Primary SOAP");
        job.EndpointBLabel.ShouldBe("Canary JSON");
        job.TreatNullAndEmptyCollectionsAsEqual.ShouldBeTrue();
        job.IgnoreRules.Single().TreatNullAndEmptyCollectionsAsEqual.ShouldBeTrue();
    }

    [TestMethod]
    public async Task ExecuteJobAsync_RecordsRequestTimingMetadataAndPublishesFinalizingProgress()
    {
        var handler = new AlternateContractTestHttpMessageHandler();
        var progressPublisher = new RecordingComparisonProgressPublisher();
        using var serviceProvider = CreateServiceProvider(handler, progressPublisher);
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
        });

        createdDirectories.Add(Path.Combine(Path.GetTempPath(), "ComparisonToolJobs", job.JobId));

        await jobService.ExecuteJobAsync(job.JobId);

        var result = jobService.GetResult(job.JobId);
        result.ShouldNotBeNull();
        result.Metadata.ContainsKey(RequestComparisonRunTimings.MetadataKey).ShouldBeTrue();

        var timings = result.Metadata[RequestComparisonRunTimings.MetadataKey].ShouldBeOfType<RequestComparisonRunTimings>();
        timings.TotalRequests.ShouldBe(2);
        timings.SuccessfulRequests.ShouldBe(2);
        timings.TotalPairsCompared.ShouldBe(2);
        timings.TotalElapsedMs.ShouldBeGreaterThanOrEqualTo(0);
        timings.ParsingMs.ShouldBeGreaterThanOrEqualTo(0);
        timings.RequestExecutionMs.ShouldBeGreaterThanOrEqualTo(0);
        timings.ResponseComparisonMs.ShouldBeGreaterThanOrEqualTo(0);
        timings.FocusedRawContentMs.ShouldBeGreaterThanOrEqualTo(0);
        timings.FinalizationMs.ShouldBeGreaterThanOrEqualTo(0);

        progressPublisher.Updates.Any(update => update.Phase == ComparisonPhase.Finalizing).ShouldBeTrue();
        progressPublisher.Updates.Last().Phase.ShouldBe(ComparisonPhase.Completed);
    }

    [TestMethod]
    [Timeout(180000)]
    [TestCategory("Performance")]
    public async Task ExecuteJobAsync_With5000SyntheticAlternateContractRequests_MeetsPerformanceFitnessFunctions()
    {
        var handler = new FastExpectedJsonCustomerLookupHandler();
        using var serviceProvider = CreateAdvancedServiceProvider(
            handler,
            extraConfiguration: LargeBatchFitnessConfiguration());
        var jobService = serviceProvider.GetRequiredService<RequestComparisonJobService>();

        var batchId = Guid.NewGuid().ToString("N");
        CreateAdvancedPerformanceRequestBatch(batchId, 5000);

        var job = jobService.CreateJob(new CreateRequestComparisonJobRequest
        {
            RequestBatchId = batchId,
            EndpointA = "https://endpoint-a.test/customer-lookup",
            EndpointB = "https://endpoint-b.test/customer-lookup",
            ModelName = AdvancedExpectedModelName,
            UseAlternateContractForEndpointB = true,
            AlternateContractProfileId = AdvancedProfileId,
            IgnoreXmlNamespaces = true,
            MaxConcurrency = 32,
            TimeoutMs = 10000,
            EnableSemanticAnalysis = true,
            EnableEnhancedStructuralAnalysis = false,
        });

        createdDirectories.Add(Path.Combine(Path.GetTempPath(), "ComparisonToolJobs", job.JobId));

        await jobService.ExecuteJobAsync(job.JobId);

        var result = jobService.GetResult(job.JobId);
        result.ShouldNotBeNull();
        result.TotalPairsCompared.ShouldBe(5000);
        result.FilePairResults.Count.ShouldBe(5000);

        var timings = result.Metadata[RequestComparisonRunTimings.MetadataKey].ShouldBeOfType<RequestComparisonRunTimings>();
        timings.TotalRequests.ShouldBe(5000);
        timings.SuccessfulRequests.ShouldBe(5000);
        timings.TotalPairsCompared.ShouldBe(5000);
        timings.LargeBatchMode.ShouldBeTrue();
        timings.LargeBatchTotalChunks.ShouldBe(10);
        timings.TotalElapsedMs.ShouldBeLessThanOrEqualTo(120000);
        timings.ParsingMs.ShouldBeLessThanOrEqualTo(5000);
        timings.RequestExecutionMs.ShouldBeLessThanOrEqualTo(60000);
        timings.ResponseComparisonMs.ShouldBeLessThanOrEqualTo(45000);
        timings.FocusedRawContentMs.ShouldBeLessThanOrEqualTo(5000);
        timings.FinalizationMs.ShouldBeLessThanOrEqualTo(3000);
    }

    [TestMethod]
    public async Task ExecuteJobAsync_WithLargeBatchChunks_DoesNotRunDiscardedChunkAnalysisOrFocusedArtifacts()
    {
        var handler = new FastExpectedJsonCustomerLookupHandler();
        var spyState = new SpyComparisonServiceState();
        using var serviceProvider = CreateAdvancedServiceProvider(
            handler,
            extraConfiguration: LargeBatchFitnessConfiguration(threshold: 2, chunkSize: 1),
            configureServices: services =>
            {
                services.AddSingleton(spyState);
                services.Replace(ServiceDescriptor.Scoped<IComparisonService>(provider =>
                    new SpyComparisonService(CreateInnerComparisonService(provider), provider.GetRequiredService<SpyComparisonServiceState>())));
            });
        var jobService = serviceProvider.GetRequiredService<RequestComparisonJobService>();

        var batchId = Guid.NewGuid().ToString("N");
        CreateAdvancedPerformanceRequestBatch(batchId, 2);

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
            EnableSemanticAnalysis = true,
        });

        var jobDirectory = Path.Combine(Path.GetTempPath(), "ComparisonToolJobs", job.JobId);
        createdDirectories.Add(jobDirectory);

        await jobService.ExecuteJobAsync(job.JobId);

        var result = jobService.GetResult(job.JobId);
        result.ShouldNotBeNull();
        result.FilePairResults.Count.ShouldBe(2);
        result.Metadata.ContainsKey("PatternAnalysis").ShouldBeFalse();
        result.Metadata.ContainsKey("SemanticAnalysis").ShouldBeFalse();
        spyState.PatternAnalysisCalls.ShouldBe(0);
        spyState.SemanticAnalysisCalls.ShouldBe(0);
        result.Metadata[FocusedRawContentArtifactService.MetadataFocusedPairCountKey].ShouldBe(2);
        foreach (var pair in result.FilePairResults)
        {
            pair.HasFocusedRawContent.ShouldBeTrue();
            pair.FocusedFile1Path.ShouldBeNull();
            pair.FocusedFile2Path.ShouldBeNull();
            pair.FocusedRawContentIgnorePaths.ShouldContain($"{AdvancedExpectedModelName}.SourceSystem");
        }

        var rawContentService = new RawContentService(
            serviceProvider.GetRequiredService<ILogger<RawContentService>>(),
            focusedPruningService: serviceProvider.GetRequiredService<StructuredContentPruningService>());
        var focusedContent = await rawContentService.LoadRawContentAsync(result.FilePairResults[0], RawContentVariant.Focused);
        focusedContent.IsLoaded.ShouldBeTrue();
        focusedContent.ContentA.ShouldContain("ResultCode");
        focusedContent.ContentA.ShouldNotContain("SourceSystem");
        focusedContent.ContentB.ShouldNotContain("SourceSystem");

        var focusedDirectories = Directory.Exists(jobDirectory)
            ? Directory.GetDirectories(jobDirectory, "focused", SearchOption.AllDirectories)
            : Array.Empty<string>();
        focusedDirectories.ShouldBeEmpty();
    }

    [TestMethod]
    public async Task ExecuteJobAsync_WithAlternateContractMaterialization_RespectsConfiguredConcurrencyBound()
    {
        var handler = new FastExpectedJsonCustomerLookupHandler();
        var tracker = new MaterializationConcurrencyTracker();
        const string boundedProfileId = "bounded-materialization";
        using var serviceProvider = CreateAdvancedServiceProvider(
            handler,
            extraConfiguration: LargeBatchFitnessConfiguration(threshold: 2, chunkSize: 20, materializationConcurrency: 3),
            configureServices: services =>
            {
                services.AddSingleton(tracker);
                services.AddRequestComparisonAlternateContractProfiles(options =>
                {
                    options.RegisterProfile<
                        ExpectedJsonCustomerLookupSoapRequestEnvelope,
                        ExpectedJsonCustomerLookupAlternateRequest,
                        ExpectedJsonCustomerLookupResponse,
                        ExpectedJsonCustomerLookupAlternateResponse>(
                        canonicalModelName: AdvancedExpectedModelName,
                        profileId: boundedProfileId,
                        requestMapper: request => new ExpectedJsonCustomerLookupAlternateRequest
                        {
                            LookupId = request.Body.CustomerLookupRequest.CustomerId,
                        },
                        responseMapper: response => new ExpectedJsonCustomerLookupResponse
                        {
                            ResultCode = response.ResultCode,
                            CustomerName = response.CustomerName,
                            TraceId = response.TraceId,
                            SourceSystem = response.SourceSystem,
                        },
                        configure: builder => builder
                            .SupportSourceRequestFormats(SerializationFormat.Xml)
                            .UseAlternateRequestFormat(SerializationFormat.Json, "application/json")
                            .UseAlternateResponseFormat(SerializationFormat.Json)
                            .UseCanonicalResponseFormat(SerializationFormat.Json, "application/json")
                            .UseEndpointAResponseNormalizer(async (context, cancellationToken) =>
                            {
                                var concurrencyTracker = context.Services.GetRequiredService<MaterializationConcurrencyTracker>();
                                concurrencyTracker.Enter();
                                try
                                {
                                    await Task.Delay(25, cancellationToken).ConfigureAwait(false);
                                    var normalized = new ExpectedJsonCustomerLookupResponse
                                    {
                                        ResultCode = "00",
                                        CustomerName = "Alpha",
                                        TraceId = "trace-1001",
                                        SourceSystem = "endpoint-a",
                                    };

                                    return new NormalizedAlternateContractResponse(
                                        JsonSerializer.SerializeToUtf8Bytes(normalized),
                                        SerializationFormat.Json,
                                        "application/json",
                                        null);
                                }
                                finally
                                {
                                    concurrencyTracker.Exit();
                                }
                            })
                            .AddDefaultIgnoreRule(new IgnoreRule
                            {
                                PropertyPath = $"{AdvancedExpectedModelName}.SourceSystem",
                                IgnoreCompletely = true,
                            }));
                });
            });
        var jobService = serviceProvider.GetRequiredService<RequestComparisonJobService>();

        var batchId = Guid.NewGuid().ToString("N");
        CreateAdvancedPerformanceRequestBatch(batchId, 20);

        var job = jobService.CreateJob(new CreateRequestComparisonJobRequest
        {
            RequestBatchId = batchId,
            EndpointA = "https://endpoint-a.test/customer-lookup",
            EndpointB = "https://endpoint-b.test/customer-lookup",
            ModelName = AdvancedExpectedModelName,
            UseAlternateContractForEndpointB = true,
            AlternateContractProfileId = boundedProfileId,
            IgnoreXmlNamespaces = true,
            MaxConcurrency = 20,
            TimeoutMs = 10000,
        });

        createdDirectories.Add(Path.Combine(Path.GetTempPath(), "ComparisonToolJobs", job.JobId));

        await jobService.ExecuteJobAsync(job.JobId);

        tracker.MaxObserved.ShouldBeLessThanOrEqualTo(3);
        tracker.MaxObserved.ShouldBeGreaterThan(1);
    }

    private static IReadOnlyDictionary<string, string?> LargeBatchFitnessConfiguration(
        int threshold = 1000,
        int chunkSize = 500,
        int materializationConcurrency = 32) => new Dictionary<string, string?>
    {
        ["RequestComparison:LargeBatchThreshold"] = threshold.ToString(),
        ["RequestComparison:LargeBatchChunkSize"] = chunkSize.ToString(),
        ["RequestComparison:LargeBatchDefaultConcurrency"] = "32",
        ["RequestComparison:ResponseMaterializationMaxConcurrency"] = materializationConcurrency.ToString(),
    };

    private static ComparisonService CreateInnerComparisonService(IServiceProvider provider) => new(
        provider.GetRequiredService<ILogger<ComparisonService>>(),
        provider.GetRequiredService<IXmlDeserializationService>(),
        provider.GetRequiredService<IComparisonConfigurationService>(),
        provider.GetRequiredService<IFileSystemService>(),
        provider.GetRequiredService<PerformanceTracker>(),
        provider.GetRequiredService<SystemResourceMonitor>(),
        provider.GetRequiredService<ComparisonResultCacheService>(),
        provider.GetRequiredService<IComparisonEngine>(),
        provider.GetRequiredService<IComparisonOrchestrator>(),
        provider.GetService<DeserializationServiceFactory>());

    private static ServiceProvider CreateServiceProvider(AlternateContractTestHttpMessageHandler handler, IComparisonProgressPublisher? progressPublisher = null)
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
        services.AddSingleton<IComparisonProgressPublisher>(progressPublisher ?? new NoOpComparisonProgressPublisher());
        services.AddSingleton<RequestComparisonJobService>();

        return services.BuildServiceProvider();
    }

    private static ServiceProvider CreateAdvancedServiceProvider(
        HttpMessageHandler handler,
        IComparisonProgressPublisher? progressPublisher = null,
        IEnumerable<KeyValuePair<string, string?>>? extraConfiguration = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection();
        var configurationValues = new Dictionary<string, string?>
        {
            [$"{ExpectedJsonCustomerLookupAlternateContractOptions.ConfigurationSectionName}:AuthorizationTokenUrl"] = "https://auth.test/authorisation-token",
        };

        if (extraConfiguration != null)
        {
            foreach (var item in extraConfiguration)
            {
                configurationValues[item.Key] = item.Value;
            }
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configurationValues)
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
        services.AddSingleton<IComparisonProgressPublisher>(progressPublisher ?? new NoOpComparisonProgressPublisher());
        services.AddSingleton<RequestComparisonJobService>();

        configureServices?.Invoke(services);

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

    private void CreateAdvancedPerformanceRequestBatch(string batchId, int count)
    {
        var batchPath = Path.Combine(Path.GetTempPath(), "ComparisonToolRequests", batchId);
        Directory.CreateDirectory(batchPath);
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
        var requestXml = SerializeXml(request);

        for (var index = 0; index < count; index++)
        {
            File.WriteAllText(Path.Combine(batchPath, $"request-{index:D5}.xml"), requestXml);
        }
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

    private sealed class FastExpectedJsonCustomerLookupHandler : HttpMessageHandler
    {
        private static readonly string AuthResponse = JsonSerializer.Serialize(new ExpectedJsonCustomerLookupAuthorizationTokenResponse
        {
            AuthorizationToken = "AUTHZ-1001",
            BackupAuthorizationToken = "BACKUP-1001",
        });

        private static readonly string EndpointASuccessResponse = SerializeXml(new ExpectedJsonCustomerLookupSoapResponseEnvelope
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
        });

        private static readonly string EndpointBSuccessResponse = JsonSerializer.Serialize(new ExpectedJsonCustomerLookupAlternateResponse
        {
            ResultCode = "00",
            CustomerName = "Alpha",
            TraceId = "trace-1001",
            SourceSystem = "endpoint-b",
        });

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request.RequestUri);

            var (statusCode, body, contentType) = request.RequestUri.Host switch
            {
                "auth.test" => (HttpStatusCode.OK, AuthResponse, "application/json"),
                "endpoint-a.test" => (HttpStatusCode.OK, EndpointASuccessResponse, "application/xml"),
                "endpoint-b.test" => (HttpStatusCode.OK, EndpointBSuccessResponse, "application/json"),
                _ => throw new InvalidOperationException($"Unhandled endpoint host '{request.RequestUri.Host}'."),
            };

            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, contentType),
            });
        }
    }

    private sealed class SpyComparisonServiceState
    {
        private int patternAnalysisCalls;
        private int semanticAnalysisCalls;
        private int enhancedStructuralAnalysisCalls;

        public int PatternAnalysisCalls => patternAnalysisCalls;
        public int SemanticAnalysisCalls => semanticAnalysisCalls;
        public int EnhancedStructuralAnalysisCalls => enhancedStructuralAnalysisCalls;

        public void RecordPatternAnalysis() => Interlocked.Increment(ref patternAnalysisCalls);
        public void RecordSemanticAnalysis() => Interlocked.Increment(ref semanticAnalysisCalls);
        public void RecordEnhancedStructuralAnalysis() => Interlocked.Increment(ref enhancedStructuralAnalysisCalls);
    }

    private sealed class SpyComparisonService : IComparisonService
    {
        private readonly IComparisonService inner;
        private readonly SpyComparisonServiceState state;

        public SpyComparisonService(IComparisonService inner, SpyComparisonServiceState state)
        {
            this.inner = inner;
            this.state = state;
        }

        public Task<KellermanSoftware.CompareNetObjects.ComparisonResult> CompareXmlFilesAsync(
            Stream oldXmlStream,
            Stream newXmlStream,
            string modelName,
            CancellationToken cancellationToken = default) =>
            inner.CompareXmlFilesAsync(oldXmlStream, newXmlStream, modelName, cancellationToken);

        public Task<KellermanSoftware.CompareNetObjects.ComparisonResult> CompareXmlFilesWithCachingAsync(
            Stream oldXmlStream,
            Stream newXmlStream,
            string modelName,
            string oldFilePath = null!,
            string newFilePath = null!,
            CancellationToken cancellationToken = default) =>
            inner.CompareXmlFilesWithCachingAsync(oldXmlStream, newXmlStream, modelName, oldFilePath, newFilePath, cancellationToken);

        public Task<KellermanSoftware.CompareNetObjects.ComparisonResult> CompareFilesAsync(
            Stream oldFileStream,
            Stream newFileStream,
            string modelName,
            string oldFilePath,
            string newFilePath,
            CancellationToken cancellationToken = default) =>
            inner.CompareFilesAsync(oldFileStream, newFileStream, modelName, oldFilePath, newFilePath, cancellationToken);

        public Task<KellermanSoftware.CompareNetObjects.ComparisonResult> CompareFilesWithCachingAsync(
            Stream oldFileStream,
            Stream newFileStream,
            string modelName,
            string oldFilePath,
            string newFilePath,
            CancellationToken cancellationToken = default) =>
            inner.CompareFilesWithCachingAsync(oldFileStream, newFileStream, modelName, oldFilePath, newFilePath, cancellationToken);

        public Task<FilePairComparisonResult> CompareFilesWithCachingAsPairResultAsync(
            Stream oldFileStream,
            Stream newFileStream,
            string modelName,
            string oldFilePath,
            string newFilePath,
            CancellationToken cancellationToken = default) =>
            inner.CompareFilesWithCachingAsPairResultAsync(oldFileStream, newFileStream, modelName, oldFilePath, newFilePath, cancellationToken);

        public Task<MultiFolderComparisonResult> CompareFoldersAsync(
            List<string> folder1Files,
            List<string> folder2Files,
            string modelName,
            CancellationToken cancellationToken = default) =>
            inner.CompareFoldersAsync(folder1Files, folder2Files, modelName, cancellationToken);

        public Task<MultiFolderComparisonResult> CompareFoldersInBatchesAsync(
            List<string> folder1Files,
            List<string> folder2Files,
            string modelName,
            int batchSize = 50,
            IProgress<(int Completed, int Total)>? progress = null,
            CancellationToken cancellationToken = default) =>
            inner.CompareFoldersInBatchesAsync(folder1Files, folder2Files, modelName, batchSize, progress, cancellationToken);

        public Task<ComparisonPatternAnalysis> AnalyzePatternsAsync(
            MultiFolderComparisonResult folderResult,
            CancellationToken cancellationToken = default)
        {
            state.RecordPatternAnalysis();
            return inner.AnalyzePatternsAsync(folderResult, cancellationToken);
        }

        public Task<SemanticDifferenceAnalysis> AnalyzeSemanticDifferencesAsync(
            MultiFolderComparisonResult folderResult,
            ComparisonPatternAnalysis patternAnalysis,
            CancellationToken cancellationToken = default)
        {
            state.RecordSemanticAnalysis();
            return inner.AnalyzeSemanticDifferencesAsync(folderResult, patternAnalysis, cancellationToken);
        }

        public Task<EnhancedStructuralDifferenceAnalyzer.EnhancedStructuralAnalysisResult> AnalyzeStructualPatternsAsync(
            MultiFolderComparisonResult folderResult,
            CancellationToken cancellationToken = default)
        {
            state.RecordEnhancedStructuralAnalysis();
            return inner.AnalyzeStructualPatternsAsync(folderResult, cancellationToken);
        }
    }

    private sealed class MaterializationConcurrencyTracker
    {
        private int current;
        private int maxObserved;

        public int MaxObserved => maxObserved;

        public void Enter()
        {
            var observed = Interlocked.Increment(ref current);
            int snapshot;
            do
            {
                snapshot = maxObserved;
                if (observed <= snapshot)
                {
                    return;
                }
            }
            while (Interlocked.CompareExchange(ref maxObserved, observed, snapshot) != snapshot);
        }

        public void Exit() => Interlocked.Decrement(ref current);
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


    private sealed class RecordingComparisonProgressPublisher : IComparisonProgressPublisher
    {
        private readonly List<ComparisonProgressUpdate> updates = new();

        public IReadOnlyList<ComparisonProgressUpdate> Updates => updates;

        public Task PublishAsync(ComparisonProgressUpdate update, CancellationToken cancellationToken = default)
        {
            updates.Add(update);
            return Task.CompletedTask;
        }
    }    private sealed class NoOpComparisonProgressPublisher : IComparisonProgressPublisher
    {
        public Task PublishAsync(ComparisonProgressUpdate update, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
