namespace ComparisonTool.Core;

public class SimilarFileGroup
{
    public string GroupName { get; set; }
    public int FileCount { get; set; }
    public List<string> FilePairs { get; set; } = new List<string>();
    public string CommonPattern { get; set; }
}