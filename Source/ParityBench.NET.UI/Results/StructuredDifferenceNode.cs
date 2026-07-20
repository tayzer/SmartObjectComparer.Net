using ParityBench.NET.Domain.Comparison;

namespace ParityBench.NET.UI.Results;

internal sealed class StructuredDifferenceNode
{
    public StructuredDifferenceNode(string name, string fullPath)
    {
        Name = name;
        FullPath = fullPath;
    }

    public string Name { get; }

    public string FullPath { get; }

    public List<StructuredDifferenceNode> Children { get; } = new List<StructuredDifferenceNode>();

    public List<ComparisonDifference> Differences { get; } = new List<ComparisonDifference>();

    public int DifferenceCount => Differences.Count + Children.Sum(child => child.DifferenceCount);
}
