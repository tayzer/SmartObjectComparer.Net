using System.Collections.Concurrent;
using System.Text.Json;
using ComparisonTool.Core.Comparison;
using ComparisonTool.Core.Comparison.Configuration;
using ComparisonTool.Core.Comparison.Results;
using ComparisonTool.Core.RequestComparison.Models;
using ComparisonTool.Core.Serialization;
using ComparisonTool.Web.Services;
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
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ConcurrentDictionary<string, RequestComparisonJob> _jobs = new();
    private readonly ConcurrentDictionary<string, MultiFolderComparisonResult> _results = new();

    public RequestComparisonJobService(
        ILogger<RequestComparisonJobService> logger,
        RequestExecutionService executionService,
        RequestFileParserService parserService,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _executionService = executionService;
        _parserService = parserService;
        _scopeFactory = scopeFactory;
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
            // Phase 1: Parse request files
            job.Status = RequestComparisonStatus.Uploading;
            job.StatusMessage = "Parsing request files...";
            progress?.Report((0, 0, "Parsing request files..."));

            var requests = await _parserService.ParseRequestBatchAsync(
                job.RequestBatchId,
                cancellationToken).ConfigureAwait(false);

            job.TotalRequests = requests.Count;
            _logger.LogInformation("Parsed {Count} request files for job {JobId}", requests.Count, jobId);

            // Phase 2: Execute requests
            job.Status = RequestComparisonStatus.Executing;
            job.StatusMessage = "Executing requests...";

            var executionProgress = new Progress<(int Completed, int Total, string Message)>(p =>
            {
                job.CompletedRequests = p.Completed;
                job.StatusMessage = p.Message;
                progress?.Report(p);
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

            // Phase 3: Compare responses
            job.Status = RequestComparisonStatus.Comparing;
            job.StatusMessage = "Comparing responses...";
            progress?.Report((job.TotalRequests, job.TotalRequests, "Comparing responses..."));

            var comparisonProgress = new Progress<ComparisonProgress>(p =>
            {
                job.StatusMessage = p.Status;
                progress?.Report((p.Completed, p.Total, p.Status));
            });

            using var scope = _scopeFactory.CreateScope();
            
            // Apply per-job configuration settings
            var configService = scope.ServiceProvider.GetRequiredService<IComparisonConfigurationService>();

            // todo: hardcoded for xml?!?!?!?

            var xmlDeserializationService = scope.ServiceProvider.GetRequiredService<IXmlDeserializationService>();
            
            ApplyJobConfiguration(job, configService, xmlDeserializationService);
            
            var comparisonService = scope.ServiceProvider.GetRequiredService<DirectoryComparisonService>();

            var comparisonResult = await comparisonService.CompareDirectoriesAsync(
                job.ResponsePathA!,
                job.ResponsePathB!,
                job.ModelName,
                includeAllFiles: true,
                enablePatternAnalysis: true,
                enableSemanticAnalysis: job.EnableSemanticAnalysis,
                progress: comparisonProgress,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            // Store execution metadata in result
            comparisonResult.Metadata["RequestComparisonJobId"] = jobId;
            comparisonResult.Metadata["ExecutionResults"] = executionResults
                .Where(r => !r.Success)
                .Select(r => new { r.Request.RelativePath, r.ErrorMessage })
                .ToList();

            _results[jobId] = comparisonResult;

            // Phase 4: Complete
            job.Status = RequestComparisonStatus.Completed;
            job.StatusMessage = "Comparison completed";
            progress?.Report((job.TotalRequests, job.TotalRequests, "Comparison completed"));

            _logger.LogInformation("Completed request comparison job {JobId}", jobId);
        }
        catch (OperationCanceledException)
        {
            job.Status = RequestComparisonStatus.Cancelled;
            job.StatusMessage = "Job was cancelled";
            _logger.LogWarning("Request comparison job {JobId} was cancelled", jobId);
            throw;
        }
        catch (Exception ex)
        {
            job.Status = RequestComparisonStatus.Failed;
            job.ErrorMessage = ex.Message;
            job.StatusMessage = $"Failed: {ex.Message}";
            _logger.LogError(ex, "Request comparison job {JobId} failed", jobId);
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
