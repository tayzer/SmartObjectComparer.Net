// <copyright file="ComparisonResultCacheService.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace ComparisonTool.Core.Comparison {
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

    /// <summary>
    /// Cached comparison result with metadata.
    /// </summary>
    public class CachedComparisonResult {
        public ComparisonResult Result {
            get; set;
        }

        public DateTime CachedAt {
            get; set;
        }

        public string ConfigurationFingerprint {
            get; set;
        }

        public long ApproximateMemorySize {
            get; set;
        }

        public CachedComparisonResult(ComparisonResult result, string configFingerprint) {
            this.Result = result;
            this.CachedAt = DateTime.UtcNow;
            this.ConfigurationFingerprint = configFingerprint;

            // Rough memory estimation: differences count * average difference size
            this.ApproximateMemorySize = result?.Differences?.Count * 500 ?? 1000;
        }
    }

    /// <summary>
    /// Cached deserialized object with metadata.
    /// </summary>
    public class CachedDeserializedObject {
        public object Object {
            get; set;
        }

        public DateTime CachedAt {
            get; set;
        }

        public DateTime FileLastModified {
            get; set;
        }

        public string FileHash {
            get; set;
        }

        public long ApproximateMemorySize {
            get; set;
        }

        public CachedDeserializedObject(object obj, DateTime fileLastModified, string fileHash, long estimatedSize = 50000) {
            this.Object = obj;
            this.CachedAt = DateTime.UtcNow;
            this.FileLastModified = fileLastModified;
            this.FileHash = fileHash;
            this.ApproximateMemorySize = estimatedSize;
        }
    }

    /// <summary>
    /// Service for caching comparison results and deserialized objects to improve performance on re-runs.
    /// </summary>
    public class ComparisonResultCacheService {
        private readonly ILogger logger;
        private readonly PerformanceTracker performanceTracker;

        // Application session identifier to invalidate cache on restart
        private static readonly string AppSessionId = Guid.NewGuid().ToString("N")[..8];

        // Cache for comparison results: key = fileHash1 + fileHash2 + configHash
        private readonly ConcurrentDictionary<string, CachedComparisonResult> comparisonCache = new();

        // Cache for deserialized objects: key = filePath + lastModified
        private readonly ConcurrentDictionary<string, CachedDeserializedObject> objectCache = new();

        // Cache for configuration fingerprints: key = rules + settings serialized
        private readonly ConcurrentDictionary<string, string> configFingerprintCache = new();

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

        public ComparisonResultCacheService(ILogger logger = null, PerformanceTracker performanceTracker = null) {
            this.logger = logger ?? NullLogger.Instance;
            this.performanceTracker = performanceTracker ?? new PerformanceTracker(this.logger as ILogger<PerformanceTracker> ?? NullLogger<PerformanceTracker>.Instance);
        }

        /// <summary>
        /// Get cache statistics for monitoring and debugging.
        /// </summary>
        /// <returns></returns>
        public (long Hits, long Misses, double HitRatio, int ComparisonEntries, int ObjectEntries, long EstimatedMemory) GetCacheStatistics() {
            var total = this.totalCacheHits + this.totalCacheMisses;
            var hitRatio = total > 0 ? (double)this.totalCacheHits / total : 0.0;

            var estimatedMemory = this.comparisonCache.Values.Sum(c => c.ApproximateMemorySize) +
                                this.objectCache.Values.Sum(o => o.ApproximateMemorySize);

            return (this.totalCacheHits, this.totalCacheMisses, hitRatio, this.comparisonCache.Count, this.objectCache.Count, estimatedMemory);
        }

        /// <summary>
        /// Generate a configuration fingerprint for caching based on ignore rules and global settings.
        /// </summary>
        /// <returns></returns>
        public string GenerateConfigurationFingerprint(IComparisonConfigurationService configService) {
            return this.performanceTracker.TrackOperation("Generate_Config_Fingerprint", () => {
                try {
                    var config = new {
                        GlobalIgnoreCollectionOrder = configService.GetIgnoreCollectionOrder(),
                        GlobalIgnoreStringCase = configService.GetIgnoreStringCase(),
                        IgnoreRules = configService.GetIgnoreRules()
                            .OrderBy(r => r.PropertyPath) // Ensure consistent ordering
                            .Select(r => new { r.PropertyPath, r.IgnoreCompletely, r.IgnoreCollectionOrder })
                            .ToList(),
                        SmartIgnoreRules = configService.GetSmartIgnoreRules()
                            .Where(r => r.IsEnabled)
                            .OrderBy(r => r.Type).ThenBy(r => r.Value)
                            .Select(r => new { r.Type, r.Value, r.IsEnabled })
                            .ToList(),
                    };

                    var configJson = JsonSerializer.Serialize(config, new JsonSerializerOptions {
                        WriteIndented = false,
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    });

                    // Check cache first
                    if (this.configFingerprintCache.TryGetValue(configJson, out var cachedFingerprint)) {
                        return cachedFingerprint;
                    }

                    // Generate new fingerprint
                    using var sha256 = SHA256.Create();
                    var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(configJson));
                    var fingerprint = Convert.ToBase64String(hashBytes)[..16]; // Use first 16 chars

                    // Cache the fingerprint (with size limit)
                    if (this.configFingerprintCache.Count < 100) {
                        this.configFingerprintCache.TryAdd(configJson, fingerprint);
                    }

                    return fingerprint;
                }
                catch (Exception ex) {
                    this.logger.LogWarning(ex, "Error generating configuration fingerprint, using timestamp");
                    return DateTime.UtcNow.Ticks.ToString();
                }
            });
        }

        /// <summary>
        /// Generate a file content hash for caching.
        /// </summary>
        /// <returns></returns>
        public string GenerateFileHash(Stream fileStream) {
            return this.performanceTracker.TrackOperation("Generate_File_Hash", () => {
                try {
                    fileStream.Position = 0;
                    using var sha256 = SHA256.Create();
                    var hashBytes = sha256.ComputeHash(fileStream);
                    fileStream.Position = 0; // Reset for subsequent use
                    return Convert.ToBase64String(hashBytes)[..16]; // Use first 16 chars for shorter keys
                }
                catch (Exception ex) {
                    this.logger.LogWarning(ex, "Error generating file hash, using length + timestamp");
                    return $"{fileStream.Length}_{DateTime.UtcNow.Ticks}";
                }
            });
        }

        /// <summary>
        /// Try to get a cached comparison result.
        /// </summary>
        /// <returns></returns>
        public bool TryGetCachedComparison(string file1Hash, string file2Hash, string configFingerprint, out ComparisonResult result) {
            result = null;

            var cacheKey = $"{file1Hash}_{file2Hash}_{configFingerprint}";

            if (this.comparisonCache.TryGetValue(cacheKey, out var cached)) {
                // Check if cache entry is still valid
                if (DateTime.UtcNow - cached.CachedAt < this.comparisonCacheExpiration) {
                    result = cached.Result;
                    Interlocked.Increment(ref this.totalCacheHits);
                    this.logger.LogDebug("Cache HIT for comparison: {CacheKey}", cacheKey);
                    return true;
                }
                else {
                    // Remove expired entry
                    this.comparisonCache.TryRemove(cacheKey, out _);
                    Interlocked.Increment(ref this.totalCacheEvictions);
                    this.logger.LogDebug("Cache entry expired and removed: {CacheKey}", cacheKey);
                }
            }

            Interlocked.Increment(ref this.totalCacheMisses);
            this.logger.LogDebug("Cache MISS for comparison: {CacheKey}", cacheKey);
            return false;
        }

        /// <summary>
        /// Cache a comparison result.
        /// </summary>
        public void CacheComparison(string file1Hash, string file2Hash, string configFingerprint, ComparisonResult result) {
            if (result == null) {
                return;
            }

            // Ensure cached results are normalized and duplicate differences removed
            result = ComparisonTool.Core.Comparison.Utilities.DifferenceFilter.FilterDuplicateDifferences(result, this.logger);

            var cacheKey = $"{file1Hash}_{file2Hash}_{configFingerprint}";
            var cachedResult = new CachedComparisonResult(result, configFingerprint);

            // Check cache size limits before adding
            if (this.comparisonCache.Count >= this.maxComparisonCacheEntries) {
                this.CleanupComparisonCache();
            }

            this.comparisonCache.TryAdd(cacheKey, cachedResult);
            this.logger.LogDebug("Cached comparison result: {CacheKey}", cacheKey);

            // Periodic cleanup
            if (DateTime.UtcNow - this.lastCleanup > TimeSpan.FromMinutes(15)) {
                _ = Task.Run(this.CleanupCaches); // Run cleanup in background
            }
        }

        /// <summary>
        /// Try to get a cached deserialized object.
        /// </summary>
        /// <returns></returns>
        public bool TryGetCachedObject<T>(string filePath, DateTime fileLastModified, out T cachedObject)
            where T : class {
            cachedObject = null;

            var cacheKey = $"{filePath}_{fileLastModified.Ticks}_{AppSessionId}";

            if (this.objectCache.TryGetValue(cacheKey, out var cached)) {
                // Check if cache entry is still valid and file hasn't changed
                if (DateTime.UtcNow - cached.CachedAt < this.objectCacheExpiration &&
                    cached.FileLastModified == fileLastModified) {
                    cachedObject = cached.Object as T;
                    if (cachedObject != null) {
                        Interlocked.Increment(ref this.totalCacheHits);
                        this.logger.LogDebug("Object cache HIT for: {FilePath}", filePath);
                        return true;
                    }
                }
                else {
                    // Remove expired or invalid entry
                    this.objectCache.TryRemove(cacheKey, out _);
                    Interlocked.Increment(ref this.totalCacheEvictions);
                }
            }

            Interlocked.Increment(ref this.totalCacheMisses);
            this.logger.LogDebug("Object cache MISS for: {FilePath}", filePath);
            return false;
        }

        /// <summary>
        /// Cache a deserialized object.
        /// </summary>
        public void CacheObject(string filePath, DateTime fileLastModified, object obj, long estimatedSize = 50000) {
            if (obj == null) {
                return;
            }

            var cacheKey = $"{filePath}_{fileLastModified.Ticks}_{AppSessionId}";
            var cachedObject = new CachedDeserializedObject(obj, fileLastModified, string.Empty, estimatedSize);

            // Check cache size limits before adding
            if (this.objectCache.Count >= this.maxObjectCacheEntries) {
                this.CleanupObjectCache();
            }

            this.objectCache.TryAdd(cacheKey, cachedObject);
            this.logger.LogDebug("Cached deserialized object: {FilePath}", filePath);
        }

        /// <summary>
        /// Clear all caches (useful when memory pressure is high).
        /// </summary>
        public void ClearAllCaches() {
            var comparisonCount = this.comparisonCache.Count;
            var objectCount = this.objectCache.Count;

            this.comparisonCache.Clear();
            this.objectCache.Clear();
            this.configFingerprintCache.Clear();

            this.logger.LogInformation(
                "Cleared all caches: {ComparisonEntries} comparison entries, {ObjectEntries} object entries",
                comparisonCount, objectCount);
        }

        /// <summary>
        /// Clear all cached objects - useful when XML serialization logic has been updated
        /// or when encountering inconsistent results between single file and folder comparisons.
        /// </summary>
        public void ClearObjectCache() {
            this.objectCache.Clear();
            Interlocked.Exchange(ref this.totalCacheEvictions, this.objectCache.Count);
            this.logger.LogInformation("Cleared all cached deserialized objects");
        }

        /// <summary>
        /// Invalidate cached comparisons that used a different configuration.
        /// </summary>
        public void InvalidateConfigurationChanges(string newConfigFingerprint) {
            var toRemove = this.comparisonCache
                .Where(kvp => kvp.Value != null && kvp.Value.ConfigurationFingerprint != newConfigFingerprint)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in toRemove) {
                this.comparisonCache.TryRemove(key, out _);
                Interlocked.Increment(ref this.totalCacheEvictions);
            }

            if (toRemove.Count > 0) {
                this.logger.LogInformation("Invalidated {Count} cached comparisons due to configuration changes", toRemove.Count);
            }
        }

        /// <summary>
        /// Clean up expired comparison cache entries.
        /// </summary>
        private void CleanupComparisonCache() {
            var cutoff = DateTime.UtcNow - this.comparisonCacheExpiration;
            var toRemove = this.comparisonCache
                .Where(kvp => kvp.Value != null && kvp.Value.CachedAt < cutoff)
                .Select(kvp => kvp.Key)
                .ToList();

            // If not enough expired entries, remove oldest ones
            if (toRemove.Count < this.comparisonCache.Count / 4) {
                var oldestEntries = this.comparisonCache
                    .Where(kvp => kvp.Value != null)
                    .OrderBy(kvp => kvp.Value.CachedAt)
                    .Take(this.comparisonCache.Count / 4)
                    .Select(kvp => kvp.Key);

                toRemove.AddRange(oldestEntries);
            }

            foreach (var key in toRemove.Distinct()) {
                this.comparisonCache.TryRemove(key, out _);
                Interlocked.Increment(ref this.totalCacheEvictions);
            }

            this.logger.LogDebug("Cleaned up {Count} comparison cache entries", toRemove.Count);
        }

        /// <summary>
        /// Clean up expired object cache entries.
        /// </summary>
        private void CleanupObjectCache() {
            var cutoff = DateTime.UtcNow - this.objectCacheExpiration;
            var toRemove = this.objectCache
                .Where(kvp => kvp.Value != null && kvp.Value.CachedAt < cutoff)
                .Select(kvp => kvp.Key)
                .ToList();

            // If not enough expired entries, remove oldest ones
            if (toRemove.Count < this.objectCache.Count / 4) {
                var oldestEntries = this.objectCache
                    .Where(kvp => kvp.Value != null)
                    .OrderBy(kvp => kvp.Value.CachedAt)
                    .Take(this.objectCache.Count / 4)
                    .Select(kvp => kvp.Key);

                toRemove.AddRange(oldestEntries);
            }

            foreach (var key in toRemove.Distinct()) {
                this.objectCache.TryRemove(key, out _);
                Interlocked.Increment(ref this.totalCacheEvictions);
            }

            this.logger.LogDebug("Cleaned up {Count} object cache entries", toRemove.Count);
        }

        /// <summary>
        /// Clean up all caches (removes expired entries and enforces memory limits).
        /// </summary>
        private void CleanupCaches() {
            try {
                this.lastCleanup = DateTime.UtcNow;

                this.CleanupComparisonCache();
                this.CleanupObjectCache();

                // Check memory usage and clean more aggressively if needed
                var estimatedMemory = this.comparisonCache.Values.Where(c => c != null).Sum(c => c.ApproximateMemorySize) +
                                    this.objectCache.Values.Where(o => o != null).Sum(o => o.ApproximateMemorySize);

                if (estimatedMemory > this.maxCacheMemoryBytes) {
                    this.logger.LogWarning(
                        "Cache memory usage ({MemoryMB}MB) exceeds limit ({LimitMB}MB), performing aggressive cleanup",
                        estimatedMemory / (1024 * 1024), this.maxCacheMemoryBytes / (1024 * 1024));

                    // Remove half the entries, starting with largest and oldest
                    var comparisonToRemove = this.comparisonCache
                        .Where(kvp => kvp.Value != null)
                        .OrderByDescending(kvp => kvp.Value.ApproximateMemorySize)
                        .ThenBy(kvp => kvp.Value.CachedAt)
                        .Take(this.comparisonCache.Count / 2)
                        .Select(kvp => kvp.Key)
                        .ToList();

                    foreach (var key in comparisonToRemove) {
                        this.comparisonCache.TryRemove(key, out _);
                    }

                    var objectToRemove = this.objectCache
                        .Where(kvp => kvp.Value != null)
                        .OrderByDescending(kvp => kvp.Value.ApproximateMemorySize)
                        .ThenBy(kvp => kvp.Value.CachedAt)
                        .Take(this.objectCache.Count / 2)
                        .Select(kvp => kvp.Key)
                        .ToList();

                    foreach (var key in objectToRemove) {
                        this.objectCache.TryRemove(key, out _);
                    }

                    this.logger.LogInformation(
                        "Aggressive cleanup removed {ComparisonCount} comparison and {ObjectCount} object cache entries",
                        comparisonToRemove.Count, objectToRemove.Count);
                }

                // Log cache statistics periodically
                var stats = this.GetCacheStatistics();
                this.logger.LogDebug(
                    "Cache stats - Hits: {Hits}, Misses: {Misses}, Hit Ratio: {HitRatio:P2}, Memory: {MemoryMB}MB",
                    stats.Hits, stats.Misses, stats.HitRatio, stats.EstimatedMemory / (1024 * 1024));
            }
            catch (Exception ex) {
                this.logger.LogError(ex, "Error during cache cleanup");
            }
        }
    }
}
