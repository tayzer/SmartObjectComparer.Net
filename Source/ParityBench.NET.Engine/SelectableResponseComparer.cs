using ParityBench.NET.Application.Requests;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Engine;

public sealed class SelectableResponseComparer : IResponseComparer
{
    public const string AutoModelName = "Auto";
    private readonly IResponseComparer autoComparer;
    private readonly IResponseComparer modelComparer;
    private readonly IResponseModelRegistry modelRegistry;

    public SelectableResponseComparer(
        IResponseComparer autoComparer,
        IResponseComparer modelComparer,
        IResponseModelRegistry modelRegistry)
    {
        this.autoComparer = autoComparer ?? throw new ArgumentNullException(nameof(autoComparer));
        this.modelComparer = modelComparer ?? throw new ArgumentNullException(nameof(modelComparer));
        this.modelRegistry = modelRegistry ?? throw new ArgumentNullException(nameof(modelRegistry));
    }

    public Task<RequestPairResult> CompareAsync(
        RequestItem request,
        RunOptions options,
        ResponseArtifactMetadata? responseA,
        ResponseArtifactMetadata? responseB,
        string? errorMessage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ModelName)
            || string.Equals(options.ModelName, AutoModelName, StringComparison.OrdinalIgnoreCase))
        {
            return autoComparer.CompareAsync(request, options, responseA, responseB, errorMessage, cancellationToken);
        }

        modelRegistry.Resolve(options.ModelName);
        return modelComparer.CompareAsync(request, options, responseA, responseB, errorMessage, cancellationToken);
    }
}
