// <copyright file="SimilarFileGroup.cs" company="PlaceholderCompany">
namespace ComparisonTool.Core.Comparison.Analysis;

public class SimilarFileGroup {
    public string GroupName { get; set; } = string.Empty;

    public int FileCount {
        get; set;
    }

    public List<string> FilePairs { get; set; } = new ();

    public string CommonPattern { get; set; } = string.Empty;
}
