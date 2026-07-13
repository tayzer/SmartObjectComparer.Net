using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ParityBench.NET.Application.AcceptedDifferences;
using ParityBench.NET.Application.Reports;
using ParityBench.NET.Application.Requests;
using ParityBench.NET.Application.Results;
using ParityBench.NET.Domain.AcceptedDifferences;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Reports;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Results;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Domain.Runs.Retention;

namespace ParityBench.NET.Infrastructure.Reports;

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
