// <copyright file="HighPerformanceComparisonPipeline.cs" company="PlaceholderCompany">
using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Hashing;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using ComparisonTool.Core.Comparison.Analysis;
using ComparisonTool.Core.Comparison.Configuration;
using ComparisonTool.Core.Comparison.Results;
using ComparisonTool.Core.Serialization;
using ComparisonTool.Core.Utilities;
using KellermanSoftware.CompareNetObjects;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Core.Comparison;

/// <summary>
/// High-performance comparison pipeline using System.Threading.Channels
/// for producer-consumer pattern with separate I/O and CPU-bound stages.
/// </summary>
public sealed class HighPerformanceComparisonPipeline : IDisposable
{
    // OPTIMIZATION 4: Use XxHash64 instead of MD5 for faster hashing (non-cryptographic)
    private static readonly XxHash64 HashAlgorithm = new XxHash64();

    private readonly ILogger<HighPerformanceComparisonPipeline> logger;
    private readonly IComparisonConfigurationService configService;
    private readonly IXmlDeserializationService deserializationService;
    private readonly DeserializationServiceFactory? deserializationFactory;
    private readonly PerformanceTracker performanceTracker;

    // OPTIMIZATION 1: Reuse CompareLogic instances per thread to avoid allocation
    private readonly ThreadLocal<CompareLogic> threadLocalCompareLogic;

    // OPTIMIZATION 2: Pool DifferenceCategorizer instances
    private readonly ObjectPool<DifferenceCategorizer> categorizerPool;

    // OPTIMIZATION 3: Cache reflection method info
    private readonly ConcurrentDictionary<Type, Func<Stream, object>> cachedDeserializers = new ConcurrentDictionary<Type, Func<Stream, object>>();

    // Pipeline configuration
    private readonly int ioParallelism;
    private readonly int cpuParallelism;
    private readonly int channelCapacity;

    public HighPerformanceComparisonPipeline(
        ILogger<HighPerformanceComparisonPipeline> logger,
        IComparisonConfigurationService configService,
        IXmlDeserializationService deserializationService,
        PerformanceTracker performanceTracker,
        DeserializationServiceFactory? deserializationFactory = null)
    {
        this.logger = logger;
        this.configService = configService;
        this.deserializationService = deserializationService;
        this.performanceTracker = performanceTracker;
        this.deserializationFactory = deserializationFactory;

        // Calculate optimal parallelism based on system resources
        var cpuCores = Environment.ProcessorCount;
        ioParallelism = Math.Max(2, cpuCores / 2); // I/O can be more parallel
        cpuParallelism = Math.Max(1, cpuCores - 2); // Leave cores for I/O and UI
        channelCapacity = cpuCores * 4; // Buffer to decouple stages

        // OPTIMIZATION 1: Thread-local CompareLogic to avoid allocation per file
        threadLocalCompareLogic = new ThreadLocal<CompareLogic>(
            () => CreateOptimizedCompareLogic(),
            trackAllValues: false);

        // OPTIMIZATION 2: Object pool for categorizers
        categorizerPool = new ObjectPool<DifferenceCategorizer>(
            () => new DifferenceCategorizer(),
            maxPoolSize: cpuCores * 2);
    }

    /// <summary>
    /// Generate a fast hash for cache key using XxHash64.
    /// </summary>
    /// <param name="stream">The stream to hash.</param>
    /// <returns>Base64-encoded hash string.</returns>
    public static string GenerateFastHash(Stream stream)
    {
        const int bufferSize = 81920;
        var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);

