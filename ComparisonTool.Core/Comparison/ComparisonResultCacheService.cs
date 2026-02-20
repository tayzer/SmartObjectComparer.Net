using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ComparisonTool.Core.Comparison.Configuration;
using ComparisonTool.Core.Utilities;
using KellermanSoftware.CompareNetObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ComparisonTool.Core.Comparison;

/// <summary>
/// Cached comparison result with metadata.
/// </summary>
public class CachedComparisonResult
{
    public CachedComparisonResult(ComparisonResult result, string configFingerprint)
    {
        Result = result;
        CachedAt = DateTime.UtcNow;
        ConfigurationFingerprint = configFingerprint;

        // Rough memory estimation: differences count * average difference size
        ApproximateMemorySize = result?.Differences?.Count * 500 ?? 1000;
    }

    public ComparisonResult Result
    {
        get; set;
    }

    public DateTime CachedAt
    {
        get; set;
    }

    public string ConfigurationFingerprint
    {
        get; set;
    }

    public long ApproximateMemorySize
    {
        get; set;
    }
}

/// <summary>
/// Cached deserialized object with metadata.
/// </summary>
public class CachedDeserializedObject
{
    public CachedDeserializedObject(object obj, DateTime fileLastModified, string fileHash, long estimatedSize = 50000)
    {
        Object = obj;
        CachedAt = DateTime.UtcNow;
        FileLastModified = fileLastModified;
        FileHash = fileHash;
        ApproximateMemorySize = estimatedSize;
    }

    public object Object
    {
        get; set;
    }

    public DateTime CachedAt
    {
        get; set;
    }

    public DateTime FileLastModified
    {
        get; set;
    }

    public string FileHash
    {
        get; set;
    }

    public long ApproximateMemorySize
    {
        get; set;
    }
}

/// <summary>
/// Service for caching comparison results and deserialized objects to improve performance on re-runs.
/// </summary>
public class ComparisonResultCacheService
{
    // Application session identifier to invalidate cache on restart
    private static readonly string AppSessionId = Guid.NewGuid().ToString("N")[..8];

    private readonly ILogger logger;
    private readonly PerformanceTracker performanceTracker;

    // Cache for comparison results: key = fileHash1 + fileHash2 + configHash
    private readonly ConcurrentDictionary<string, CachedComparisonResult> comparisonCache = new ConcurrentDictionary<string, CachedComparisonResult>(StringComparer.Ordinal);

    // Cache for deserialized objects: key = filePath + lastModified
    private readonly ConcurrentDictionary<string, CachedDeserializedObject> objectCache = new ConcurrentDictionary<string, CachedDeserializedObject>(StringComparer.Ordinal);

    // Cache for configuration fingerprints: key = rules + settings serialized
    private readonly ConcurrentDictionary<string, string> configFingerprintCache = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

    // Configuration
    private readonly TimeSpan comparisonCacheExpiration = TimeSpan.FromHours(24);
    private readonly TimeSpan objectCacheExpiration = TimeSpan.FromHours(8);
    private readonly long maxCacheMemoryBytes = 500 * 1024 * 1024; // 500MB limit
    private readonly int maxComparisonCacheEntries = 10000;
    private readonly int maxObjectCacheEntries = 2000;

    // Statistics
    private long totalCacheHits = 0;
    private long totalCacheMisses = 0;
    private long totalCacheEvictions = 0;
    private DateTime lastCleanup = DateTime.UtcNow;

    public ComparisonResultCacheService(ILogger? logger = null, PerformanceTracker? performanceTracker = null)
    {
        this.logger = logger ?? NullLogger.Instance;
        this.performanceTracker = performanceTracker ?? new PerformanceTracker(this.logger as ILogger<PerformanceTracker> ?? NullLogger<PerformanceTracker>.Instance);
    }

    /// <summary>
    /// Get cache statistics for monitoring and debugging.
    /// </summary>
    /// <returns></returns>
    public (long Hits, long Misses, double HitRatio, int ComparisonEntries, int ObjectEntries, long EstimatedMemory) GetCacheStatistics()
    {
        var total = totalCacheHits + totalCacheMisses;
        var hitRatio = total > 0 ? (double)totalCacheHits / total : 0.0;

        var estimatedMemory = comparisonCache.Values.Sum(c => c.ApproximateMemorySize) +
                            objectCache.Values.Sum(o => o.ApproximateMemorySize);

        return (totalCacheHits, totalCacheMisses, hitRatio, comparisonCache.Count, objectCache.Count, estimatedMemory);
    }

