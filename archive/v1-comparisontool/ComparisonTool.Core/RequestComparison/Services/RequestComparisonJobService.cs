using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using ComparisonTool.Core.Comparison;
using ComparisonTool.Core.Comparison.Configuration;
using ComparisonTool.Core.Comparison.Results;
using ComparisonTool.Core.RequestComparison.Models;
using ComparisonTool.Core.RequestComparison.AlternateContracts;
using ComparisonTool.Core.Serialization;
using ComparisonTool.Core.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ComparisonTool.Core.RequestComparison.Services;

/// <summary>
/// Service for managing request comparison jobs.
/// </summary>
public class RequestComparisonJobService
{
    private readonly ILogger<RequestComparisonJobService> logger;
    private readonly RequestExecutionService executionService;
    private readonly RequestFileParserService parserService;
    private readonly RawTextComparisonService rawTextComparisonService;
    private readonly ResponseMaskingService responseMaskingService;
    private readonly RequestComparisonAlternateContractTransformationService alternateContractTransformationService;
    private readonly FocusedRawContentArtifactService focusedRawContentArtifactService;
    private readonly IServiceScopeFactory scopeFactory;
    private readonly IComparisonProgressPublisher? progressPublisher;
    private readonly RequestComparisonLargeBatchOptions largeBatchOptions;
    private readonly ConcurrentDictionary<string, RequestComparisonJob> jobs = new();
    private readonly ConcurrentDictionary<string, MultiFolderComparisonResult> results = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> lastProgressUpdate = new();
    private static readonly TimeSpan ProgressThrottleInterval = TimeSpan.FromMilliseconds(250);

    public RequestComparisonJobService(
        ILogger<RequestComparisonJobService> logger,
        RequestExecutionService executionService,
        RequestFileParserService parserService,
        RawTextComparisonService rawTextComparisonService,
        ResponseMaskingService responseMaskingService,
        RequestComparisonAlternateContractTransformationService alternateContractTransformationService,
        FocusedRawContentArtifactService focusedRawContentArtifactService,
        IServiceScopeFactory scopeFactory,
        IOptions<RequestComparisonLargeBatchOptions>? largeBatchOptions = null,
        IComparisonProgressPublisher? progressPublisher = null)
    {
        this.logger = logger;
        this.executionService = executionService;
        this.parserService = parserService;
        this.rawTextComparisonService = rawTextComparisonService;
        this.responseMaskingService = responseMaskingService;
        this.alternateContractTransformationService = alternateContractTransformationService;
        this.focusedRawContentArtifactService = focusedRawContentArtifactService;
        this.scopeFactory = scopeFactory;
        this.largeBatchOptions = largeBatchOptions?.Value ?? new RequestComparisonLargeBatchOptions();
        this.progressPublisher = progressPublisher;
    }

    /// <summary>
    /// Publishes a progress update with optional throttling for high-frequency phases.
    /// </summary>
    private async Task PublishProgressAsync(
        string jobId,
        ComparisonPhase phase,
        int percent,
        string message,
        int? completed = null,
        int? total = null,
        string? error = null,
        bool forcePublish = false)
    {
        if (progressPublisher == null) return;

        // Throttle updates during high-frequency phases (Executing)
        if (!forcePublish && phase == ComparisonPhase.Executing)
        {
            var now = DateTimeOffset.UtcNow;
            if (lastProgressUpdate.TryGetValue(jobId, out var lastUpdate) &&
                now - lastUpdate < ProgressThrottleInterval)
            {
                return;
            }
            lastProgressUpdate[jobId] = now;
        }

        var update = new ComparisonProgressUpdate
        {
            JobId = jobId,
            Phase = phase,
            PercentComplete = percent,
            Message = message,
            Timestamp = DateTimeOffset.UtcNow,
            CompletedItems = completed,
            TotalItems = total,
            ErrorMessage = error
        };

        try
        {
            await progressPublisher.PublishAsync(update);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to publish progress update for job {JobId}", jobId);
        }
    }

    /// <summary>
    /// Creates a new request comparison job.
    /// </summary>
    public RequestComparisonJob CreateJob(CreateRequestComparisonJobRequest request)
    {
        var job = new RequestComparisonJob
        {
            JobId = Guid.NewGuid().ToString("N")[..12],
            RequestBatchId = request.RequestBatchId,
            EndpointA = new Uri(request.EndpointA),
            EndpointALabel = request.EndpointALabel,
            EndpointB = new Uri(request.EndpointB),
            EndpointBLabel = request.EndpointBLabel,
            HeadersA = request.HeadersA ?? new Dictionary<string, string>(),
            HeadersB = request.HeadersB ?? new Dictionary<string, string>(),
            ContentTypeOverride = request.ContentTypeOverride,
            TimeoutMs = request.TimeoutMs,
            MaxConcurrency = request.MaxConcurrency,
            ModelName = request.ModelName ?? "Auto",
            UseAlternateContractForEndpointB = request.UseAlternateContractForEndpointB,
            AlternateContractProfileId = request.AlternateContractProfileId,
            // Comparison configuration parity with Home
            IgnoreCollectionOrder = request.IgnoreCollectionOrder,
            IgnoreStringCase = request.IgnoreStringCase,
            IgnoreTrailingWhitespaceAtEnd = request.IgnoreTrailingWhitespaceAtEnd,
            TreatNullAndEmptyCollectionsAsEqual = request.TreatNullAndEmptyCollectionsAsEqual,
            IgnoreXmlNamespaces = request.IgnoreXmlNamespaces,
            IgnoreRules = request.IgnoreRules?.ToList() ?? new List<IgnoreRule>(),
            SmartIgnoreRules = request.SmartIgnoreRules?.ToList() ?? new List<SmartIgnoreRuleDto>(),
            MaskRules = request.MaskRules?.ToList() ?? new List<MaskRuleDto>(),
            EnableSemanticAnalysis = request.EnableSemanticAnalysis,
            EnableEnhancedStructuralAnalysis = request.EnableEnhancedStructuralAnalysis
        };

        if (job.MaskRules.Count > 0)
        {
            responseMaskingService.ValidateRules(job.MaskRules);
        }

        if (job.UseAlternateContractForEndpointB &&
            !alternateContractTransformationService.TryResolveProfile(job, out _, out var profileResolutionError))
        {
            throw new InvalidOperationException(profileResolutionError);
        }

        jobs[job.JobId] = job;
        logger.LogInformation("Created request comparison job {JobId} with model {ModelName}", job.JobId, job.ModelName);

        return job;
    }

