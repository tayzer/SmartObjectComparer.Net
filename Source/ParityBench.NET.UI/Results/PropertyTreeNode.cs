using MudBlazor;

namespace ParityBench.NET.UI.Results;

internal sealed class PropertyTreeNode : TreeItemData<string>
{
    public PropertyTreeNode(string name, string fullPath) : base(fullPath)
    {
        Name = name;
        FullPath = fullPath;
        Text = name;
    }

    public string Name { get; }

    public string FullPath { get; set; }

    public bool IsLeaf { get; set; }

    public int DifferenceCount { get; set; }

    public int AffectedPairCount { get; set; }

    internal HashSet<string> AffectedPairKeys { get; } = new(StringComparer.OrdinalIgnoreCase);
}
