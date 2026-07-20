namespace ParityBench.NET.Application.Reports;

public sealed record StaticReportBundleWriteResult(
    string OutputDirectory,
    string ManifestPath,
    string RedirectorPath,
    int DetailPageCount,
    int RawArtifactCount);
