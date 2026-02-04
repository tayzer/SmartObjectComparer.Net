using System.ComponentModel.DataAnnotations;

namespace ComparisonTool.Core.RequestComparison.Models;

/// <summary>
/// Request body for creating a new request comparison job.
/// </summary>
public record CreateRequestComparisonJobRequest
{
    /// <summary>Gets the batch ID from the request upload.</summary>
    [Required]
    public required string RequestBatchId { get; init; }

    /// <summary>Gets the first endpoint URL.</summary>
    [Required]
    [Url]
    public required string EndpointA { get; init; }

    /// <summary>Gets the second endpoint URL.</summary>
    [Required]
    [Url]
    public required string EndpointB { get; init; }

    /// <summary>Gets the headers for endpoint A.</summary>
    public Dictionary<string, string>? HeadersA { get; init; }

    /// <summary>Gets the headers for endpoint B.</summary>
    public Dictionary<string, string>? HeadersB { get; init; }

    /// <summary>Gets the content type override for request bodies.</summary>
    public string? ContentTypeOverride { get; init; }

    /// <summary>Gets the timeout in milliseconds for each request.</summary>
    [Range(1000, 300000)]
    public int TimeoutMs { get; init; } = 30000;

    /// <summary>Gets the maximum concurrent requests.</summary>
    [Range(1, 256)]
    public int MaxConcurrency { get; init; } = 64;

    /// <summary>Gets the model name to use for comparison.</summary>
    public string? ModelName { get; init; }

    // --- Comparison Configuration Parity with Home ---

    /// <summary>Gets a value indicating whether to ignore collection order during comparison.</summary>
    public bool IgnoreCollectionOrder { get; init; } = false;

    /// <summary>Gets a value indicating whether to ignore string case during comparison.</summary>
    public bool IgnoreStringCase { get; init; } = false;

    /// <summary>Gets a value indicating whether to ignore XML namespaces during deserialization.</summary>
    public bool IgnoreXmlNamespaces { get; init; } = true;

    /// <summary>Gets the ignore rules to apply during comparison.</summary>
    public List<IgnoreRuleDto>? IgnoreRules { get; init; }

    /// <summary>Gets the smart ignore rules to apply during comparison.</summary>
    public List<SmartIgnoreRuleDto>? SmartIgnoreRules { get; init; }

    /// <summary>Gets a value indicating whether to enable semantic analysis.</summary>
    public bool EnableSemanticAnalysis { get; init; } = true;

    /// <summary>Gets a value indicating whether to enable enhanced structural analysis.</summary>
    public bool EnableEnhancedStructuralAnalysis { get; init; } = true;
}

/// <summary>
/// DTO for ignore rules in API requests.
/// </summary>
public record IgnoreRuleDto
{
    /// <summary>Gets the property path to ignore.</summary>
    public string PropertyPath { get; init; } = string.Empty;

    /// <summary>Gets a value indicating whether to ignore the property completely.</summary>
    public bool IgnoreCompletely { get; init; } = false;

    /// <summary>Gets a value indicating whether to ignore collection order for this property.</summary>
    public bool IgnoreCollectionOrder { get; init; } = false;
}

/// <summary>
/// DTO for smart ignore rules in API requests.
/// </summary>
public record SmartIgnoreRuleDto
{
    /// <summary>Gets the type of smart ignore rule.</summary>
    public string Type { get; init; } = "PropertyName";

    /// <summary>Gets the value/pattern for the rule.</summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>Gets the description for the rule.</summary>
    public string? Description { get; init; }
}

/// <summary>
/// Response for job creation.
/// </summary>
public record CreateJobResponse
{
    /// <summary>Gets the created job ID.</summary>
    public required string JobId { get; init; }
}

/// <summary>
/// Response for job status.
/// </summary>
public record JobStatusResponse
{
    /// <summary>Gets the job status.</summary>
    public required string Status { get; init; }

    /// <summary>Gets the number of completed requests.</summary>
    public int Completed { get; init; }

    /// <summary>Gets the total number of requests.</summary>
    public int Total { get; init; }

    /// <summary>Gets the status message.</summary>
    public string? Message { get; init; }

    /// <summary>Gets the error message if failed.</summary>
    public string? Error { get; init; }
}

/// <summary>
/// Response for batch upload.
/// </summary>
public record RequestBatchUploadResponse
{
    /// <summary>Gets the number of uploaded files.</summary>
    public int Uploaded { get; init; }

    /// <summary>Gets the batch ID for large uploads.</summary>
    public string? BatchId { get; init; }

    /// <summary>Gets the list of files for small uploads.</summary>
    public List<string>? Files { get; init; }

    /// <summary>Gets the path to the file list for large uploads.</summary>
    public string? FileListPath { get; init; }

    /// <summary>Gets the cache key used for this upload.</summary>
    public string? CacheKey { get; init; }

    /// <summary>Gets a value indicating whether this upload was resolved from cache.</summary>
    public bool? CacheHit { get; init; }
}

/// <summary>
/// Response for batch cache lookup.
/// </summary>
public record RequestBatchCacheLookupResponse
{
    /// <summary>Gets a value indicating whether a cached batch was found.</summary>
    public bool Found { get; init; }

    /// <summary>Gets the cache key that was queried.</summary>
    public string? CacheKey { get; init; }

    /// <summary>Gets the cached batch ID if found.</summary>
    public string? BatchId { get; init; }

    /// <summary>Gets the number of files in the cached batch.</summary>
    public int Uploaded { get; init; }
}
