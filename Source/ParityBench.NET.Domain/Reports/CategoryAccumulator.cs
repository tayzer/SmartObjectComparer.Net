using ParityBench.NET.Domain.Requests;

namespace ParityBench.NET.Domain.Reports;

internal sealed class CategoryAccumulator
{
    public CategoryAccumulator(string category)
    {
        Category = category;
    }

    public string Category { get; }

    public int OccurrenceCount { get; set; }

    public int AffectedPairCount { get; set; }
}
