// <copyright file="DifferencePattern.cs" company="PlaceholderCompany">



using KellermanSoftware.CompareNetObjects;

namespace ComparisonTool.Core.Comparison.Analysis;

/// <summary>
/// Represents a pattern of differences.
/// </summary>
public class DifferencePattern {
    public string Pattern { get; set; } = string.Empty;

    public string PropertyPath { get; set; } = string.Empty;

    public int OccurrenceCount {
        get; set;
    }

    public List<Difference> Examples { get; set; } = new();
}
