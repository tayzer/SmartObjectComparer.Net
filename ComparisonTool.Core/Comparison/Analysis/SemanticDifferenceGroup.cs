// <copyright file="SemanticDifferenceGroup.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using KellermanSoftware.CompareNetObjects;

namespace ComparisonTool.Core.Comparison.Analysis;

/// <summary>
/// Represents a group of semantically related differences.
/// </summary>
public class SemanticDifferenceGroup
{
    /// <summary>
    /// Gets or sets a descriptive name for this group of differences.
    /// </summary>
    required public string GroupName
    {
        get; set;
    }

    /// <summary>
    /// Gets or sets a semantic description of what these differences represent.
    /// </summary>
    required public string SemanticDescription
    {
        get; set;
    }

    /// <summary>
    /// Gets or sets confidence level (0-100) that this grouping is accurate.
    /// </summary>
    public int ConfidenceLevel
    {
        get; set;
    }

    /// <summary>
    /// Gets count of differences in this group.
    /// </summary>
    public int DifferenceCount => Differences.Count;

    /// <summary>
    /// Gets count of affected files.
    /// </summary>
    public int FileCount => AffectedFiles.Count;

    /// <summary>
    /// Gets or sets the list of differences in this semantic group.
    /// </summary>
    public List<Difference> Differences { get; set; } = new List<Difference>();

    /// <summary>
    /// Gets or sets the files affected by this semantic group.
    /// </summary>
    public HashSet<string> AffectedFiles { get; set; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// Gets or sets common property paths that are part of this semantic group.
    /// </summary>
    public HashSet<string> RelatedProperties { get; set; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// Gets a representative change that exemplifies this group.
    /// </summary>
    public Difference RepresentativeDifference => Differences.FirstOrDefault();
}
