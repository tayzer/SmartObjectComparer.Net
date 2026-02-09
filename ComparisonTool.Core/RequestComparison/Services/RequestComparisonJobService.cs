using System.Collections.Concurrent;
using System.Text.Json;
using ComparisonTool.Core.Comparison;
using ComparisonTool.Core.Comparison.Configuration;
using ComparisonTool.Core.Comparison.Results;
using ComparisonTool.Core.RequestComparison.Models;
using ComparisonTool.Core.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Core.RequestComparison.Services;

/// <summary>
/// Service for managing request comparison jobs.
/// </summary>
public class RequestComparisonJobService
{
    private readonly ILogger<RequestComparisonJobService> _logger;
    private readonly RequestExecutionService _executionService;
    private readonly RequestFileParserService _parserService;
    private readonly RawTextComparisonService _rawTextComparisonService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IComparisonProgressPublisher? _progressPublisher;
    private readonly ConcurrentDictionary<string, RequestComparisonJob> _jobs = new();
    private readonly ConcurrentDictionary<string, MultiFolderComparisonResult> _results = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastProgressUpdate = new();
    private static readonly TimeSpan ProgressThrottleInterval = TimeSpan.FromMilliseconds(250);

    public RequestComparisonJobService(
        ILogger<RequestComparisonJobService> logger,
        RequestExecutionService executionService,
        RequestFileParserService parserService,
        RawTextComparisonService rawTextComparisonService,
        IServiceScopeFactory scopeFactory,
        IComparisonProgressPublisher? progressPublisher = null)
    {
        _logger = logger;
        _executionService = executionService;
        _parserService = parserService;
        _rawTextComparisonService = rawTextComparisonService;
        _scopeFactory = scopeFactory;
        _progressPublisher = progressPublisher;
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
        if (_progressPublisher == null) return;

        // Throttle updates during high-frequency phases (Executing)
        if (!forcePublish && phase == ComparisonPhase.Executing)
        {
            var now = DateTimeOffset.UtcNow;
            if (_lastProgressUpdate.TryGetValue(jobId, out var lastUpdate) &&
                now - lastUpdate < ProgressThrottleInterval)
            {
                return;
            }
            _lastProgressUpdate[jobId] = now;
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
            await _progressPublisher.PublishAsync(update);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish progress update for job {JobId}", jobId);
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
            EndpointB = new Uri(request.EndpointB),
            HeadersA = request.HeadersA ?? new Dictionary<string, string>(),
            HeadersB = request.HeadersB ?? new Dictionary<string, string>(),
            ContentTypeOverride = request.ContentTypeOverride,
            TimeoutMs = request.TimeoutMs,
            MaxConcurrency = request.MaxConcurrency,
            ModelName = request.ModelName ?? "Auto",
            // Comparison configuration parity with Home
            IgnoreCollectionOrder = request.IgnoreCollectionOrder,
            IgnoreStringCase = request.IgnoreStringCase,
            IgnoreXmlNamespaces = request.IgnoreXmlNamespaces,
            IgnoreRules = request.IgnoreRules?.ToList() ?? new List<IgnoreRuleDto>(),
            SmartIgnoreRules = request.SmartIgnoreRules?.ToList() ?? new List<SmartIgnoreRuleDto>(),
            EnableSemanticAnalysis = request.EnableSemanticAnalysis,
            EnableEnhancedStructuralAnalysis = request.EnableEnhancedStructuralAnalysis
        };

        _jobs[job.JobId] = job;
        _logger.LogInformation("Created request comparison job {JobId} with model {ModelName}", job.JobId, job.ModelName);

        return job;
    }

    /// <summary>
    /// Gets a job by ID.
    /// </summary>
    public RequestComparisonJob? GetJob(string jobId) =>
        _jobs.TryGetValue(jobId, out var job) ? job : null;

    /// <summary>
    /// Gets the comparison result for a completed job.
    /// </summary>
    public MultiFolderComparisonResult? GetResult(string jobId) =>
        _results.TryGetValue(jobId, out var result) ? result : null;

    /// <summary>
    /// Executes a request comparison job asynchronously.
    /// </summary>
    public async Task ExecuteJobAsync(
        string jobId,
        IProgress<(int Completed, int Total, string Message)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!_jobs.TryGetValue(jobId, out var job))
        {
            throw new InvalidOperationException($"Job {jobId} not found");
        }

