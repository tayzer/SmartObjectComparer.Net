using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KellermanSoftware.CompareNetObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ComparisonTool.Core.Comparison.Configuration;
using ComparisonTool.Core.Utilities;

namespace ComparisonTool.Core.Comparison
{
    /// <summary>
    /// Cached comparison result with metadata
    /// </summary>
    public class CachedComparisonResult
    {
        public ComparisonResult Result { get; set; }
        public DateTime CachedAt { get; set; }
        public string ConfigurationFingerprint { get; set; }
        public long ApproximateMemorySize { get; set; }
        
        public CachedComparisonResult(ComparisonResult result, string configFingerprint)
        {
            Result = result;
            CachedAt = DateTime.UtcNow;
            ConfigurationFingerprint = configFingerprint;
            // Rough memory estimation: differences count * average difference size
            ApproximateMemorySize = result?.Differences?.Count * 500 ?? 1000;
        }
    }

    /// <summary>
    /// Cached deserialized object with metadata
    /// </summary>
    public class CachedDeserializedObject
    {
        public object Object { get; set; }
        public DateTime CachedAt { get; set; }
        public DateTime FileLastModified { get; set; }
        public string FileHash { get; set; }
        public long ApproximateMemorySize { get; set; }
        
        public CachedDeserializedObject(object obj, DateTime fileLastModified, string fileHash, long estimatedSize = 50000)
        {
            Object = obj;
            CachedAt = DateTime.UtcNow;
            FileLastModified = fileLastModified;
            FileHash = fileHash;
            ApproximateMemorySize = estimatedSize;
        }
    }

    /// <summary>
    /// Service for caching comparison results and deserialized objects to improve performance on re-runs
    /// </summary>
    public class ComparisonResultCacheService
    {
        private readonly ILogger _logger;
        private readonly PerformanceTracker _performanceTracker;
        
        // Cache for comparison results: key = fileHash1 + fileHash2 + configHash
        private readonly ConcurrentDictionary<string, CachedComparisonResult> _comparisonCache = new();
        
        // Cache for deserialized objects: key = filePath + lastModified
        private readonly ConcurrentDictionary<string, CachedDeserializedObject> _objectCache = new();
        
        // Cache for configuration fingerprints: key = rules + settings serialized
        private readonly ConcurrentDictionary<string, string> _configFingerprintCache = new();
        
        // Configuration
        private readonly TimeSpan _comparisonCacheExpiration = TimeSpan.FromHours(24);
        private readonly TimeSpan _objectCacheExpiration = TimeSpan.FromHours(8);
        private readonly long _maxCacheMemoryBytes = 500 * 1024 * 1024; // 500MB limit
        private readonly int _maxComparisonCacheEntries = 10000;
        private readonly int _maxObjectCacheEntries = 2000;
        
        // Statistics
        private long _totalCacheHits = 0;
        private long _totalCacheMisses = 0;
        private long _totalCacheEvictions = 0;
        private DateTime _lastCleanup = DateTime.UtcNow;
        
        public ComparisonResultCacheService(ILogger logger = null, PerformanceTracker performanceTracker = null)
        {
            _logger = logger ?? NullLogger.Instance;
            _performanceTracker = performanceTracker ?? new PerformanceTracker(_logger as ILogger<PerformanceTracker> ?? NullLogger<PerformanceTracker>.Instance);
        }

        /// <summary>
        /// Get cache statistics for monitoring and debugging
        /// </summary>
        public (long Hits, long Misses, double HitRatio, int ComparisonEntries, int ObjectEntries, long EstimatedMemory) GetCacheStatistics()
        {
            var total = _totalCacheHits + _totalCacheMisses;
            var hitRatio = total > 0 ? (double)_totalCacheHits / total : 0.0;
            
            var estimatedMemory = _comparisonCache.Values.Sum(c => c.ApproximateMemorySize) +
                                _objectCache.Values.Sum(o => o.ApproximateMemorySize);
            
            return (_totalCacheHits, _totalCacheMisses, hitRatio, _comparisonCache.Count, _objectCache.Count, estimatedMemory);
        }

