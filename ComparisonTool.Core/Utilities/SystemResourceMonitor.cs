// <copyright file="SystemResourceMonitor.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace ComparisonTool.Core.Utilities
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Monitors system resources to provide guidance for optimal resource utilization.
    /// </summary>
    public class SystemResourceMonitor
    {
        private readonly ILogger<SystemResourceMonitor> logger;
        private readonly Process currentProcess;

        // Cache values to avoid frequent checks
        private DateTime lastCpuCheck = DateTime.MinValue;
        private double cachedCpuUsage = 0;
        private DateTime lastMemoryCheck = DateTime.MinValue;
        private double cachedMemoryUsage = 0;

        // Constants
        private const int ResourceCheckCacheTimeMs = 1000; // Only check resources every second

        public SystemResourceMonitor(ILogger<SystemResourceMonitor> logger)
        {
            this.logger = logger;
            this.currentProcess = Process.GetCurrentProcess();
        }

        /// <summary>
        /// Gets the current CPU usage percentage (0-100).
        /// </summary>
        /// <returns></returns>
        public double GetCpuUsage()
        {
            if ((DateTime.Now - this.lastCpuCheck).TotalMilliseconds < ResourceCheckCacheTimeMs)
            {
                return this.cachedCpuUsage;
            }

            try
            {
                var startTime = DateTime.UtcNow;
                var startCpuUsage = this.currentProcess.TotalProcessorTime;

                // Wait a short time to get a meaningful measurement
                System.Threading.Thread.Sleep(100);

                var endTime = DateTime.UtcNow;
                var endCpuUsage = this.currentProcess.TotalProcessorTime;

                var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
                var totalElapsedMs = (endTime - startTime).TotalMilliseconds;

                // Calculate CPU usage for the process relative to all cores
                var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalElapsedMs) * 100;

                this.cachedCpuUsage = Math.Min(100, Math.Max(0, cpuUsageTotal));
                this.lastCpuCheck = DateTime.Now;

                return this.cachedCpuUsage;
            }
            catch (Exception ex)
            {
                this.logger.LogWarning(ex, "Error getting CPU usage, returning default");
                return 50; // Default to 50% if we can't determine actual usage
            }
        }

        /// <summary>
        /// Gets the current memory usage percentage (0-100).
        /// </summary>
        /// <returns></returns>
        public double GetMemoryUsage()
        {
            if ((DateTime.Now - this.lastMemoryCheck).TotalMilliseconds < ResourceCheckCacheTimeMs)
            {
                return this.cachedMemoryUsage;
            }

            try
            {
                this.currentProcess.Refresh();
                var memoryUsageMb = this.currentProcess.WorkingSet64 / (1024 * 1024);

                // Get available system memory (rough estimate)
                var availableMemoryMb = this.GetAvailableSystemMemoryMB();
                var totalMemoryMb = this.GetTotalSystemMemoryMB();

                // Calculate memory usage percentage
                double usagePercentage = 0;
                if (totalMemoryMb > 0)
                {
                    usagePercentage = 100 * (1 - ((double)availableMemoryMb / totalMemoryMb));
                }

                this.cachedMemoryUsage = Math.Min(100, Math.Max(0, usagePercentage));
                this.lastMemoryCheck = DateTime.Now;

                this.logger.LogDebug(
                    "Memory usage: {MemoryUsageMb}MB, Available: {AvailableMb}MB, Total: {TotalMb}MB, Percentage: {Percentage}%",
                    memoryUsageMb, availableMemoryMb, totalMemoryMb, this.cachedMemoryUsage);

                return this.cachedMemoryUsage;
            }
            catch (Exception ex)
            {
                this.logger.LogWarning(ex, "Error getting memory usage, returning default");
                return 50; // Default to 50% if we can't determine actual usage
            }
        }

        /// <summary>
        /// Estimates optimal parallelism based on system resources and workload.
        /// </summary>
        /// <param name="itemCount">Number of items to process.</param>
        /// <param name="averageItemSizeKb">Average size of each item in KB (0 if unknown).</param>
        /// <returns>Optimal parallelism degree.</returns>
        public int CalculateOptimalParallelism(int itemCount, long averageItemSizeKb = 0)
        {
            // Get resource usage
            var cpuUsage = this.GetCpuUsage();
            var memoryUsage = this.GetMemoryUsage();

            // Get processor count
            var processorCount = Environment.ProcessorCount;

            // Base parallelism on CPU count
            var baseParallelism = processorCount;

            // Apply scaling factors based on current resource utilization
            var cpuScaleFactor = 1.0;
            if (cpuUsage > 80)
            {
                cpuScaleFactor = 0.5; // CPU is heavily loaded, reduce parallelism
            }
            else if (cpuUsage < 30)
            {
                cpuScaleFactor = 1.5; // CPU is underutilized, increase parallelism
            }

            var memoryScaleFactor = 1.0;
            if (memoryUsage > 80)
            {
                memoryScaleFactor = 0.5; // Memory is heavily used, reduce parallelism
            }
            else if (memoryUsage < 30)
            {
                memoryScaleFactor = 1.3; // Memory is underutilized, increase parallelism
            }

            // Adjust for workload characteristics
            var workloadFactor = 1.0;
            if (itemCount > 1000)
            {
                workloadFactor = 0.8; // Many small items benefit from more parallelism but with some limit
            }

            // If item size is known, adjust for it
            if (averageItemSizeKb > 0)
            {
                if (averageItemSizeKb > 10000)
                {
                    workloadFactor *= 0.5; // Very large items
                }
                else if (averageItemSizeKb > 1000)
                {
                    workloadFactor *= 0.7; // Large items
                }
                else if (averageItemSizeKb < 10)
                {
                    workloadFactor *= 1.5; // Very small items
                }
            }

            // Calculate final parallelism
            var adjustedParallelism = baseParallelism * cpuScaleFactor * memoryScaleFactor * workloadFactor;

            // Ensure result is at least 1 and at most 2 * processor count
            var result = (int)Math.Ceiling(adjustedParallelism);
            result = Math.Max(1, Math.Min(result, processorCount * 2));

            this.logger.LogInformation(
                "Calculated optimal parallelism: {Result} (Base: {Base}, CPU: {CpuFactor:F2}, Memory: {MemoryFactor:F2}, Workload: {WorkloadFactor:F2})",
                result, baseParallelism, cpuScaleFactor, memoryScaleFactor, workloadFactor);

            return result;
        }

        /// <summary>
        /// Gets optimal batch size based on system resources and item count.
        /// </summary>
        /// <returns></returns>
        public int CalculateOptimalBatchSize(int totalItems, long averageItemSizeKb = 0)
        {
            // Default batch sizes based on item count
            int batchSize;

            if (totalItems > 10000)
            {
                batchSize = 100;
            }
            else if (totalItems > 1000)
            {
                batchSize = 50;
            }
            else if (totalItems > 100)
            {
                batchSize = 25;
            }
            else
            {
                batchSize = 10;
            }

            // Adjust for memory conditions
            var memoryUsage = this.GetMemoryUsage();

            if (memoryUsage > 80)
            {
                batchSize = Math.Max(5, batchSize / 2);
            }
            else if (memoryUsage < 30)
            {
                batchSize = Math.Min(200, batchSize * 2);
            }

            // Adjust for item size if known
            if (averageItemSizeKb > 0)
            {
                if (averageItemSizeKb > 10000)
                {
                    batchSize = Math.Max(2, batchSize / 4); // Very large items
                }
                else if (averageItemSizeKb > 1000)
                {
                    batchSize = Math.Max(5, batchSize / 2); // Large items
                }
            }

            this.logger.LogInformation(
                "Calculated optimal batch size: {BatchSize} for {TotalItems} items",
                batchSize, totalItems);

            return batchSize;
        }

        /// <summary>
        /// Calculates average file size in KB for a list of files.
        /// </summary>
        /// <returns></returns>
        public long CalculateAverageFileSizeKb(IEnumerable<string> filePaths, int sampleSize = 20)
        {
            try
            {
                // If there are a lot of files, just take a sample
                var sampleFiles = filePaths.Count() <= sampleSize
                    ? filePaths
                    : filePaths.OrderBy(_ => Guid.NewGuid()).Take(sampleSize);

                long totalSizeBytes = 0;
                var fileCount = 0;

                foreach (var file in sampleFiles)
                {
                    if (File.Exists(file))
                    {
                        totalSizeBytes += new FileInfo(file).Length;
                        fileCount++;
                    }
                }

                if (fileCount == 0)
                {
                    return 0;
                }

                return totalSizeBytes / fileCount / 1024; // Convert to KB
            }
            catch (Exception ex)
            {
                this.logger.LogWarning(ex, "Error calculating average file size");
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
