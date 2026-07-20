using ComparisonTool.Core.Comparison.Analysis;
using ComparisonTool.Core.Comparison.Results;

namespace ComparisonTool.Core.Serialization.BlazorReport;

/// <summary>
/// The top-level data structure embedded in the Blazor WASM report HTML.
/// Serialized by the CLI's BlazorReportBundleBuilder and deserialized by the report's ReportDataService.
/// </summary>
public sealed class ReportBootstrapData
{
    /// <summary>
    /// The full comparison result — the same type the Blazor UI components consume.
    /// </summary>
    public MultiFolderComparisonResult? Result { get; set; }

    /// <summary>
    /// Pre-computed enhanced structural analysis result (optional).
    /// </summary>
    public EnhancedStructuralDifferenceAnalyzer.EnhancedStructuralAnalysisResult? EnhancedAnalysis { get; set; }

    /// <summary>
    /// Pre-computed semantic difference analysis (optional).
    /// </summary>
    public SemanticDifferenceAnalysis? SemanticAnalysis { get; set; }

    /// <summary>
    /// Report metadata: model name, directories/endpoints, elapsed time, etc.
    /// </summary>
    public ReportMetadata? Metadata { get; set; }
}

/// <summary>
/// Report-level metadata for display in the report header.
/// </summary>
public sealed class ReportMetadata
{
    public string? ReportId { get; set; }
    public string? GeneratedAt { get; set; }
    public string? Command { get; set; }
    public string? ModelName { get; set; }
    public string? Directory1 { get; set; }
    public string? Directory2 { get; set; }
    public string? EndpointA { get; set; }
    public string? EndpointALabel { get; set; }
    public string? EndpointB { get; set; }
    public string? EndpointBLabel { get; set; }
    public string? JobId { get; set; }
    public double ElapsedSeconds { get; set; }
}
