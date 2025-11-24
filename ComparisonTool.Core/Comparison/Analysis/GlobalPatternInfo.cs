// <copyright file="GlobalPatternInfo.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using KellermanSoftware.CompareNetObjects;

namespace ComparisonTool.Core.Comparison.Analysis;

/// <summary>
/// Information about a pattern of differences that appears across multiple files
/// Modified for thread safety with a field instead of property for OccurrenceCount.
/// </summary>
public class GlobalPatternInfo
{
    public string PatternPath { get; set; } = string.Empty;

    // Public fields retained for use with Interlocked operations elsewhere in the codebase
    public int OccurrenceCountValue;

    // Property wrapper for the field (keeps API compatibility)
    public int OccurrenceCount
    {
        get => this.OccurrenceCountValue;
        set => this.OccurrenceCountValue = value;
    }

    public int FileCountValue;

    // Property wrapper for the field (keeps API compatibility)
    public int FileCount
    {
        get => this.FileCountValue;
        set => this.FileCountValue = value;
    }

    public List<string> AffectedFiles { get; set; } = new ();

    public List<Difference> Examples { get; set; } = new ();
}
