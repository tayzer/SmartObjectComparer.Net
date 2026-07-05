namespace ParityBench.NET.Cli;

public sealed record RequestCommandOptions(
    string RequestDirectory,
    Uri EndpointA,
    Uri EndpointB,
    string ModelName,
    string? ContractProfileId,
    int MaxConcurrency,
    TimeSpan Timeout,
    string? ContentTypeOverride,
    IReadOnlyList<string> CommonHeaders,
    IReadOnlyList<string> EndpointAHeaders,
    IReadOnlyList<string> EndpointBHeaders,
    string? ReportOutputDirectory,
    string? ReportAssetsDirectory);