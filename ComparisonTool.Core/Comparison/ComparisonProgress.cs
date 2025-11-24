// <copyright file="ComparisonProgress.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace ComparisonTool.Core.Comparison;

/// <summary>
/// Progress information for comparison operations.
/// </summary>
public class ComparisonProgress
{
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

    public double PercentComplete => this.Total == 0 ? 0 : (double)this.Completed / this.Total * 100;

    public ComparisonProgress(int completed, int total, string status)
    {
        this.Completed = completed;
        this.Total = total;
        this.Status = status;
    }
}