    /// <summary>
    /// Gets a job by ID.
    /// </summary>
    public RequestComparisonJob? GetJob(string jobId) =>
        jobs.TryGetValue(jobId, out var job) ? job : null;

    /// <summary>
    /// Gets the comparison result for a completed job.
    /// </summary>
    public MultiFolderComparisonResult? GetResult(string jobId) =>
        results.TryGetValue(jobId, out var result) ? result : null;

    /// <summary>
    /// Executes a request comparison job asynchronously.
    /// </summary>
    public async Task ExecuteJobAsync(
        string jobId,
        IProgress<(int Completed, int Total, string Message)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!jobs.TryGetValue(jobId, out var job))
        {
            throw new InvalidOperationException($"Job {jobId} not found");
        }

        var totalStopwatch = Stopwatch.StartNew();
        long parsingMs = 0;
        long requestExecutionMs = 0;
        long responseComparisonMs = 0;
        long focusedRawContentMs = 0;
        long finalizationMs = 0;

        try
        {
            // Phase 1: Parse request files (0-5%)
            job.Status = RequestComparisonStatus.Uploading;
            job.StatusMessage = "Parsing request files...";
            progress?.Report((0, 0, "Parsing request files..."));
            await PublishProgressAsync(jobId, ComparisonPhase.Parsing, 0, "Parsing request files...", forcePublish: true);

            var parsingStart = Stopwatch.GetTimestamp();
            var requests = await parserService.ParseRequestBatchAsync(
                job.RequestBatchId,
                cancellationToken).ConfigureAwait(false);
            parsingMs = ToMilliseconds(Stopwatch.GetElapsedTime(parsingStart));

            job.TotalRequests = requests.Count;
            logger.LogInformation("Parsed {Count} request files for job {JobId}", requests.Count, jobId);
            await PublishProgressAsync(jobId, ComparisonPhase.Parsing, 5, $"Parsed {requests.Count} request files", requests.Count, requests.Count, forcePublish: true);

            var useLargeBatchMode = RequestComparisonLargeBatchPlanner.ShouldUseLargeBatchMode(requests.Count, largeBatchOptions);
            var effectiveChunkSize = useLargeBatchMode
                ? RequestComparisonLargeBatchPlanner.GetEffectiveChunkSize(largeBatchOptions)
                : Math.Max(1, requests.Count);
            var requestChunks = useLargeBatchMode
                ? RequestComparisonLargeBatchPlanner.Partition(requests, effectiveChunkSize)
                : requests.Count == 0
                    ? Array.Empty<IReadOnlyList<RequestFileInfo>>()
                    : new[] { requests };

            job.LargeBatchMode = useLargeBatchMode;
            job.LargeBatchChunkSize = effectiveChunkSize;
            job.LargeBatchTotalChunks = requestChunks.Count;
            job.LargeBatchProcessedChunks = 0;

            if (useLargeBatchMode)
            {
                logger.LogInformation(
                    "Job {JobId} is using large-batch mode: Requests={RequestCount}, ChunkSize={ChunkSize}, TotalChunks={TotalChunks}, MaxConcurrency={MaxConcurrency}",
                    jobId,
                    requests.Count,
                    effectiveChunkSize,
                    requestChunks.Count,
                    job.MaxConcurrency);
                await PublishProgressAsync(
                    jobId,
                    ComparisonPhase.Executing,
                    5,
                    $"Large batch mode: {requests.Count} requests in {requestChunks.Count} chunks of up to {effectiveChunkSize}.",
                    0,
                    requests.Count,
                    forcePublish: true);
            }

            var comparisonResult = new MultiFolderComparisonResult
            {
                TotalPairsCompared = 0,
                AllEqual = false,
                FilePairResults = new List<FilePairComparisonResult>(),
                Metadata = new Dictionary<string, object>(StringComparer.Ordinal),
            };

            var outcomeAccumulator = new ExecutionOutcomeSummaryAccumulator();
            var failedExecutionRecords = new List<RequestExecutionFailureMetadata>();
            var alternateContractProfile = job.UseAlternateContractForEndpointB
                ? alternateContractTransformationService.ResolveProfile(job)
                : null;
            var executedCount = 0;
            var successCount = 0;
            var chunkCountForProgress = Math.Max(1, requestChunks.Count);

            for (var chunkIndex = 0; chunkIndex < requestChunks.Count; chunkIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var chunkNumber = chunkIndex + 1;
                var chunk = requestChunks[chunkIndex];
                var chunkLabel = useLargeBatchMode ? $"Chunk {chunkNumber}/{requestChunks.Count}: " : string.Empty;
                var chunkPercentBase = 5.0 + (90.0 * chunkIndex / chunkCountForProgress);
                var chunkExecuteSpan = 70.0 / chunkCountForProgress;
                var chunkCompareSpan = 20.0 / chunkCountForProgress;
                var completedBeforeChunk = executedCount;

                // Phase 2: Execute this request chunk.
                job.Status = RequestComparisonStatus.Executing;
                job.StatusMessage = $"{chunkLabel}Executing requests...";
                await PublishProgressAsync(
                    jobId,
                    ComparisonPhase.Executing,
                    (int)Math.Round(chunkPercentBase),
                    job.StatusMessage,
                    completedBeforeChunk,
                    requests.Count,
                    forcePublish: true);

                var executionProgress = new Progress<(int Completed, int Total, string Message)>(p =>
                {
                    var globalCompleted = Math.Min(requests.Count, completedBeforeChunk + p.Completed);
                    job.CompletedRequests = globalCompleted;
                    job.StatusMessage = $"{chunkLabel}{p.Message}";
                    progress?.Report((globalCompleted, requests.Count, job.StatusMessage));

                    var percent = (int)Math.Min(
                        95,
                        Math.Round(chunkPercentBase + (chunkExecuteSpan * p.Completed / Math.Max(1, p.Total))));
                    _ = PublishProgressAsync(
                        jobId,
                        ComparisonPhase.Executing,
                        percent,
                        job.StatusMessage,
                        globalCompleted,
                        requests.Count);
                });

                var executionStart = Stopwatch.GetTimestamp();
                var executionResults = await executionService.ExecuteRequestsAsync(
                    job,
                    chunk,
                    executionProgress,
                    cancellationToken).ConfigureAwait(false);
                requestExecutionMs += ToMilliseconds(Stopwatch.GetElapsedTime(executionStart));

                executedCount += executionResults.Count;
                successCount += executionResults.Count(r => r.Success);
                job.CompletedRequests = executedCount;

                failedExecutionRecords.AddRange(executionResults
                    .Where(r => !r.Success)
                    .Select(r => new RequestExecutionFailureMetadata(r.Request.RelativePath, r.ErrorMessage)));

                var comparisonStart = Stopwatch.GetTimestamp();
                var chunkComparisonResult = await CompareExecutionResultsAsync(
                    job,
                    executionResults,
                    alternateContractProfile,
                    chunkNumber,
                    requestChunks.Count,
                    chunkPercentBase + chunkExecuteSpan,
                    chunkCompareSpan,
                    chunkLabel,
                    progress,
                    cancellationToken).ConfigureAwait(false);
                responseComparisonMs += ToMilliseconds(Stopwatch.GetElapsedTime(comparisonStart));

                outcomeAccumulator.Add(chunkComparisonResult.OutcomeSummary);
                comparisonResult.FilePairResults.AddRange(chunkComparisonResult.Result.FilePairResults);
                comparisonResult.TotalPairsCompared = comparisonResult.FilePairResults.Count;

                job.LargeBatchProcessedChunks = chunkNumber;

                await PublishProgressAsync(
                    jobId,
                    ComparisonPhase.Comparing,
                    (int)Math.Min(95, Math.Round(5.0 + (90.0 * chunkNumber / chunkCountForProgress))),
                    useLargeBatchMode ? $"Completed chunk {chunkNumber}/{requestChunks.Count}." : "Compared responses.",
                    executedCount,
                    requests.Count,
                    forcePublish: true);
            }

            var outcomeSummary = outcomeAccumulator.ToSummary();
            logger.LogInformation(
                "Executed {Success}/{Total} requests successfully for job {JobId}",
                successCount,
                executedCount,
                jobId);

            comparisonResult.FilePairResults = comparisonResult.FilePairResults
                .OrderBy(r => r.RequestRelativePath ?? r.File1Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.File1Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            comparisonResult.TotalPairsCompared = comparisonResult.FilePairResults.Count;
            comparisonResult.AllEqual = comparisonResult.FilePairResults.Count > 0
                && comparisonResult.FilePairResults.All(r => r.AreEqual);

            await PublishProgressAsync(jobId, ComparisonPhase.Finalizing, 95, "Preparing focused raw content metadata...", executedCount, requests.Count, forcePublish: true);
            var focusedRawContentStart = Stopwatch.GetTimestamp();
            PrepareFocusedRawContent(job, comparisonResult);
            focusedRawContentMs = ToMilliseconds(Stopwatch.GetElapsedTime(focusedRawContentStart));
            var finalizationStart = Stopwatch.GetTimestamp();

            PopulateRequestResultMetadata(
                comparisonResult,
                job,
                jobId,
                outcomeSummary,
                alternateContractProfile,
                failedExecutionRecords);

            finalizationMs = ToMilliseconds(Stopwatch.GetElapsedTime(finalizationStart));

            await GenerateRequestAnalysisAsync(job, comparisonResult, executedCount, requests.Count, cancellationToken)
                .ConfigureAwait(false);

            await PublishProgressAsync(jobId, ComparisonPhase.Finalizing, 99, "Finalizing comparison result metadata...", executedCount, requests.Count, forcePublish: true);
            totalStopwatch.Stop();
            comparisonResult.Metadata[RequestComparisonRunTimings.MetadataKey] = new RequestComparisonRunTimings
            {
                TotalRequests = requests.Count,
                SuccessfulRequests = successCount,
                TotalPairsCompared = comparisonResult.TotalPairsCompared,
                LargeBatchMode = job.LargeBatchMode,
                LargeBatchTotalChunks = job.LargeBatchTotalChunks,
                ParsingMs = parsingMs,
                RequestExecutionMs = requestExecutionMs,
                ResponseComparisonMs = responseComparisonMs,
                FocusedRawContentMs = focusedRawContentMs,
                FinalizationMs = finalizationMs,
                TotalElapsedMs = totalStopwatch.ElapsedMilliseconds,
            };

            results[jobId] = comparisonResult;

            // Phase 4: Complete
            job.Status = RequestComparisonStatus.Completed;
            job.StatusMessage = "Comparison completed";
            progress?.Report((job.TotalRequests, job.TotalRequests, "Comparison completed"));
            await PublishProgressAsync(jobId, ComparisonPhase.Completed, 100, "Comparison completed successfully", job.TotalRequests, job.TotalRequests, forcePublish: true);

            logger.LogInformation("Completed request comparison job {JobId}", jobId);
            lastProgressUpdate.TryRemove(jobId, out _);
        }
        catch (OperationCanceledException)
        {
            job.Status = RequestComparisonStatus.Cancelled;
            job.StatusMessage = "Job was cancelled";
            await PublishProgressAsync(jobId, ComparisonPhase.Cancelled, job.CompletedRequests * 100 / Math.Max(1, job.TotalRequests), "Job was cancelled", forcePublish: true);
            logger.LogWarning("Request comparison job {JobId} was cancelled", jobId);
            lastProgressUpdate.TryRemove(jobId, out _);
            throw;
        }
        catch (Exception ex)
        {
            job.Status = RequestComparisonStatus.Failed;
            job.ErrorMessage = ex.Message;
            job.StatusMessage = $"Failed: {ex.Message}";
            await PublishProgressAsync(jobId, ComparisonPhase.Failed, job.CompletedRequests * 100 / Math.Max(1, job.TotalRequests), $"Failed: {ex.Message}", error: ex.Message, forcePublish: true);
            logger.LogError(ex, "Request comparison job {JobId} failed", jobId);
            lastProgressUpdate.TryRemove(jobId, out _);
            throw;
        }
    }