    /// <summary>
    /// Generate a configuration fingerprint for caching based on ignore rules and global settings.
    /// </summary>
    /// <returns></returns>
    public string GenerateConfigurationFingerprint(IComparisonConfigurationService configService) =>
        performanceTracker.TrackOperation("Generate_Config_Fingerprint", () =>
        {
            try
            {
                var config = new
                {
                    GlobalIgnoreCollectionOrder = configService.GetIgnoreCollectionOrder(),
                    GlobalIgnoreStringCase = configService.GetIgnoreStringCase(),
                    GlobalIgnoreTrailingWhitespaceAtEnd = configService.GetIgnoreTrailingWhitespaceAtEnd(),
                    IgnoreRules = configService.GetIgnoreRules()
                        .OrderBy(r => r.PropertyPath, StringComparer.Ordinal) // Ensure consistent ordering
                        .Select(r => new { r.PropertyPath, r.IgnoreCompletely, r.IgnoreCollectionOrder })
                        .ToList(),
                    SmartIgnoreRules = configService.GetSmartIgnoreRules()
                        .Where(r => r.IsEnabled)
                        .OrderBy(r => r.Type).ThenBy(r => r.Value, StringComparer.Ordinal)
                        .Select(r => new { r.Type, r.Value, r.IsEnabled })
                        .ToList(),
                };

                var configJson = JsonSerializer.Serialize(config, new JsonSerializerOptions
                {
                    WriteIndented = false,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                });

                // Check cache first
                if (configFingerprintCache.TryGetValue(configJson, out var cachedFingerprint))
                {
                    return cachedFingerprint;
                }

                // Generate new fingerprint
                using var sha256 = SHA256.Create();
                var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(configJson));
                var fingerprint = Convert.ToBase64String(hashBytes)[..16]; // Use first 16 chars

                // Cache the fingerprint (with size limit)
                if (configFingerprintCache.Count < 100)
                {
                    configFingerprintCache.TryAdd(configJson, fingerprint);
                }

                return fingerprint;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error generating configuration fingerprint, using timestamp");
                return DateTime.UtcNow.Ticks.ToString();
            }
        });

    /// <summary>
    /// Generate a file content hash for caching.
    /// PERFORMANCE OPTIMIZATION: Use XxHash64 for 2-3x faster hashing than SHA256.
    /// </summary>
    /// <returns></returns>
    public string GenerateFileHash(Stream fileStream) =>
        performanceTracker.TrackOperation("Generate_File_Hash", () =>
        {
            try
            {
                fileStream.Position = 0;

                // PERFORMANCE OPTIMIZATION: Use XxHash64 instead of SHA256 (much faster, non-cryptographic)
                var hash = new System.IO.Hashing.XxHash64();
                const int bufferSize = 81920; // 80KB buffer
                var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(bufferSize);

                try
                {
                    int bytesRead;
                    while ((bytesRead = fileStream.Read(buffer, 0, bufferSize)) > 0)
                    {
                        hash.Append(buffer.AsSpan(0, bytesRead));
                    }

                    fileStream.Position = 0; // Reset for subsequent use
                    return Convert.ToBase64String(hash.GetHashAndReset());
                }
                finally
                {
                    System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error generating file hash, using length + timestamp");
                return $"{fileStream.Length}_{DateTime.UtcNow.Ticks}";
            }
        });

    /// <summary>
    /// Try to get a cached comparison result.
    /// </summary>
    /// <returns></returns>
    public bool TryGetCachedComparison(string file1Hash, string file2Hash, string configFingerprint, out ComparisonResult result)
    {
        result = null;

        var cacheKey = $"{file1Hash}_{file2Hash}_{configFingerprint}";

        if (comparisonCache.TryGetValue(cacheKey, out var cached))
        {
            // Check if cache entry is still valid
            if (DateTime.UtcNow - cached.CachedAt < comparisonCacheExpiration)
            {
                result = cached.Result;
                Interlocked.Increment(ref totalCacheHits);
                logger.LogDebug("Cache HIT for comparison: {CacheKey}", cacheKey);
                return true;
            }
            else
            {
                // Remove expired entry
                comparisonCache.TryRemove(cacheKey, out _);
                Interlocked.Increment(ref totalCacheEvictions);
                logger.LogDebug("Cache entry expired and removed: {CacheKey}", cacheKey);
            }
        }

        Interlocked.Increment(ref totalCacheMisses);
        logger.LogDebug("Cache MISS for comparison: {CacheKey}", cacheKey);
        return false;
    }

    /// <summary>
    /// Cache a comparison result.
    /// </summary>
    public void CacheComparison(string file1Hash, string file2Hash, string configFingerprint, ComparisonResult result)
    {
        if (result == null)
        {
            return;
        }

        // Ensure cached results are normalized and duplicate differences removed
        result = ComparisonTool.Core.Comparison.Utilities.DifferenceFilter.FilterDuplicateDifferences(result, logger);

        var cacheKey = $"{file1Hash}_{file2Hash}_{configFingerprint}";
        var cachedResult = new CachedComparisonResult(result, configFingerprint);

        // Check cache size limits before adding
        if (comparisonCache.Count >= maxComparisonCacheEntries)
        {
            CleanupComparisonCache();
        }

        comparisonCache.TryAdd(cacheKey, cachedResult);
        logger.LogDebug("Cached comparison result: {CacheKey}", cacheKey);

        // Periodic cleanup
        if (DateTime.UtcNow - lastCleanup > TimeSpan.FromMinutes(15))
        {
            _ = Task.Run(CleanupCaches); // Run cleanup in background
        }
    }

    /// <summary>
    /// Try to get a cached deserialized object.
    /// </summary>
    /// <returns></returns>
    public bool TryGetCachedObject<T>(string filePath, DateTime fileLastModified, out T cachedObject)
        where T : class
    {
        cachedObject = null;

        var cacheKey = $"{filePath}_{fileLastModified.Ticks}_{AppSessionId}";

        if (objectCache.TryGetValue(cacheKey, out var cached))
        {
            // Check if cache entry is still valid and file hasn't changed
            if (DateTime.UtcNow - cached.CachedAt < objectCacheExpiration &&
                cached.FileLastModified == fileLastModified)
            {
                cachedObject = cached.Object as T;
                if (cachedObject != null)
                {
                    Interlocked.Increment(ref totalCacheHits);
                    logger.LogDebug("Object cache HIT for: {FilePath}", filePath);
                    return true;
                }
            }
            else
            {
                // Remove expired or invalid entry
                objectCache.TryRemove(cacheKey, out _);
                Interlocked.Increment(ref totalCacheEvictions);
            }
        }

        Interlocked.Increment(ref totalCacheMisses);
        logger.LogDebug("Object cache MISS for: {FilePath}", filePath);
        return false;
    }

    /// <summary>
    /// Cache a deserialized object.
    /// </summary>
    public void CacheObject(string filePath, DateTime fileLastModified, object obj, long estimatedSize = 50000)
    {
        if (obj == null)
        {
            return;
        }

        var cacheKey = $"{filePath}_{fileLastModified.Ticks}_{AppSessionId}";
        var cachedObject = new CachedDeserializedObject(obj, fileLastModified, string.Empty, estimatedSize);

        // Check cache size limits before adding
        if (objectCache.Count >= maxObjectCacheEntries)
        {
            CleanupObjectCache();
        }

        objectCache.TryAdd(cacheKey, cachedObject);
        logger.LogDebug("Cached deserialized object: {FilePath}", filePath);
    }

    /// <summary>
    /// Clear all caches (useful when memory pressure is high).
    /// </summary>
    public void ClearAllCaches()
    {
        var comparisonCount = comparisonCache.Count;
        var objectCount = objectCache.Count;

        comparisonCache.Clear();
        objectCache.Clear();
        configFingerprintCache.Clear();

        logger.LogInformation(
            "Cleared all caches: {ComparisonEntries} comparison entries, {ObjectEntries} object entries",
            comparisonCount,
            objectCount);
    }

    /// <summary>
    /// Clear all cached objects - useful when XML serialization logic has been updated
    /// or when encountering inconsistent results between single file and folder comparisons.
    /// </summary>
    public void ClearObjectCache()
    {
        objectCache.Clear();
        Interlocked.Exchange(ref totalCacheEvictions, objectCache.Count);
        logger.LogInformation("Cleared all cached deserialized objects");
    }

    /// <summary>
    /// Invalidate cached comparisons that used a different configuration.
    /// </summary>
    public void InvalidateConfigurationChanges(string newConfigFingerprint)
    {
        var toRemove = comparisonCache
            .Where(kvp => kvp.Value != null && !string.Equals(kvp.Value.ConfigurationFingerprint, newConfigFingerprint, StringComparison.Ordinal))
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in toRemove)
        {
            comparisonCache.TryRemove(key, out _);
            Interlocked.Increment(ref totalCacheEvictions);
        }

        if (toRemove.Count > 0)
        {
            logger.LogInformation("Invalidated {Count} cached comparisons due to configuration changes", toRemove.Count);
        }
    }

    /// <summary>
    /// Clean up expired comparison cache entries.
    /// </summary>
    private void CleanupComparisonCache()
    {
        var cutoff = DateTime.UtcNow - comparisonCacheExpiration;
        var toRemove = comparisonCache
            .Where(kvp => kvp.Value != null && kvp.Value.CachedAt < cutoff)
            .Select(kvp => kvp.Key)
            .ToList();

        // If not enough expired entries, remove oldest ones
        if (toRemove.Count < comparisonCache.Count / 4)
        {
            var oldestEntries = comparisonCache
                .Where(kvp => kvp.Value != null)
                .OrderBy(kvp => kvp.Value.CachedAt)
                .Take(comparisonCache.Count / 4)
                .Select(kvp => kvp.Key);

            toRemove.AddRange(oldestEntries);
        }

        foreach (var key in toRemove.Distinct(StringComparer.Ordinal))
        {
            comparisonCache.TryRemove(key, out _);
            Interlocked.Increment(ref totalCacheEvictions);
        }

        logger.LogDebug("Cleaned up {Count} comparison cache entries", toRemove.Count);
    }

    /// <summary>
    /// Clean up expired object cache entries.
    /// </summary>
    private void CleanupObjectCache()
    {
        var cutoff = DateTime.UtcNow - objectCacheExpiration;
        var toRemove = objectCache
            .Where(kvp => kvp.Value != null && kvp.Value.CachedAt < cutoff)
            .Select(kvp => kvp.Key)
            .ToList();

        // If not enough expired entries, remove oldest ones
        if (toRemove.Count < objectCache.Count / 4)
        {
            var oldestEntries = objectCache
                .Where(kvp => kvp.Value != null)
                .OrderBy(kvp => kvp.Value.CachedAt)
                .Take(objectCache.Count / 4)
                .Select(kvp => kvp.Key);

            toRemove.AddRange(oldestEntries);
        }

        foreach (var key in toRemove.Distinct(StringComparer.Ordinal))
        {
            objectCache.TryRemove(key, out _);
            Interlocked.Increment(ref totalCacheEvictions);
        }

        logger.LogDebug("Cleaned up {Count} object cache entries", toRemove.Count);
    }

    /// <summary>
    /// Clean up all caches (removes expired entries and enforces memory limits).
    /// </summary>
    private void CleanupCaches()
    {
        try
        {
            lastCleanup = DateTime.UtcNow;

            CleanupComparisonCache();
            CleanupObjectCache();

            // Check memory usage and clean more aggressively if needed
            var estimatedMemory = comparisonCache.Values.Where(c => c != null).Sum(c => c.ApproximateMemorySize) +
                                objectCache.Values.Where(o => o != null).Sum(o => o.ApproximateMemorySize);

            if (estimatedMemory > maxCacheMemoryBytes)
            {
                logger.LogWarning(
                    "Cache memory usage ({MemoryMB}MB) exceeds limit ({LimitMB}MB), performing aggressive cleanup",
                    estimatedMemory / (1024 * 1024),
                    maxCacheMemoryBytes / (1024 * 1024));

                // Remove half the entries, starting with largest and oldest
                var comparisonToRemove = comparisonCache
                    .Where(kvp => kvp.Value != null)
                    .OrderByDescending(kvp => kvp.Value.ApproximateMemorySize)
                    .ThenBy(kvp => kvp.Value.CachedAt)
                    .Take(comparisonCache.Count / 2)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in comparisonToRemove)
                {
                    comparisonCache.TryRemove(key, out _);
                }

                var objectToRemove = objectCache
                    .Where(kvp => kvp.Value != null)
                    .OrderByDescending(kvp => kvp.Value.ApproximateMemorySize)
                    .ThenBy(kvp => kvp.Value.CachedAt)
                    .Take(objectCache.Count / 2)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in objectToRemove)
                {
                    objectCache.TryRemove(key, out _);
                }

                logger.LogInformation(
                    "Aggressive cleanup removed {ComparisonCount} comparison and {ObjectCount} object cache entries",
                    comparisonToRemove.Count,
                    objectToRemove.Count);
            }

            // Log cache statistics periodically
            var stats = GetCacheStatistics();
            logger.LogDebug(
                "Cache stats - Hits: {Hits}, Misses: {Misses}, Hit Ratio: {HitRatio:P2}, Memory: {MemoryMB}MB",
                stats.Hits,
                stats.Misses,
                stats.HitRatio,
                stats.EstimatedMemory / (1024 * 1024));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during cache cleanup");
        }
    }
}