        try
        {
            // Phase 1: Parse request files (0-5%)
            job.Status = RequestComparisonStatus.Uploading;
            job.StatusMessage = "Parsing request files...";
            progress?.Report((0, 0, "Parsing request files..."));
            await PublishProgressAsync(jobId, ComparisonPhase.Parsing, 0, "Parsing request files...", forcePublish: true);

            var requests = await _parserService.ParseRequestBatchAsync(
                job.RequestBatchId,
                cancellationToken).ConfigureAwait(false);

            job.TotalRequests = requests.Count;
            _logger.LogInformation("Parsed {Count} request files for job {JobId}", requests.Count, jobId);
            await PublishProgressAsync(jobId, ComparisonPhase.Parsing, 5, $"Parsed {requests.Count} request files", requests.Count, requests.Count, forcePublish: true);

            // Phase 2: Execute requests (5-75%)
            job.Status = RequestComparisonStatus.Executing;
            job.StatusMessage = "Executing requests...";
            await PublishProgressAsync(jobId, ComparisonPhase.Executing, 5, "Starting request execution...", 0, requests.Count, forcePublish: true);

            var executionProgress = new Progress<(int Completed, int Total, string Message)>(p =>
            {
                job.CompletedRequests = p.Completed;
                job.StatusMessage = p.Message;
                progress?.Report(p);
                // Calculate percent: 5% + (70% * completed/total)
                var percent = 5 + (int)(70.0 * p.Completed / Math.Max(1, p.Total));
                _ = PublishProgressAsync(jobId, ComparisonPhase.Executing, percent, p.Message, p.Completed, p.Total);
            });

            var executionResults = await _executionService.ExecuteRequestsAsync(
                job,
                requests,
                executionProgress,
                cancellationToken).ConfigureAwait(false);

            var successCount = executionResults.Count(r => r.Success);
            _logger.LogInformation(
                "Executed {Success}/{Total} requests successfully for job {JobId}",
                successCount,
                executionResults.Count,
                jobId);
            await PublishProgressAsync(jobId, ComparisonPhase.Executing, 75, $"Executed {successCount}/{executionResults.Count} requests successfully", executionResults.Count, executionResults.Count, forcePublish: true);

            // Phase 2.5: Classify execution results by HTTP outcome
            var classified = ExecutionResultClassifier.ClassifyAll(executionResults);
            var outcomeSummary = ExecutionResultClassifier.Summarize(classified);

            var successPairs = classified.Where(c => c.Outcome == RequestPairOutcome.BothSuccess).ToList();
            var nonSuccessPairs = classified.Where(c =>
                c.Outcome == RequestPairOutcome.StatusCodeMismatch ||
                c.Outcome == RequestPairOutcome.BothNonSuccess).ToList();
            var failedPairs = classified.Where(c => c.Outcome == RequestPairOutcome.OneOrBothFailed).ToList();

            _logger.LogInformation(
                "Job {JobId} classification: BothSuccess={BothSuccess}, StatusCodeMismatch={StatusCodeMismatch}, BothNonSuccess={BothNonSuccess}, OneOrBothFailed={Failed}",
                jobId,
                outcomeSummary.BothSuccess,
                outcomeSummary.StatusCodeMismatch,
                outcomeSummary.BothNonSuccess,
                outcomeSummary.OneOrBothFailed);

            // Phase 3: Compare responses (75-100%)
            job.Status = RequestComparisonStatus.Comparing;
            job.StatusMessage = "Comparing responses...";
            progress?.Report((job.TotalRequests, job.TotalRequests, "Comparing responses..."));
            await PublishProgressAsync(jobId, ComparisonPhase.Comparing, 75, "Starting response comparison...", forcePublish: true);

            MultiFolderComparisonResult comparisonResult;

            if (successPairs.Count > 0)
            {
                // Phase 3a: Domain-model comparison for BothSuccess pairs (existing pipeline)
                var comparisonProgress = new Progress<ComparisonProgress>(p =>
                {
                    job.StatusMessage = p.Status;
                    progress?.Report((p.Completed, p.Total, p.Status));
                    // Calculate percent: 75% + (20% * completed/total) — reserve last 5% for raw text comparison
                    var percent = 75 + (int)(20.0 * p.Completed / Math.Max(1, p.Total));
                    _ = PublishProgressAsync(jobId, ComparisonPhase.Comparing, Math.Min(percent, 95), p.Status, p.Completed, p.Total);
                });

                using var scope = _scopeFactory.CreateScope();

                // Apply per-job configuration settings
                var configService = scope.ServiceProvider.GetRequiredService<IComparisonConfigurationService>();
                var xmlDeserializationService = scope.ServiceProvider.GetRequiredService<IXmlDeserializationService>();

                ApplyJobConfiguration(job, configService, xmlDeserializationService);

                var comparisonService = scope.ServiceProvider.GetRequiredService<DirectoryComparisonService>();

                comparisonResult = await comparisonService.CompareDirectoriesAsync(
                    job.ResponsePathA!,
                    job.ResponsePathB!,
                    job.ModelName,
                    includeAllFiles: true,
                    enablePatternAnalysis: true,
                    enableSemanticAnalysis: job.EnableSemanticAnalysis,
                    progress: comparisonProgress,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // No success pairs — create an empty result container
                comparisonResult = new MultiFolderComparisonResult
                {
                    TotalPairsCompared = 0,
                    AllEqual = false,
                    FilePairResults = new List<FilePairComparisonResult>(),
                    Metadata = new Dictionary<string, object>(StringComparer.Ordinal),
                };
            }

            // Phase 3b: Raw text comparison for StatusCodeMismatch + BothNonSuccess pairs
            if (nonSuccessPairs.Count > 0)
            {
                await PublishProgressAsync(jobId, ComparisonPhase.Comparing, 95,
                    $"Comparing {nonSuccessPairs.Count} non-success response pairs as raw text...", forcePublish: true);

                var rawTextResults = await _rawTextComparisonService.CompareAllRawAsync(
                    nonSuccessPairs,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                comparisonResult.FilePairResults.AddRange(rawTextResults);
                comparisonResult.AllEqual = false; // Non-success pairs always count as 'not all equal'
                comparisonResult.TotalPairsCompared += rawTextResults.Count;

                _logger.LogInformation(
                    "Raw text comparison completed for {Count} non-success pairs in job {JobId}",
                    rawTextResults.Count,
                    jobId);
            }

            // Phase 3c: Add error records for OneOrBothFailed pairs
            if (failedPairs.Count > 0)
            {
                foreach (var failed in failedPairs)
                {
                    comparisonResult.FilePairResults.Add(new FilePairComparisonResult
                    {
                        File1Name = failed.Execution.Request.RelativePath,
                        File2Name = failed.Execution.Request.RelativePath,
                        ErrorMessage = failed.Execution.ErrorMessage ?? "Request execution failed",
                        ErrorType = "HttpRequestException",
                        PairOutcome = RequestPairOutcome.OneOrBothFailed,
                    });
                }

                comparisonResult.AllEqual = false;
                comparisonResult.TotalPairsCompared += failedPairs.Count;
            }

            // Stamp BothSuccess pair outcomes onto their FilePairComparisonResults
            // (non-success pairs already have PairOutcome set by the raw text service)
            foreach (var pairResult in comparisonResult.FilePairResults)
            {
                if (pairResult.PairOutcome == null)
                {
                    // This is a domain-model comparison result — find its execution result
                    var execResult = successPairs.FirstOrDefault(c =>
                        string.Equals(
                            Path.GetFileName(c.Execution.Request.RelativePath),
                            pairResult.File1Name,
                            StringComparison.OrdinalIgnoreCase));

                    if (execResult != null)
                    {
                        pairResult.PairOutcome = RequestPairOutcome.BothSuccess;
                        pairResult.HttpStatusCodeA = execResult.Execution.StatusCodeA;
                        pairResult.HttpStatusCodeB = execResult.Execution.StatusCodeB;
                    }
                }
            }

            // Sort all results by filename for consistent ordering
            comparisonResult.FilePairResults = comparisonResult.FilePairResults
                .OrderBy(r => r.File1Name, StringComparer.Ordinal)
                .ToList();

            // Store execution metadata in result
            comparisonResult.Metadata["RequestComparisonJobId"] = jobId;
            comparisonResult.Metadata["ExecutionOutcomeSummary"] = outcomeSummary;
            comparisonResult.Metadata["ExecutionResults"] = executionResults
                .Where(r => !r.Success)
                .Select(r => new { r.Request.RelativePath, r.ErrorMessage })
                .ToList();

            _results[jobId] = comparisonResult;

            // Phase 4: Complete
            job.Status = RequestComparisonStatus.Completed;
            job.StatusMessage = "Comparison completed";
            progress?.Report((job.TotalRequests, job.TotalRequests, "Comparison completed"));
            await PublishProgressAsync(jobId, ComparisonPhase.Completed, 100, "Comparison completed successfully", job.TotalRequests, job.TotalRequests, forcePublish: true);

            _logger.LogInformation("Completed request comparison job {JobId}", jobId);
            _lastProgressUpdate.TryRemove(jobId, out _);
        }
        catch (OperationCanceledException)
        {
            job.Status = RequestComparisonStatus.Cancelled;
            job.StatusMessage = "Job was cancelled";
            await PublishProgressAsync(jobId, ComparisonPhase.Cancelled, job.CompletedRequests * 100 / Math.Max(1, job.TotalRequests), "Job was cancelled", forcePublish: true);
            _logger.LogWarning("Request comparison job {JobId} was cancelled", jobId);
            _lastProgressUpdate.TryRemove(jobId, out _);
            throw;
        }
        catch (Exception ex)
        {
            job.Status = RequestComparisonStatus.Failed;
            job.ErrorMessage = ex.Message;
            job.StatusMessage = $"Failed: {ex.Message}";
            await PublishProgressAsync(jobId, ComparisonPhase.Failed, job.CompletedRequests * 100 / Math.Max(1, job.TotalRequests), $"Failed: {ex.Message}", error: ex.Message, forcePublish: true);
            _logger.LogError(ex, "Request comparison job {JobId} failed", jobId);
            _lastProgressUpdate.TryRemove(jobId, out _);
            throw;
        }
    }

    /// <summary>
    /// Cleans up job resources older than the specified age.
    /// </summary>
    public void CleanupOldJobs(TimeSpan maxAge)
    {
        var cutoff = DateTimeOffset.UtcNow - maxAge;
        var oldJobs = _jobs.Values.Where(j => j.CreatedAt < cutoff).ToList();

        foreach (var job in oldJobs)
        {
            _jobs.TryRemove(job.JobId, out _);
            _results.TryRemove(job.JobId, out _);

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
                _logger.LogWarning(ex, "Failed to clean up job directory for {JobId}", job.JobId);
            }
        }

        if (oldJobs.Count > 0)
        {
            _logger.LogInformation("Cleaned up {Count} old request comparison jobs", oldJobs.Count);
        }
    }

