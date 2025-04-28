using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Core.Utilities
{
    /// <summary>
    /// Monitors system resources to provide guidance for optimal resource utilization
    /// </summary>
    public class SystemResourceMonitor
    {
        private readonly ILogger<SystemResourceMonitor> _logger;
        private readonly Process _currentProcess;
        
        // Cache values to avoid frequent checks
        private DateTime _lastCpuCheck = DateTime.MinValue;
        private double _cachedCpuUsage = 0;
        private DateTime _lastMemoryCheck = DateTime.MinValue;
        private double _cachedMemoryUsage = 0;
        
        // Constants
        private const int ResourceCheckCacheTimeMs = 1000; // Only check resources every second
        
        public SystemResourceMonitor(ILogger<SystemResourceMonitor> logger)
        {
            _logger = logger;
            _currentProcess = Process.GetCurrentProcess();
        }
        
        /// <summary>
        /// Gets the current CPU usage percentage (0-100)
        /// </summary>
        public double GetCpuUsage()
        {
            if ((DateTime.Now - _lastCpuCheck).TotalMilliseconds < ResourceCheckCacheTimeMs)
            {
                return _cachedCpuUsage;
            }
            
            try
            {
                var startTime = DateTime.UtcNow;
                var startCpuUsage = _currentProcess.TotalProcessorTime;
                
                // Wait a short time to get a meaningful measurement
                System.Threading.Thread.Sleep(100);
                
                var endTime = DateTime.UtcNow;
                var endCpuUsage = _currentProcess.TotalProcessorTime;
                
                var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
                var totalElapsedMs = (endTime - startTime).TotalMilliseconds;
                
                // Calculate CPU usage for the process relative to all cores
                var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalElapsedMs) * 100;
                
                _cachedCpuUsage = Math.Min(100, Math.Max(0, cpuUsageTotal));
                _lastCpuCheck = DateTime.Now;
                
                return _cachedCpuUsage;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error getting CPU usage, returning default");
                return 50; // Default to 50% if we can't determine actual usage
            }
        }
        
        /// <summary>
        /// Gets the current memory usage percentage (0-100)
        /// </summary>
        public double GetMemoryUsage()
        {
            if ((DateTime.Now - _lastMemoryCheck).TotalMilliseconds < ResourceCheckCacheTimeMs)
            {
                return _cachedMemoryUsage;
            }
            
            try
            {
                _currentProcess.Refresh();
                var memoryUsageMb = _currentProcess.WorkingSet64 / (1024 * 1024);
                
                // Get available system memory (rough estimate)
                var availableMemoryMb = GetAvailableSystemMemoryMB();
                var totalMemoryMb = GetTotalSystemMemoryMB();
                
                // Calculate memory usage percentage
                double usagePercentage = 0;
                if (totalMemoryMb > 0)
                {
                    usagePercentage = 100 * (1 - (double)availableMemoryMb / totalMemoryMb);
                }
                
                _cachedMemoryUsage = Math.Min(100, Math.Max(0, usagePercentage));
                _lastMemoryCheck = DateTime.Now;
                
                _logger.LogDebug("Memory usage: {MemoryUsageMb}MB, Available: {AvailableMb}MB, Total: {TotalMb}MB, Percentage: {Percentage}%", 
                    memoryUsageMb, availableMemoryMb, totalMemoryMb, _cachedMemoryUsage);
                
                return _cachedMemoryUsage;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error getting memory usage, returning default");
                return 50; // Default to 50% if we can't determine actual usage
            }
        }
        
        /// <summary>
        /// Estimates optimal parallelism based on system resources and workload
        /// </summary>
        /// <param name="itemCount">Number of items to process</param>
        /// <param name="averageItemSizeKb">Average size of each item in KB (0 if unknown)</param>
        /// <returns>Optimal parallelism degree</returns>
        public int CalculateOptimalParallelism(int itemCount, long averageItemSizeKb = 0)
        {
            // Get resource usage
            var cpuUsage = GetCpuUsage();
            var memoryUsage = GetMemoryUsage();
            
            // Get processor count
            int processorCount = Environment.ProcessorCount;
            
            // Base parallelism on CPU count
            int baseParallelism = processorCount;
            
            // Apply scaling factors based on current resource utilization
            double cpuScaleFactor = 1.0;
            if (cpuUsage > 80) cpuScaleFactor = 0.5; // CPU is heavily loaded, reduce parallelism
            else if (cpuUsage < 30) cpuScaleFactor = 1.5; // CPU is underutilized, increase parallelism
            
            double memoryScaleFactor = 1.0;
            if (memoryUsage > 80) memoryScaleFactor = 0.5; // Memory is heavily used, reduce parallelism
            else if (memoryUsage < 30) memoryScaleFactor = 1.3; // Memory is underutilized, increase parallelism
            
            // Adjust for workload characteristics
            double workloadFactor = 1.0;
            if (itemCount > 1000) workloadFactor = 0.8; // Many small items benefit from more parallelism but with some limit
            
            // If item size is known, adjust for it
            if (averageItemSizeKb > 0)
            {
                if (averageItemSizeKb > 10000) workloadFactor *= 0.5; // Very large items
                else if (averageItemSizeKb > 1000) workloadFactor *= 0.7; // Large items
                else if (averageItemSizeKb < 10) workloadFactor *= 1.5; // Very small items
            }
            
            // Calculate final parallelism
            double adjustedParallelism = baseParallelism * cpuScaleFactor * memoryScaleFactor * workloadFactor;
            
            // Ensure result is at least 1 and at most 2 * processor count
            int result = (int)Math.Ceiling(adjustedParallelism);
            result = Math.Max(1, Math.Min(result, processorCount * 2));
            
            _logger.LogInformation(
                "Calculated optimal parallelism: {Result} (Base: {Base}, CPU: {CpuFactor:F2}, Memory: {MemoryFactor:F2}, Workload: {WorkloadFactor:F2})",
                result, baseParallelism, cpuScaleFactor, memoryScaleFactor, workloadFactor);
            
            return result;
        }
        
        /// <summary>
        /// Gets optimal batch size based on system resources and item count
        /// </summary>
        public int CalculateOptimalBatchSize(int totalItems, long averageItemSizeKb = 0)
        {
            // Default batch sizes based on item count
            int batchSize;
            
            if (totalItems > 10000) batchSize = 100;
            else if (totalItems > 1000) batchSize = 50;
            else if (totalItems > 100) batchSize = 25;
            else batchSize = 10;
            
            // Adjust for memory conditions
            double memoryUsage = GetMemoryUsage();
            
            if (memoryUsage > 80) batchSize = Math.Max(5, batchSize / 2);
            else if (memoryUsage < 30) batchSize = Math.Min(200, batchSize * 2);
            
            // Adjust for item size if known
            if (averageItemSizeKb > 0)
            {
                if (averageItemSizeKb > 10000) batchSize = Math.Max(2, batchSize / 4); // Very large items
                else if (averageItemSizeKb > 1000) batchSize = Math.Max(5, batchSize / 2); // Large items
            }
            
            _logger.LogInformation("Calculated optimal batch size: {BatchSize} for {TotalItems} items", 
                batchSize, totalItems);
                
            return batchSize;
        }
        
        /// <summary>
        /// Calculates average file size in KB for a list of files
        /// </summary>
        public long CalculateAverageFileSizeKb(IEnumerable<string> filePaths, int sampleSize = 20)
        {
            try
            {
                // If there are a lot of files, just take a sample
                var sampleFiles = filePaths.Count() <= sampleSize 
                    ? filePaths 
                    : filePaths.OrderBy(_ => Guid.NewGuid()).Take(sampleSize);
                
                long totalSizeBytes = 0;
                int fileCount = 0;
                
                foreach (var file in sampleFiles)
                {
                    if (File.Exists(file))
                    {
                        totalSizeBytes += new FileInfo(file).Length;
                        fileCount++;
                    }
                }
                
                if (fileCount == 0) return 0;
                
                return totalSizeBytes / fileCount / 1024; // Convert to KB
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error calculating average file size");
                return 0;
            }
        }
        
        // Helper methods to get system memory
        private long GetAvailableSystemMemoryMB()
        {
            try
            {
                // On Windows, use PerformanceCounter but handle errors gracefully
                return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024);
            }
            catch
            {
                // Fallback: rough estimate based on GC information
                var gcMemory = GC.GetGCMemoryInfo();
                return gcMemory.TotalAvailableMemoryBytes / (1024 * 1024);
            }
        }
        
        private long GetTotalSystemMemoryMB()
        {
            try
            {
                return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024);
            }
            catch
            {
                // Fallback to a reasonable default for modern systems
                return 8192; // Assume 8GB if we can't determine
            }
        }
    }
}
