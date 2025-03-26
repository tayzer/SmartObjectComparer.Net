namespace ComparisonTool.Core.Comparison.Results;

public class MultiFolderComparisonResult
{
    public bool AllEqual { get; set; } = true;
    public int TotalPairsCompared { get; set; }
    public List<FilePairComparisonResult> FilePairResults { get; set; } = new();
}