    /// <summary>
    /// Applies per-job configuration settings to the comparison services.
    /// </summary>
    private void ApplyJobConfiguration(
        RequestComparisonJob job,
        IComparisonConfigurationService configService,
        IXmlDeserializationService xmlDeserializationService)
    {
        _logger.LogInformation(
            "Applying job configuration for {JobId}: IgnoreCollectionOrder={IgnoreCollectionOrder}, IgnoreStringCase={IgnoreStringCase}, IgnoreXmlNamespaces={IgnoreXmlNamespaces}, IgnoreRules={IgnoreRuleCount}, SmartIgnoreRules={SmartIgnoreRuleCount}",
            job.JobId,
            job.IgnoreCollectionOrder,
            job.IgnoreStringCase,
            job.IgnoreXmlNamespaces,
            job.IgnoreRules.Count,
            job.SmartIgnoreRules.Count);

        // Clear existing rules to start fresh for this job
        configService.ClearIgnoreRules();
        configService.ClearSmartIgnoreRules();

        // Apply global settings
        configService.SetIgnoreCollectionOrder(job.IgnoreCollectionOrder);
        configService.SetIgnoreStringCase(job.IgnoreStringCase);
        xmlDeserializationService.IgnoreXmlNamespaces = job.IgnoreXmlNamespaces;

        // Apply ignore rules
        foreach (var ruleDto in job.IgnoreRules)
        {
            var rule = new IgnoreRule
            {
                PropertyPath = ruleDto.PropertyPath,
                IgnoreCompletely = ruleDto.IgnoreCompletely,
                IgnoreCollectionOrder = ruleDto.IgnoreCollectionOrder
            };
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
                _logger.LogWarning("Unknown smart ignore rule type: {Type}", ruleDto.Type);
            }
        }

        // Apply all configured settings
        configService.ApplyConfiguredSettings();
    }
}