        try
        {
            var hash = new XxHash64();
            int bytesRead;

            stream.Position = 0;
            while ((bytesRead = stream.Read(buffer, 0, bufferSize)) > 0)
            {
                hash.Append(buffer.AsSpan(0, bytesRead));
            }

            stream.Position = 0;
            return Convert.ToBase64String(hash.GetHashAndReset());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Compare files using a high-performance producer-consumer pipeline.
    /// </summary>
    /// <param name="filePairs">File pairs to compare.</param>
    /// <param name="modelName">Name of the registered model.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task<MultiFolderComparisonResult> CompareFilesAsync(
        IReadOnlyList<(string File1Path, string File2Path, string RelativePath)> filePairs,
        string modelName,
        IProgress<ComparisonProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var totalFiles = filePairs.Count;
        logger.LogInformation(
            "Starting high-performance comparison of {Count} file pairs with I/O parallelism {IoParallel} and CPU parallelism {CpuParallel}",
            totalFiles,
            ioParallelism,
            cpuParallelism);

        // Create bounded channels for back-pressure
        var deserializationChannel = Channel.CreateBounded<DeserializedFilePair>(
            new BoundedChannelOptions(channelCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleWriter = false,
                SingleReader = false,
            });

        var comparisonChannel = Channel.CreateBounded<FilePairComparisonResult>(
            new BoundedChannelOptions(channelCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleWriter = false,
                SingleReader = true,
            });

        // Get model type once
        var modelType = deserializationService.GetModelType(modelName);

        // Cache the deserializer for this model type
        var deserializer = GetOrCreateDeserializer(modelType);

        // Results collection
        var results = new ConcurrentBag<FilePairComparisonResult>();
        var completedCount = 0;
        var allEqual = true;

        // Stage 1: I/O + Deserialization (I/O-bound producer)
        var deserializationTask = RunDeserializationStageAsync(
            filePairs,
            deserializer,
            deserializationChannel.Writer,
            cancellationToken);

        // Stage 2: Comparison (CPU-bound consumers)
        var comparisonTasks = Enumerable.Range(0, cpuParallelism)
            .Select(_ => RunComparisonStageAsync(
                deserializationChannel.Reader,
                comparisonChannel.Writer,
                modelType,
                cancellationToken))
            .ToArray();

        // Stage 3: Result aggregation (single consumer)
        var aggregationTask = Task.Run(
            async () =>
        {
            await foreach (var result in comparisonChannel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                results.Add(result);

                if (!result.Summary?.AreEqual ?? false)
                {
                    allEqual = false;
                }

                var current = Interlocked.Increment(ref completedCount);

                // Throttle progress updates to reduce UI thread contention
                if (current % Math.Max(1, totalFiles / 100) == 0 || current == totalFiles)
                {
                    progress?.Report(new ComparisonProgress(
                        current,
                        totalFiles,
                        $"Compared {current} of {totalFiles} files"));
                }
            }
        }, cancellationToken);

        // Wait for deserialization to complete and close its channel
        await deserializationTask.ConfigureAwait(false);
        deserializationChannel.Writer.Complete();

        // Wait for all comparison tasks
        await Task.WhenAll(comparisonTasks).ConfigureAwait(false);
        comparisonChannel.Writer.Complete();

        // Wait for aggregation
        await aggregationTask.ConfigureAwait(false);

        return new MultiFolderComparisonResult
        {
            TotalPairsCompared = totalFiles,
            AllEqual = allEqual,
            FilePairResults = results.OrderBy(r => r.File1Name, StringComparer.Ordinal).ToList(),
            Metadata = new Dictionary<string, object>(StringComparer.Ordinal),
        };
    }

    public void Dispose() => threadLocalCompareLogic?.Dispose();