    private static void PopulateRequestResultMetadata(
        MultiFolderComparisonResult comparisonResult,
        RequestComparisonJob job,
        string jobId,
        ExecutionOutcomeSummary outcomeSummary,
        RequestComparisonAlternateContractProfile? alternateContractProfile,
        IReadOnlyList<RequestExecutionFailureMetadata> failedExecutionRecords)
    {
        comparisonResult.Metadata["IgnoreCollectionOrder"] = job.IgnoreCollectionOrder;
        comparisonResult.Metadata["TreatNullAndEmptyCollectionsAsEqual"] = job.TreatNullAndEmptyCollectionsAsEqual;
        comparisonResult.Metadata["EndpointA"] = job.EndpointA.ToString();
        comparisonResult.Metadata["EndpointB"] = job.EndpointB.ToString();
        comparisonResult.Metadata["EndpointALabel"] = GetEndpointLabel(job.EndpointALabel, job.EndpointA);
        comparisonResult.Metadata["EndpointBLabel"] = GetEndpointLabel(job.EndpointBLabel, job.EndpointB);
        comparisonResult.Metadata["RequestComparisonJobId"] = jobId;
        comparisonResult.Metadata["ExecutionOutcomeSummary"] = outcomeSummary;
        comparisonResult.Metadata["LargeBatchMode"] = job.LargeBatchMode;
        comparisonResult.Metadata["LargeBatchChunkSize"] = job.LargeBatchChunkSize;
        comparisonResult.Metadata["LargeBatchTotalChunks"] = job.LargeBatchTotalChunks;
        comparisonResult.Metadata["LargeBatchProcessedChunks"] = job.LargeBatchProcessedChunks;
        comparisonResult.Metadata["UseAlternateContractForEndpointB"] = job.UseAlternateContractForEndpointB;
        comparisonResult.Metadata["AlternateContractProfileId"] = job.AlternateContractProfileId!;
        if (alternateContractProfile != null)
        {
            comparisonResult.Metadata["AlternateContractCanonicalResponseFormat"] = alternateContractProfile.CanonicalResponseFormat.ToString();
            comparisonResult.Metadata["AlternateContractDefaultIgnoreRuleCount"] = alternateContractProfile.DefaultIgnoreRules.Count;
        }

        comparisonResult.Metadata["ExecutionResults"] = failedExecutionRecords;
    }

