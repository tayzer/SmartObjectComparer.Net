namespace ParityBench.NET.UI.Workflow;

public sealed class NoOpRequestSourcePicker : IRequestSourcePicker
{
    public bool IsAvailable => false;

    public Task<IReadOnlyList<string>> PickRequestFilesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

    public Task<string?> PickRequestDirectoryAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);
}
