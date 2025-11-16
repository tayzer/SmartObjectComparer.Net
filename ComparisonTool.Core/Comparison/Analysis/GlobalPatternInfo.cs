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
    public string PatternPath { get; set; }

    public int occurrenceCount;

    // Property wrapper for the field
    public int OccurrenceCount
    {
        get => this.occurrenceCount;
        set => this.occurrenceCount = value;
    }

    public int fileCount;

    // Property wrapper for the field
    public int FileCount
    {
        get => this.fileCount;
        set => this.fileCount = value;
    }

    public List<string> AffectedFiles { get; set; } = new ();

    public List<Difference> Examples { get; set; } = new ();
}