    private async Task GenerateRequestAnalysisAsync(
        RequestComparisonJob job,
        MultiFolderComparisonResult comparisonResult,
        int completedRequests,
        int totalRequests,
        CancellationToken cancellationToken)
    {
        if (comparisonResult.AllEqual ||
            (!job.EnableSemanticAnalysis && !job.EnableEnhancedStructuralAnalysis))
        {
            return;
        }

        job.Status = RequestComparisonStatus.Analyzing;
        job.StatusMessage = "Analyzing response differences...";
        await PublishProgressAsync(
            job.JobId,
            ComparisonPhase.Analyzing,
            99,
            job.StatusMessage,
            completedRequests,
            totalRequests,
            forcePublish: true).ConfigureAwait(false);

        using var scope = scopeFactory.CreateScope();
        var comparisonService = scope.ServiceProvider.GetRequiredService<IComparisonService>();

        if (job.EnableSemanticAnalysis && comparisonResult.FilePairResults.Count > 1)
        {
            try
            {
                var patternAnalysis = await comparisonService.AnalyzePatternsAsync(
                    comparisonResult,
                    cancellationToken).ConfigureAwait(false);
                comparisonResult.Metadata["PatternAnalysis"] = patternAnalysis;

                var semanticAnalysis = await comparisonService.AnalyzeSemanticDifferencesAsync(
                    comparisonResult,
                    patternAnalysis,
                    cancellationToken).ConfigureAwait(false);
                comparisonResult.Metadata["SemanticAnalysis"] = semanticAnalysis;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                comparisonResult.Metadata["SemanticAnalysisError"] = ex.Message;
                logger.LogWarning(ex, "Semantic request comparison analysis failed for job {JobId}", job.JobId);
            }
        }

        if (job.EnableEnhancedStructuralAnalysis)
        {
            try
            {
                var enhancedAnalysis = await comparisonService.AnalyzeStructualPatternsAsync(
                    comparisonResult,
                    cancellationToken).ConfigureAwait(false);
                comparisonResult.Metadata["EnhancedStructuralAnalysis"] = enhancedAnalysis;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                comparisonResult.Metadata["EnhancedStructuralAnalysisError"] = ex.Message;
                logger.LogWarning(ex, "Enhanced structural request comparison analysis failed for job {JobId}", job.JobId);
            }
        }
    }

