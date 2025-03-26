﻿namespace ComparisonTool.Core.Comparison.Analysis;

public class SimilarFileGroup
{
    public string GroupName { get; set; }
    public int FileCount { get; set; }
    public List<string> FilePairs { get; set; } = new();
    public string CommonPattern { get; set; }
}