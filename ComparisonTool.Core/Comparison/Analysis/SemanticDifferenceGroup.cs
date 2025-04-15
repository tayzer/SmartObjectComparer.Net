using KellermanSoftware.CompareNetObjects;

namespace ComparisonTool.Core.Comparison.Analysis;

/// <summary>
/// Represents a group of semantically related differences
/// </summary>
public class SemanticDifferenceGroup
{
    /// <summary>
    /// A descriptive name for this group of differences
    /// </summary>
    public string GroupName { get; set; }

    /// <summary>
    /// A semantic description of what these differences represent
    /// </summary>
    public string SemanticDescription { get; set; }

    /// <summary>
    /// Confidence level (0-100) that this grouping is accurate
    /// </summary>
    public int ConfidenceLevel { get; set; }

    /// <summary>
    /// Count of differences in this group
    /// </summary>
    public int DifferenceCount => Differences.Count;

    /// <summary>
    /// Count of affected files
    /// </summary>
    public int FileCount => AffectedFiles.Count;

    /// <summary>
    /// The list of differences in this semantic group
    /// </summary>
    public List<Difference> Differences { get; set; } = new List<Difference>();

    /// <summary>
    /// The files affected by this semantic group
    /// </summary>
    public HashSet<string> AffectedFiles { get; set; } = new HashSet<string>();

    /// <summary>
    /// Common property paths that are part of this semantic group
    /// </summary>
    public HashSet<string> RelatedProperties { get; set; } = new HashSet<string>();

    /// <summary>
    /// A representative change that exemplifies this group
    /// </summary>
    public Difference RepresentativeDifference => Differences.FirstOrDefault();
}