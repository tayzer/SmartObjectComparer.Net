// <copyright file="GlobalPropertyChangeInfo.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace ComparisonTool.Core.Comparison.Analysis;

public class GlobalPropertyChangeInfo
{
    public string PropertyName { get; set; } = string.Empty;

    // Public field retained for use with Interlocked operations
    public int occurrenceCount;

    // Property wrapper for the field (keeps API compatibility)
    public int OccurrenceCount
    {
        get => this.occurrenceCount;
        set => this.occurrenceCount = value;
    }

    public Dictionary<string, string> CommonChanges { get; set; } = new ();

    public List<string> AffectedFiles { get; set; } = new ();
}
