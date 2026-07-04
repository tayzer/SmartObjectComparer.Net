namespace ParityBench.NET.Domain.Runs;

public sealed record RunProgress
{
    public RunProgress(
        int percentComplete,
        string message,
        int? completedItems = null,
        int? totalItems = null)
    {
        if (percentComplete is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(percentComplete), "Percent complete must be between 0 and 100.");
        }

        if (completedItems < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(completedItems), "Completed item count must not be negative.");
        }

        if (totalItems < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalItems), "Total item count must not be negative.");
        }

        if (completedItems > totalItems)
        {
            throw new ArgumentOutOfRangeException(nameof(completedItems), "Completed item count must not exceed total item count.");
        }

        PercentComplete = percentComplete;
        Message = string.IsNullOrWhiteSpace(message) ? string.Empty : message;
        CompletedItems = completedItems;
        TotalItems = totalItems;
    }

    public int PercentComplete { get; }

    public string Message { get; }

    public int? CompletedItems { get; }

    public int? TotalItems { get; }
}
