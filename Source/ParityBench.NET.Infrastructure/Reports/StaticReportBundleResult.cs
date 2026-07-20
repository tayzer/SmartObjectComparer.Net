namespace ParityBench.NET.Infrastructure.Reports;

public sealed record StaticReportBundleResult(
    string OutputDirectory,
    string ManifestPath,
    string RedirectorPath,
    int DetailPageCount,
    int RawArtifactCount);
