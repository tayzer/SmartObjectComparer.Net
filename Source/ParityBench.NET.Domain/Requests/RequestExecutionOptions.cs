namespace ParityBench.NET.Domain.Requests;

public sealed record RequestExecutionOptions
{
    public RequestExecutionOptions(string? contentTypeOverride = null)
    {
        ContentTypeOverride = string.IsNullOrWhiteSpace(contentTypeOverride) ? null : contentTypeOverride.Trim();
    }

    public string? ContentTypeOverride { get; }
}