    private async Task<ChunkComparisonResult> CompareExecutionResultsAsync(
        RequestComparisonJob job,
        IReadOnlyList<RequestExecutionResult> executionResults,
        RequestComparisonAlternateContractProfile? alternateContractProfile,
        int chunkNumber,
        int totalChunks,
        double comparePercentBase,
        double comparePercentSpan,
        string progressPrefix,
        IProgress<(int Completed, int Total, string Message)>? progress,
        CancellationToken cancellationToken)
    {
        var classified = ExecutionResultClassifier.ClassifyAll(executionResults);
        var outcomeSummary = ExecutionResultClassifier.Summarize(classified);

        var successPairs = classified.Where(c => c.Outcome == RequestPairOutcome.BothSuccess).ToList();
        var nonSuccessPairs = classified.Where(c =>
            c.Outcome == RequestPairOutcome.StatusCodeMismatch ||
            c.Outcome == RequestPairOutcome.BothNonSuccess).ToList();
        var failedPairs = classified.Where(c => c.Outcome == RequestPairOutcome.OneOrBothFailed).ToList();

        logger.LogInformation(
            "Job {JobId} chunk {ChunkNumber}/{TotalChunks} classification: BothSuccess={BothSuccess}, StatusCodeMismatch={StatusCodeMismatch}, BothNonSuccess={BothNonSuccess}, OneOrBothFailed={Failed}",
            job.JobId,
            chunkNumber,
            totalChunks,
            outcomeSummary.BothSuccess,
            outcomeSummary.StatusCodeMismatch,
            outcomeSummary.BothNonSuccess,
            outcomeSummary.OneOrBothFailed);

        job.Status = RequestComparisonStatus.Comparing;
        job.StatusMessage = $"{progressPrefix}Comparing responses...";
        progress?.Report((job.CompletedRequests, job.TotalRequests, job.StatusMessage));
        await PublishProgressAsync(
            job.JobId,
            ComparisonPhase.Comparing,
            (int)Math.Min(95, Math.Round(comparePercentBase)),
            job.StatusMessage,
            job.CompletedRequests,
            job.TotalRequests,
            forcePublish: true).ConfigureAwait(false);

        MultiFolderComparisonResult comparisonResult;
        string? comparisonDirectoryA = null;

        if (successPairs.Count > 0)
        {
            var usePersistentComparisonDirectories = alternateContractProfile != null;
            var chunkDirectoryName = totalChunks > 1 ? chunkNumber.ToString("D4") : string.Empty;
            var tempDirA = usePersistentComparisonDirectories
                ? totalChunks > 1
                    ? Path.Combine(Path.GetTempPath(), "ComparisonToolJobs", job.JobId, "comparisonChunks", chunkDirectoryName, "comparisonA")
                    : Path.Combine(Path.GetTempPath(), "ComparisonToolJobs", job.JobId, "comparisonA")
                : Path.Combine(Path.GetTempPath(), $"success_responses_a_{job.JobId}_{chunkNumber:D4}");
            var tempDirB = usePersistentComparisonDirectories
                ? totalChunks > 1
                    ? Path.Combine(Path.GetTempPath(), "ComparisonToolJobs", job.JobId, "comparisonChunks", chunkDirectoryName, "comparisonB")
                    : Path.Combine(Path.GetTempPath(), "ComparisonToolJobs", job.JobId, "comparisonB")
                : Path.Combine(Path.GetTempPath(), $"success_responses_b_{job.JobId}_{chunkNumber:D4}");

            comparisonDirectoryA = tempDirA;

            try
            {
                if (Directory.Exists(tempDirA))
                {
                    Directory.Delete(tempDirA, recursive: true);
                }

                if (Directory.Exists(tempDirB))
                {
                    Directory.Delete(tempDirB, recursive: true);
                }

                Directory.CreateDirectory(tempDirA);
                Directory.CreateDirectory(tempDirB);

                var materializationOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = GetResponseMaterializationMaxConcurrency(job),
                    CancellationToken = cancellationToken,
                };

                await Parallel.ForEachAsync(successPairs, materializationOptions, async (successPair, ct) =>
                {
                    var exec = successPair.Execution;
                    if (exec.ResponsePathA != null && exec.ResponsePathB != null &&
                        File.Exists(exec.ResponsePathA) && File.Exists(exec.ResponsePathB))
                    {
                        await MaterializeSuccessPairForComparisonAsync(job, exec, tempDirA, tempDirB, ct)
                            .ConfigureAwait(false);
                    }
                }).ConfigureAwait(false);

                var comparisonProgress = new Progress<ComparisonProgress>(p =>
                {
                    job.StatusMessage = $"{progressPrefix}{p.Status}";
                    progress?.Report((job.CompletedRequests, job.TotalRequests, job.StatusMessage));
                    var percent = (int)Math.Min(
                        95,
                        Math.Round(comparePercentBase + (comparePercentSpan * p.Completed / Math.Max(1, p.Total))));
                    _ = PublishProgressAsync(
                        job.JobId,
                        ComparisonPhase.Comparing,
                        percent,
                        job.StatusMessage,
                        p.Completed,
                        p.Total);
                });

                using var scope = scopeFactory.CreateScope();

                var configService = scope.ServiceProvider.GetRequiredService<IComparisonConfigurationService>();
                var xmlDeserializationService = scope.ServiceProvider.GetRequiredService<IXmlDeserializationService>();

                ApplyJobConfiguration(job, configService, xmlDeserializationService);

                var comparisonService = scope.ServiceProvider.GetRequiredService<DirectoryComparisonService>();
                var comparisonModelName = alternateContractProfile?.CanonicalModelName ?? job.ModelName;

                comparisonResult = await comparisonService.CompareDirectoriesAsync(
                    tempDirA,
                    tempDirB,
                    comparisonModelName,
                    includeAllFiles: true,
                    enablePatternAnalysis: false,
                    enableSemanticAnalysis: false,
                    populateFocusedRawContent: false,
                    progress: comparisonProgress,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (!usePersistentComparisonDirectories)
                {
                    TryDeleteDirectory(tempDirA);
                    TryDeleteDirectory(tempDirB);
                }
            }
        }
        else
        {
            comparisonResult = new MultiFolderComparisonResult
            {
                TotalPairsCompared = 0,
                AllEqual = false,
                FilePairResults = new List<FilePairComparisonResult>(),
                Metadata = new Dictionary<string, object>(StringComparer.Ordinal),
            };
        }

        if (nonSuccessPairs.Count > 0)
        {
            await PublishProgressAsync(
                job.JobId,
                ComparisonPhase.Comparing,
                (int)Math.Min(95, Math.Round(comparePercentBase + comparePercentSpan)),
                $"{progressPrefix}Comparing {nonSuccessPairs.Count} non-success response pairs as raw text...",
                forcePublish: true).ConfigureAwait(false);

            var rawTextResults = await rawTextComparisonService.CompareAllRawAsync(
                nonSuccessPairs,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            comparisonResult.FilePairResults.AddRange(rawTextResults);

            logger.LogInformation(
                "Raw text comparison completed for {Count} non-success pairs in job {JobId} chunk {ChunkNumber}/{TotalChunks}",
                rawTextResults.Count,
                job.JobId,
                chunkNumber,
                totalChunks);
        }

        if (failedPairs.Count > 0)
        {
            foreach (var failed in failedPairs)
            {
                comparisonResult.FilePairResults.Add(new FilePairComparisonResult
                {
                    File1Name = Path.GetFileName(failed.Execution.Request.RelativePath),
                    File2Name = Path.GetFileName(failed.Execution.Request.RelativePath),
                    RequestRelativePath = failed.Execution.Request.RelativePath,
                    ErrorMessage = failed.Execution.ErrorMessage ?? "Request execution failed",
                    ErrorType = "HttpRequestException",
                    PairOutcome = RequestPairOutcome.OneOrBothFailed,
                });
            }
        }

        StampSuccessPairOutcomes(job, comparisonResult, successPairs, alternateContractProfile, comparisonDirectoryA);
        comparisonResult.TotalPairsCompared = comparisonResult.FilePairResults.Count;
        comparisonResult.AllEqual = comparisonResult.FilePairResults.Count > 0
            && comparisonResult.FilePairResults.All(r => r.AreEqual);

        return new ChunkComparisonResult(comparisonResult, outcomeSummary);
    }

    private void StampSuccessPairOutcomes(
        RequestComparisonJob job,
        MultiFolderComparisonResult comparisonResult,
        IReadOnlyList<ClassifiedExecutionResult> successPairs,
        RequestComparisonAlternateContractProfile? alternateContractProfile,
        string? comparisonDirectoryA)
    {
        foreach (var pairResult in comparisonResult.FilePairResults)
        {
            if (pairResult.PairOutcome != null)
            {
                continue;
            }

            var execResult = successPairs.FirstOrDefault(c =>
                string.Equals(
                    c.Execution.Request.RelativePath,
                    pairResult.RequestRelativePath,
                    StringComparison.OrdinalIgnoreCase));

            execResult ??= ResolveSuccessPairByComparisonArtifactPath(
                successPairs,
                pairResult,
                alternateContractProfile?.CanonicalResponseFormat,
                comparisonDirectoryA);

            execResult ??= successPairs.FirstOrDefault(c =>
                string.Equals(
                    Path.GetFileName(c.Execution.Request.RelativePath),
                    pairResult.File1Name,
                    StringComparison.OrdinalIgnoreCase));

            execResult ??= successPairs.FirstOrDefault(c =>
                string.Equals(
                    Path.GetFileNameWithoutExtension(c.Execution.Request.RelativePath),
                    Path.GetFileNameWithoutExtension(pairResult.File1Name),
                    StringComparison.OrdinalIgnoreCase));

            if (execResult == null)
            {
                continue;
            }

            pairResult.RequestRelativePath = execResult.Execution.Request.RelativePath;
            pairResult.PairOutcome = RequestPairOutcome.BothSuccess;
            pairResult.HttpStatusCodeA = execResult.Execution.StatusCodeA;
            pairResult.HttpStatusCodeB = execResult.Execution.StatusCodeB;
            pairResult.ContentTypeA = alternateContractProfile?.CanonicalResponseContentType ?? execResult.Execution.ContentTypeA;
            pairResult.ContentTypeB = alternateContractProfile?.CanonicalResponseContentType ?? execResult.Execution.ContentTypeB;

            if (alternateContractProfile == null &&
                execResult.Execution.ResponsePathA != null &&
                execResult.Execution.ResponsePathB != null)
            {
                pairResult.File1Path = execResult.Execution.ResponsePathA;
                pairResult.File2Path = execResult.Execution.ResponsePathB;
            }
        }
    }

    private int GetResponseMaterializationMaxConcurrency(RequestComparisonJob job)
    {
        var configuredLimit = Math.Max(1, largeBatchOptions.ResponseMaterializationMaxConcurrency);
        return Math.Max(1, Math.Min(configuredLimit, Math.Max(1, job.MaxConcurrency)));
    }

    private void TryDeleteDirectory(string directoryPath)
    {
        try
        {
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, recursive: true);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete temporary directory {TempDir}", directoryPath);
        }
    }

