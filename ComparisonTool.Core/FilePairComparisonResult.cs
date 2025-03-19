using KellermanSoftware.CompareNetObjects;

namespace ComparisonTool.Core;

public class FilePairComparisonResult
{
    public string File1Name { get; set; }
    public string File2Name { get; set; }
    public ComparisonResult Result { get; set; }
    public DifferenceSummary Summary { get; set; }
    public bool AreEqual => Summary?.AreEqual ?? false;
}