    /// <summary>
    /// Stage 1: Read files and deserialize (I/O-bound).
    /// </summary>
    private async Task RunDeserializationStageAsync(
        IReadOnlyList<(string File1Path, string File2Path, string RelativePath)> filePairs,
        Func<Stream, object> deserializer,
        ChannelWriter<DeserializedFilePair> writer,
        CancellationToken cancellationToken)
        => await Parallel.ForEachAsync(
            filePairs,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = ioParallelism,
                CancellationToken = cancellationToken,
            },
            async (filePair, ct) =>
            {
                var (file1Path, file2Path, relativePath) = filePair;

                try
                {
                    // OPTIMIZATION: Read both files in parallel
                    var (obj1, obj2) = await DeserializeBothFilesAsync(
                        file1Path, file2Path, deserializer, ct).ConfigureAwait(false);

                    var deserializedPair = new DeserializedFilePair
                    {
                        File1Path = file1Path,
                        File2Path = file2Path,
                        RelativePath = relativePath,
                        Object1 = obj1,
                        Object2 = obj2,
                    };

                    await writer.WriteAsync(deserializedPair, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error deserializing file pair: {Path}: {Message}", relativePath, ex.Message);

                    // CRITICAL FIX: Pass error information to the comparison stage
                    // so it can be properly reported instead of silently skipped
                    var errorPair = new DeserializedFilePair
                    {
                        File1Path = file1Path,
                        File2Path = file2Path,
                        RelativePath = relativePath,
                        Object1 = null,
                        Object2 = null,
                        ErrorMessage = ex.Message,
                        ErrorType = ex.GetType().Name,
                    };

                    await writer.WriteAsync(errorPair, ct).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);

    /// <summary>
    /// Deserialize both files concurrently for maximum I/O throughput.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async Task<(object, object)> DeserializeBothFilesAsync(
        string file1Path,
        string file2Path,
        Func<Stream, object> deserializer,
        CancellationToken ct)
    {
        // OPTIMIZATION: Read and deserialize both files in parallel
        var task1 = Task.Run(() => ReadAndDeserialize(file1Path, deserializer), ct);
        var task2 = Task.Run(() => ReadAndDeserialize(file2Path, deserializer), ct);

        await Task.WhenAll(task1, task2).ConfigureAwait(false);

        return (task1.Result, task2.Result);
    }

    /// <summary>
    /// Read file and deserialize with optimal buffer size.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private object ReadAndDeserialize(string filePath, Func<Stream, object> deserializer)
    {
        // OPTIMIZATION: Use FileOptions.SequentialScan for hint to OS
        using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024, // 64KB buffer for sequential read
            FileOptions.SequentialScan);

        return deserializer(stream);
    }