    private sealed record ChunkComparisonResult(
        MultiFolderComparisonResult Result,
        ExecutionOutcomeSummary OutcomeSummary);

    private sealed record RequestExecutionFailureMetadata(
        string RelativePath,
        string? ErrorMessage);

    private sealed class ExecutionOutcomeSummaryAccumulator
    {
        private int totalRequests;
        private int bothSuccess;
        private int statusCodeMismatch;
        private int bothNonSuccess;
        private int oneOrBothFailed;

        public void Add(ExecutionOutcomeSummary summary)
        {
            totalRequests += summary.TotalRequests;
            bothSuccess += summary.BothSuccess;
            statusCodeMismatch += summary.StatusCodeMismatch;
            bothNonSuccess += summary.BothNonSuccess;
            oneOrBothFailed += summary.OneOrBothFailed;
        }

        public ExecutionOutcomeSummary ToSummary() => new()
        {
            TotalRequests = totalRequests,
            BothSuccess = bothSuccess,
            StatusCodeMismatch = statusCodeMismatch,
            BothNonSuccess = bothNonSuccess,
            OneOrBothFailed = oneOrBothFailed,
        };
    }

    /// <summary>
    /// Cleans up job resources older than the specified age.
    /// </summary>
    public void CleanupOldJobs(TimeSpan maxAge)
    {
        var cutoff = DateTimeOffset.UtcNow - maxAge;
        var oldJobs = jobs.Values.Where(j => j.CreatedAt < cutoff).ToList();

        foreach (var job in oldJobs)
        {
            jobs.TryRemove(job.JobId, out _);
            results.TryRemove(job.JobId, out _);

            // Clean up response directories
            try
            {
                var jobPath = Path.Combine(Path.GetTempPath(), "ComparisonToolJobs", job.JobId);
                if (Directory.Exists(jobPath))
                {
                    Directory.Delete(jobPath, true);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to clean up job directory for {JobId}", job.JobId);
            }
        }

        if (oldJobs.Count > 0)
        {
            logger.LogInformation("Cleaned up {Count} old request comparison jobs", oldJobs.Count);
        }
    }

    private void PrepareFocusedRawContent(
        RequestComparisonJob job,
        MultiFolderComparisonResult comparisonResult)
    {
        var effectiveIgnoreRules = alternateContractTransformationService.GetEffectiveIgnoreRules(job)
            .Where(rule => rule.IgnoreCompletely && !string.IsNullOrWhiteSpace(rule.PropertyPath))
            .Select(rule => new IgnoreRule
            {
                PropertyPath = rule.PropertyPath,
                IgnoreCompletely = true,
            })
            .ToList();

        focusedRawContentArtifactService.MarkFocusedRawContentAvailable(
            comparisonResult,
            effectiveIgnoreRules);
    }

    /// <summary>
    /// Applies per-job configuration settings to the comparison services.
    /// </summary>
    private void ApplyJobConfiguration(
        RequestComparisonJob job,
        IComparisonConfigurationService configService,
        IXmlDeserializationService xmlDeserializationService)
    {
        var effectiveIgnoreRules = alternateContractTransformationService.GetEffectiveIgnoreRules(job);

        logger.LogInformation(
            "Applying job configuration for {JobId}: IgnoreCollectionOrder={IgnoreCollectionOrder}, IgnoreStringCase={IgnoreStringCase}, IgnoreTrailingWhitespaceAtEnd={IgnoreTrailingWhitespaceAtEnd}, TreatNullAndEmptyCollectionsAsEqual={TreatNullAndEmptyCollectionsAsEqual}, IgnoreXmlNamespaces={IgnoreXmlNamespaces}, IgnoreRules={IgnoreRuleCount}, EffectiveIgnoreRules={EffectiveIgnoreRuleCount}, SmartIgnoreRules={SmartIgnoreRuleCount}",
            job.JobId,
            job.IgnoreCollectionOrder,
            job.IgnoreStringCase,
            job.IgnoreTrailingWhitespaceAtEnd,
            job.TreatNullAndEmptyCollectionsAsEqual,
            job.IgnoreXmlNamespaces,
            job.IgnoreRules.Count,
            effectiveIgnoreRules.Count,
            job.SmartIgnoreRules.Count);

        // Clear existing rules to start fresh for this job
        configService.ClearIgnoreRules();
        configService.ClearSmartIgnoreRules();

        // Apply global settings
        configService.SetIgnoreCollectionOrder(job.IgnoreCollectionOrder);
        configService.SetIgnoreStringCase(job.IgnoreStringCase);
        configService.SetIgnoreTrailingWhitespaceAtEnd(job.IgnoreTrailingWhitespaceAtEnd);
        configService.SetTreatNullAndEmptyCollectionsAsEqual(job.TreatNullAndEmptyCollectionsAsEqual);
        xmlDeserializationService.IgnoreXmlNamespaces = job.IgnoreXmlNamespaces;

        // Apply ignore rules
        foreach (var rule in effectiveIgnoreRules)
        {
            configService.AddIgnoreRule(rule);
        }

        // Apply smart ignore rules
        foreach (var ruleDto in job.SmartIgnoreRules)
        {
            if (Enum.TryParse<SmartIgnoreType>(ruleDto.Type, true, out var ruleType))
            {
                var rule = new SmartIgnoreRule
                {
                    Type = ruleType,
                    Value = ruleDto.Value,
                    Description = ruleDto.Description ?? string.Empty
                };
                configService.AddSmartIgnoreRule(rule);
            }
            else
            {
                logger.LogWarning("Unknown smart ignore rule type: {Type}", ruleDto.Type);
            }
        }

        // Apply all configured settings
        configService.ApplyConfiguredSettings();
    }

    private async Task MaterializeSuccessPairForComparisonAsync(
        RequestComparisonJob job,
        RequestExecutionResult executionResult,
        string tempDirA,
        string tempDirB,
        CancellationToken cancellationToken)
    {
        if (!job.UseAlternateContractForEndpointB)
        {
            var targetPathA = BuildComparisonTargetPath(tempDirA, executionResult.Request.RelativePath);
            var targetPathB = BuildComparisonTargetPath(tempDirB, executionResult.Request.RelativePath);

            Directory.CreateDirectory(Path.GetDirectoryName(targetPathA)!);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPathB)!);

            await Task.WhenAll(
                CopyFileAsync(executionResult.ResponsePathA!, targetPathA, cancellationToken),
                CopyFileAsync(executionResult.ResponsePathB!, targetPathB, cancellationToken)).ConfigureAwait(false);
            return;
        }