        /// <summary>
        /// Generate a configuration fingerprint for caching based on ignore rules and global settings
        /// </summary>
        public string GenerateConfigurationFingerprint(IComparisonConfigurationService configService)
        {
            return _performanceTracker.TrackOperation("Generate_Config_Fingerprint", () =>
            {
                try
                {
                    var config = new
                    {
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
                            .ToList()
                    };

                    var configJson = JsonSerializer.Serialize(config, new JsonSerializerOptions 
                    { 
                        WriteIndented = false,
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    });
                    
                    // Check cache first
                    if (_configFingerprintCache.TryGetValue(configJson, out var cachedFingerprint))
                    {
                        return cachedFingerprint;
                    }
                    
                    // Generate new fingerprint
                    using var sha256 = SHA256.Create();
                    var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(configJson));
                    var fingerprint = Convert.ToBase64String(hashBytes)[..16]; // Use first 16 chars
                    
                    // Cache the fingerprint (with size limit)
                    if (_configFingerprintCache.Count < 100)
                    {
                        _configFingerprintCache.TryAdd(configJson, fingerprint);
                    }
                    
                    return fingerprint;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error generating configuration fingerprint, using timestamp");
                    return DateTime.UtcNow.Ticks.ToString();
                }
            });
        }

        /// <summary>
        /// Generate a file content hash for caching
        /// </summary>
        public string GenerateFileHash(Stream fileStream)
        {
            return _performanceTracker.TrackOperation("Generate_File_Hash", () =>
            {
                try
                {
                    fileStream.Position = 0;
                    using var sha256 = SHA256.Create();
                    var hashBytes = sha256.ComputeHash(fileStream);
                    fileStream.Position = 0; // Reset for subsequent use
                    return Convert.ToBase64String(hashBytes)[..16]; // Use first 16 chars for shorter keys
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error generating file hash, using length + timestamp");
                    return $"{fileStream.Length}_{DateTime.UtcNow.Ticks}";
                }
            });
        }

        /// <summary>
        /// Try to get a cached comparison result
        /// </summary>
        public bool TryGetCachedComparison(string file1Hash, string file2Hash, string configFingerprint, out ComparisonResult result)
        {
            result = null;
            
            var cacheKey = $"{file1Hash}_{file2Hash}_{configFingerprint}";
            
            if (_comparisonCache.TryGetValue(cacheKey, out var cached))
            {
                // Check if cache entry is still valid
                if (DateTime.UtcNow - cached.CachedAt < _comparisonCacheExpiration)
                {
                    result = cached.Result;
                    Interlocked.Increment(ref _totalCacheHits);
                    _logger.LogDebug("Cache HIT for comparison: {CacheKey}", cacheKey);
                    return true;
                }
                else
                {
                    // Remove expired entry
                    _comparisonCache.TryRemove(cacheKey, out _);
                    Interlocked.Increment(ref _totalCacheEvictions);
                    _logger.LogDebug("Cache entry expired and removed: {CacheKey}", cacheKey);
                }
            }
            
            Interlocked.Increment(ref _totalCacheMisses);
            _logger.LogDebug("Cache MISS for comparison: {CacheKey}", cacheKey);
            return false;
        }

        /// <summary>
        /// Cache a comparison result
        /// </summary>
        public void CacheComparison(string file1Hash, string file2Hash, string configFingerprint, ComparisonResult result)
        {
            if (result == null) return;
            
            var cacheKey = $"{file1Hash}_{file2Hash}_{configFingerprint}";
            var cachedResult = new CachedComparisonResult(result, configFingerprint);
            
            // Check cache size limits before adding
            if (_comparisonCache.Count >= _maxComparisonCacheEntries)
            {
                CleanupComparisonCache();
            }
            
            _comparisonCache.TryAdd(cacheKey, cachedResult);
            _logger.LogDebug("Cached comparison result: {CacheKey}", cacheKey);
            
            // Periodic cleanup
            if (DateTime.UtcNow - _lastCleanup > TimeSpan.FromMinutes(15))
            {
                _ = Task.Run(CleanupCaches); // Run cleanup in background
            }
        }

