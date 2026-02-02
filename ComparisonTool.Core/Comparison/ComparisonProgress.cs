// <copyright file="ComparisonProgress.cs" company="PlaceholderCompany">
namespace ComparisonTool.Core.Comparison;

/// <summary>
/// Progress information for comparison operations.
/// </summary>
public class ComparisonProgress
{
    public ComparisonProgress(int completed, int total, string status)
    {
        Completed = completed;
        Total = total;
        Status = status;
    }

    public int Completed
    {
        get;
    }

    public int Total
    {
        get;
    }

    public string Status
    {
        get;
    }

    public double PercentComplete => Total == 0 ? 0 : (double)Completed / Total * 100;
}