        var profile = alternateContractTransformationService.ResolveProfile(job);
        var canonicalRelativePath = BuildCanonicalRelativePath(executionResult.Request.RelativePath, profile.CanonicalResponseFormat);
        var targetCanonicalPathA = Path.Combine(tempDirA, canonicalRelativePath);
        var targetCanonicalPathB = Path.Combine(tempDirB, canonicalRelativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(targetCanonicalPathA)!);
        Directory.CreateDirectory(Path.GetDirectoryName(targetCanonicalPathB)!);

        var normalizedA = await alternateContractTransformationService.NormalizeEndpointAResponseAsync(
            job,
            executionResult,
            cancellationToken).ConfigureAwait(false);
        var normalizedB = await alternateContractTransformationService.NormalizeEndpointBResponseAsync(
            job,
            executionResult,
            cancellationToken).ConfigureAwait(false);

        var contentA = job.MaskRules.Count > 0
            ? responseMaskingService.MaskContent(normalizedA.Body, normalizedA.ContentType, targetCanonicalPathA, job.MaskRules)
            : normalizedA.Body;
        var contentB = job.MaskRules.Count > 0
            ? responseMaskingService.MaskContent(normalizedB.Body, normalizedB.ContentType, targetCanonicalPathB, job.MaskRules)
            : normalizedB.Body;