        /// <summary>
        /// Try to get a cached deserialized object
        /// </summary>
        public bool TryGetCachedObject<T>(string filePath, DateTime fileLastModified, out T cachedObject) where T : class
        {
            cachedObject = null;
            
            var cacheKey = $"{filePath}_{fileLastModified.Ticks}";
            
            if (_objectCache.TryGetValue(cacheKey, out var cached))
            {
                // Check if cache entry is still valid and file hasn't changed
                if (DateTime.UtcNow - cached.CachedAt < _objectCacheExpiration && 
                    cached.FileLastModified == fileLastModified)
                {
                    cachedObject = cached.Object as T;
                    if (cachedObject != null)
                    {
                        Interlocked.Increment(ref _totalCacheHits);
                        _logger.LogDebug("Object cache HIT for: {FilePath}", filePath);
                        return true;
                    }
                }
                else
                {
                    // Remove expired or invalid entry
                    _objectCache.TryRemove(cacheKey, out _);
                    Interlocked.Increment(ref _totalCacheEvictions);
                }
            }
            
            Interlocked.Increment(ref _totalCacheMisses);
            _logger.LogDebug("Object cache MISS for: {FilePath}", filePath);
            return false;
        }

        /// <summary>
        /// Cache a deserialized object
        /// </summary>
        public void CacheObject(string filePath, DateTime fileLastModified, object obj, long estimatedSize = 50000)
        {
            if (obj == null) return;
            
            var cacheKey = $"{filePath}_{fileLastModified.Ticks}";
            var cachedObject = new CachedDeserializedObject(obj, fileLastModified, "", estimatedSize);
            
            // Check cache size limits before adding
            if (_objectCache.Count >= _maxObjectCacheEntries)
            {
                CleanupObjectCache();
            }
            
            _objectCache.TryAdd(cacheKey, cachedObject);
            _logger.LogDebug("Cached deserialized object: {FilePath}", filePath);
        }

        /// <summary>
        /// Clear all caches (useful when memory pressure is high)
        /// </summary>
        public void ClearAllCaches()
        {
            var comparisonCount = _comparisonCache.Count;
            var objectCount = _objectCache.Count;
            
            _comparisonCache.Clear();
            _objectCache.Clear();
            _configFingerprintCache.Clear();
            
            _logger.LogInformation("Cleared all caches: {ComparisonEntries} comparison entries, {ObjectEntries} object entries", 
                comparisonCount, objectCount);
        }

        /// <summary>
        /// Invalidate cached comparisons that used a different configuration
        /// </summary>
        public void InvalidateConfigurationChanges(string newConfigFingerprint)
        {
            var toRemove = _comparisonCache
                .Where(kvp => kvp.Value.ConfigurationFingerprint != newConfigFingerprint)
                .Select(kvp => kvp.Key)
                .ToList();
            
            foreach (var key in toRemove)
            {
                _comparisonCache.TryRemove(key, out _);
                Interlocked.Increment(ref _totalCacheEvictions);
            }
            
            if (toRemove.Count > 0)
            {
                _logger.LogInformation("Invalidated {Count} cached comparisons due to configuration changes", toRemove.Count);
            }
        }

        /// <summary>
        /// Clean up expired comparison cache entries
        /// </summary>
        private void CleanupComparisonCache()
        {
            var cutoff = DateTime.UtcNow - _comparisonCacheExpiration;
            var toRemove = _comparisonCache
                .Where(kvp => kvp.Value.CachedAt < cutoff)
                .Select(kvp => kvp.Key)
                .ToList();
            
            // If not enough expired entries, remove oldest ones
            if (toRemove.Count < _comparisonCache.Count / 4)
            {
                var oldestEntries = _comparisonCache
                    .OrderBy(kvp => kvp.Value.CachedAt)
                    .Take(_comparisonCache.Count / 4)
                    .Select(kvp => kvp.Key);
                
                toRemove.AddRange(oldestEntries);
            }
            
            foreach (var key in toRemove.Distinct())
            {
                _comparisonCache.TryRemove(key, out _);
                Interlocked.Increment(ref _totalCacheEvictions);
            }
            
            _logger.LogDebug("Cleaned up {Count} comparison cache entries", toRemove.Count);
        }

