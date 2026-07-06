namespace ParityBench.NET.Domain.Reports;

public sealed record StaticReportRawTextDifference
{
    public StaticReportRawTextDifference(
        StaticReportRawTextDifferenceType type,
        int? lineNumberA = null,
        int? lineNumberB = null,
        string? textA = null,
        string? textB = null,
        string? message = null)
    {
        if (lineNumberA < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(lineNumberA), "Line number A must be positive.");
        }

        if (lineNumberB < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(lineNumberB), "Line number B must be positive.");
        }

        Type = type;
        LineNumberA = lineNumberA;
        LineNumberB = lineNumberB;
        TextA = textA;
        TextB = textB;
        Message = string.IsNullOrWhiteSpace(message) ? null : message;
    }

    public StaticReportRawTextDifferenceType Type { get; }

    public int? LineNumberA { get; }

    public int? LineNumberB { get; }

    public string? TextA { get; }

    public string? TextB { get; }

    public string? Message { get; }
}
