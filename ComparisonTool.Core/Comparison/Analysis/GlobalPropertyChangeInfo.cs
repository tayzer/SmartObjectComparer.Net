// <copyright file="GlobalPropertyChangeInfo.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace ComparisonTool.Core.Comparison.Analysis;

public class GlobalPropertyChangeInfo
{
    public string PropertyName { get; set; }

    // Changed to field for use with Interlocked.Increment
    public int occurrenceCount;

    // Property wrapper for the field
    public int OccurrenceCount
    {
        get => this.occurrenceCount;
        set => this.occurrenceCount = value;
    }

    public Dictionary<string, string> CommonChanges { get; set; } = new ();

    public List<string> AffectedFiles { get; set; } = new ();
}