    /// <summary>
    /// Stage 2: Compare objects (CPU-bound).
    /// </summary>
    private async Task RunComparisonStageAsync(
        ChannelReader<DeserializedFilePair> reader,
        ChannelWriter<FilePairComparisonResult> writer,
        Type modelType,
        CancellationToken cancellationToken)
    {
        await foreach (var pair in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            // CRITICAL FIX: Check if the deserialization stage passed an error
            if (pair.HasError)
            {
                var errorResult = new FilePairComparisonResult
                {
                    File1Name = Path.GetFileName(pair.File1Path),
                    File2Name = Path.GetFileName(pair.File2Path),
                    ErrorMessage = pair.ErrorMessage,
                    ErrorType = pair.ErrorType,
                };

                await writer.WriteAsync(errorResult, cancellationToken).ConfigureAwait(false);
                continue;
            }

            try
            {
                // OPTIMIZATION: Reuse thread-local CompareLogic
                var compareLogic = threadLocalCompareLogic.Value!;

                // Perform comparison
                var result = compareLogic.Compare(pair.Object1!, pair.Object2!);

                // Filter ignored differences
                result = configService.FilterSmartIgnoredDifferences(result, modelType);
                result = configService.FilterIgnoredDifferences(result);

                // OPTIMIZATION: Pool categorizer instances
                var categorizer = categorizerPool.Get();
                try
                {
                    var summary = categorizer.CategorizeAndSummarize(result);

                    var pairResult = new FilePairComparisonResult
                    {
                        File1Name = Path.GetFileName(pair.File1Path),
                        File2Name = Path.GetFileName(pair.File2Path),
                        Result = result,
                        Summary = summary,
                    };

                    await writer.WriteAsync(pairResult, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    categorizerPool.Return(categorizer);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error comparing file pair: {Path}: {Message}", pair.RelativePath, ex.Message);

                // CRITICAL FIX: Create an error result instead of silently skipping
                var errorResult = new FilePairComparisonResult
                {
                    File1Name = Path.GetFileName(pair.File1Path),
                    File2Name = Path.GetFileName(pair.File2Path),
                    ErrorMessage = ex.Message,
                    ErrorType = ex.GetType().Name,
                };

                await writer.WriteAsync(errorResult, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Create an optimized CompareLogic instance with performance-tuned settings.
    /// </summary>
    private CompareLogic CreateOptimizedCompareLogic()
    {
        var currentConfig = configService.GetCurrentConfig();

        var optimizedLogic = new CompareLogic
        {
            Config = new ComparisonConfig
            {
                // OPTIMIZATION: Limit max differences for early termination
                MaxDifferences = Math.Min(currentConfig.MaxDifferences, 500),

                // Core settings
                IgnoreObjectTypes = currentConfig.IgnoreObjectTypes,
                ComparePrivateFields = false, // Skip private fields for performance
                ComparePrivateProperties = false, // Skip private props for performance
                CompareReadOnly = currentConfig.CompareReadOnly,
                IgnoreCollectionOrder = currentConfig.IgnoreCollectionOrder,
                CaseSensitive = currentConfig.CaseSensitive,

                // OPTIMIZATION: Enable caching for reflection
                Caching = true,

                // OPTIMIZATION: Skip null checking - assume valid data
                SkipInvalidIndexers = true,
            },
        };

        // Initialize collections
        optimizedLogic.Config.MembersToIgnore = new List<string>(currentConfig.MembersToIgnore ?? new List<string>());
        optimizedLogic.Config.AttributesToIgnore = new List<Type>(currentConfig.AttributesToIgnore ?? new List<Type>());
        optimizedLogic.Config.MembersToInclude = new List<string>();
        optimizedLogic.Config.CustomComparers = new List<KellermanSoftware.CompareNetObjects.TypeComparers.BaseTypeComparer>();

        // Always ignore array length properties
        if (!optimizedLogic.Config.MembersToIgnore.Contains("Length", StringComparer.Ordinal))
        {
            optimizedLogic.Config.MembersToIgnore.Add("Length");
        }

        if (!optimizedLogic.Config.MembersToIgnore.Contains("LongLength", StringComparer.Ordinal))
        {
            optimizedLogic.Config.MembersToIgnore.Add("LongLength");
        }

        return optimizedLogic;
    }

    /// <summary>
    /// Get or create a cached deserializer delegate for the model type.
    /// </summary>
    private Func<Stream, object> GetOrCreateDeserializer(Type modelType) =>
        cachedDeserializers.GetOrAdd(modelType, type =>
        {
            var methodInfo = typeof(IXmlDeserializationService)
                .GetMethod(nameof(IXmlDeserializationService.DeserializeXml))
                ?? throw new InvalidOperationException("DeserializeXml method was not found.");
            var method = methodInfo.MakeGenericMethod(type);

            return stream => method.Invoke(deserializationService, new object[] { stream })
                ?? throw new InvalidOperationException("Deserialization returned null.");
        });

    /// <summary>
    /// Internal class for passing deserialized objects between pipeline stages.
    /// </summary>
    private sealed class DeserializedFilePair
    {
        required public string File1Path
        {
            get; init;
        }

        required public string File2Path
        {
            get; init;
        }

        required public string RelativePath
        {
            get; init;
        }

        public object? Object1
        {
            get; init;
        }

        public object? Object2
        {
            get; init;
        }

        /// <summary>
        /// Gets the error message if deserialization failed.
        /// </summary>
        public string? ErrorMessage
        {
            get; init;
        }

        /// <summary>
        /// Gets the error type if deserialization failed.
        /// </summary>
        public string? ErrorType
        {
            get; init;
        }

        /// <summary>
        /// Gets a value indicating whether this pair had an error during deserialization.
        /// </summary>
        public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    }
}

/// <summary>
/// Simple object pool for reusing instances.
/// </summary>
/// <typeparam name="T">Type of objects to pool.</typeparam>
public sealed class ObjectPool<T>
    where T : class
{
    private readonly ConcurrentBag<T> pool = new ConcurrentBag<T>();
    private readonly Func<T> factory;
    private readonly int maxPoolSize;
    private int currentSize;

    public ObjectPool(Func<T> factory, int maxPoolSize = 32)
    {
        this.factory = factory;
        this.maxPoolSize = maxPoolSize;
    }

    public T Get()
    {
        if (pool.TryTake(out var item))
        {
            Interlocked.Decrement(ref currentSize);
            return item;
        }

        return factory();
    }

    public void Return(T item)
    {
        if (Interlocked.Increment(ref currentSize) <= maxPoolSize)
        {
            pool.Add(item);
        }
        else
        {
            Interlocked.Decrement(ref currentSize);
        }
    }
}
