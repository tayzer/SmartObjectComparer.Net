namespace ParityBench.NET.UI.Workflow;

internal sealed class MaskTreeNode
{
    public MaskTreeNode(string name, string fullPath, PropertyPathEntry? entry)
    {
        Name = name;
        FullPath = fullPath;
        Entry = entry;
    }

    public string Name { get; }
    public string FullPath { get; }
    public PropertyPathEntry? Entry { get; set; }
    public List<MaskTreeNode> Children { get; } = new();
}
