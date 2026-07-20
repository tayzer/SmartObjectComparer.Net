namespace ParityBench.NET.UI.Workflow;

/// <summary>
/// Lets host applications supply request-file and request-folder selection without coupling shared UI to a platform.
/// </summary>
public interface IRequestSourcePicker
{
    bool IsAvailable { get; }

    Task<IReadOnlyList<string>> PickRequestFilesAsync(CancellationToken cancellationToken = default);

    Task<string?> PickRequestDirectoryAsync(CancellationToken cancellationToken = default);
}