        /// <summary>
        /// Clean up expired object cache entries
        /// </summary>
        private void CleanupObjectCache()
        {
            var cutoff = DateTime.UtcNow - _objectCacheExpiration;
            var toRemove = _objectCache
                .Where(kvp => kvp.Value.CachedAt < cutoff)
                .Select(kvp => kvp.Key)
                .ToList();
            
            // If not enough expired entries, remove oldest ones
            if (toRemove.Count < _objectCache.Count / 4)
            {
                var oldestEntries = _objectCache
                    .OrderBy(kvp => kvp.Value.CachedAt)
                    .Take(_objectCache.Count / 4)
                    .Select(kvp => kvp.Key);
                
                toRemove.AddRange(oldestEntries);
            }
            
            foreach (var key in toRemove.Distinct())
            {
                _objectCache.TryRemove(key, out _);
                Interlocked.Increment(ref _totalCacheEvictions);
            }
            
            _logger.LogDebug("Cleaned up {Count} object cache entries", toRemove.Count);
        }

        /// <summary>
        /// Clean up all caches (removes expired entries and enforces memory limits)
        /// </summary>
        private void CleanupCaches()
        {
            try
            {
                _lastCleanup = DateTime.UtcNow;
                
                CleanupComparisonCache();
                CleanupObjectCache();
                
                // Check memory usage and clean more aggressively if needed
                var estimatedMemory = _comparisonCache.Values.Sum(c => c.ApproximateMemorySize) +
                                    _objectCache.Values.Sum(o => o.ApproximateMemorySize);
                
                if (estimatedMemory > _maxCacheMemoryBytes)
                {
                    _logger.LogWarning("Cache memory usage ({MemoryMB}MB) exceeds limit ({LimitMB}MB), performing aggressive cleanup",
                        estimatedMemory / (1024 * 1024), _maxCacheMemoryBytes / (1024 * 1024));
                    
                    // Remove half the entries, starting with largest and oldest
                    var comparisonToRemove = _comparisonCache
                        .OrderByDescending(kvp => kvp.Value.ApproximateMemorySize)
                        .ThenBy(kvp => kvp.Value.CachedAt)
                        .Take(_comparisonCache.Count / 2)
                        .Select(kvp => kvp.Key)
                        .ToList();
                    
                    foreach (var key in comparisonToRemove)
                    {
                        _comparisonCache.TryRemove(key, out _);
                    }
                    
                    var objectToRemove = _objectCache
                        .OrderByDescending(kvp => kvp.Value.ApproximateMemorySize)
                        .ThenBy(kvp => kvp.Value.CachedAt)
                        .Take(_objectCache.Count / 2)
                        .Select(kvp => kvp.Key)
                        .ToList();
                    
                    foreach (var key in objectToRemove)
                    {
                        _objectCache.TryRemove(key, out _);
                    }
                    
                    _logger.LogInformation("Aggressive cleanup removed {ComparisonCount} comparison and {ObjectCount} object cache entries",
                        comparisonToRemove.Count, objectToRemove.Count);
                }
                
                // Log cache statistics periodically
                var stats = GetCacheStatistics();
                _logger.LogDebug("Cache stats - Hits: {Hits}, Misses: {Misses}, Hit Ratio: {HitRatio:P2}, Memory: {MemoryMB}MB",
                    stats.Hits, stats.Misses, stats.HitRatio, stats.EstimatedMemory / (1024 * 1024));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during cache cleanup");
            }
        }
    }
} 