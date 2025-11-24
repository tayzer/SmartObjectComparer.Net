// <copyright file="MultiFolderComparisonResult.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace ComparisonTool.Core.Comparison.Results;

public class MultiFolderComparisonResult {
    public bool AllEqual { get; set; } = true;

    public int TotalPairsCompared { get; set; }

    public List<FilePairComparisonResult> FilePairResults { get; set; } = new();

    public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
}
