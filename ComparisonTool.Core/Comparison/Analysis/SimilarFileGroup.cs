// <copyright file="SimilarFileGroup.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace ComparisonTool.Core.Comparison.Analysis;

public class SimilarFileGroup
{
    public string GroupName { get; set; } = string.Empty;

    public int FileCount
    {
        get; set;
    }

    public IList<string> FilePairs { get; set; } = new List<string>();

    public string CommonPattern { get; set; } = string.Empty;
}