        await Task.WhenAll(
            WriteFileAsync(targetCanonicalPathA, contentA, cancellationToken),
            WriteFileAsync(targetCanonicalPathB, contentB, cancellationToken)).ConfigureAwait(false);
    }

    private static string GetEndpointLabel(string? label, Uri endpoint) =>
        string.IsNullOrWhiteSpace(label)
            ? endpoint.ToString()
            : label.Trim();

    private static string BuildComparisonTargetPath(string rootDirectory, string requestRelativePath)
    {
        var sanitizedPath = SanitizeRelativePath(requestRelativePath);
        return Path.Combine(rootDirectory, sanitizedPath);
    }

    private static string BuildCanonicalRelativePath(string requestRelativePath, SerializationFormat format)
    {
        var sanitizedPath = SanitizeRelativePath(requestRelativePath);
        return Path.ChangeExtension(sanitizedPath, FileTypeDetector.GetFileExtension(format));
    }

    private static string SanitizeRelativePath(string relativePath) => relativePath
        .Replace("..", "_")
        .Replace('/', Path.DirectorySeparatorChar)
        .Replace('\\', Path.DirectorySeparatorChar)
        .TrimStart(Path.DirectorySeparatorChar);

    private static ClassifiedExecutionResult? ResolveSuccessPairByComparisonArtifactPath(
        IReadOnlyList<ClassifiedExecutionResult> successPairs,
        FilePairComparisonResult pairResult,
        SerializationFormat? canonicalResponseFormat,
        string? comparisonDirectoryA)
    {
        if (string.IsNullOrWhiteSpace(pairResult.File1Path) ||
            string.IsNullOrWhiteSpace(comparisonDirectoryA))
        {
            return null;
        }

        if (!TryGetRelativePathUnderRoot(comparisonDirectoryA, pairResult.File1Path, out var artifactRelativePath))
        {
            return null;
        }

        var normalizedArtifactPath = NormalizeRelativePathForComparison(artifactRelativePath);

        return successPairs.FirstOrDefault(successPair =>
        {
            var expectedArtifactPath = canonicalResponseFormat.HasValue
                ? BuildCanonicalRelativePath(successPair.Execution.Request.RelativePath, canonicalResponseFormat.Value)
                : SanitizeRelativePath(successPair.Execution.Request.RelativePath);

            return string.Equals(
                NormalizeRelativePathForComparison(expectedArtifactPath),
                normalizedArtifactPath,
                StringComparison.OrdinalIgnoreCase);
        });
    }

    private static bool TryGetRelativePathUnderRoot(string rootDirectory, string filePath, out string relativePath)
    {
        relativePath = string.Empty;

        if (string.IsNullOrWhiteSpace(rootDirectory) || string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var candidate = Path.GetRelativePath(rootDirectory, filePath);
        if (string.IsNullOrWhiteSpace(candidate) ||
            candidate == "." ||
            candidate.StartsWith("..", StringComparison.Ordinal) ||
            Path.IsPathRooted(candidate))
        {
            return false;
        }

        relativePath = candidate;
        return true;
    }

    private static string NormalizeRelativePathForComparison(string relativePath) =>
        relativePath
            .Replace('\\', '/')
            .TrimStart('/');

    private static long ToMilliseconds(TimeSpan elapsed) =>
        (long)Math.Round(elapsed.TotalMilliseconds, MidpointRounding.AwayFromZero);

    private static async Task CopyFileAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        const int bufferSize = 81920; // 80KB buffer
        await using var sourceStream = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        await using var destinationStream = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        await sourceStream.CopyToAsync(destinationStream, bufferSize, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteFileAsync(string destinationPath, byte[] content, CancellationToken cancellationToken)
    {
        const int bufferSize = 81920; // 80KB buffer
        await using var destinationStream = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        await destinationStream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
    }
}
