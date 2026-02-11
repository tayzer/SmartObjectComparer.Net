namespace ComparisonTool.Cli.Reporting;

/// <summary>
/// Writes a human-readable comparison summary to the console.
/// </summary>
public static class ConsoleReportWriter
{
    /// <summary>
    /// Writes the report to stdout.
    /// </summary>
    public static void Write(ReportContext context)
    {
        var result = context.Result;
        var pairs = result.FilePairResults;

        var equalCount = pairs.Count(p => p.AreEqual);
        var diffCount = pairs.Count(p => !p.AreEqual && !p.HasError);
        var errorCount = pairs.Count(p => p.HasError);

        Console.WriteLine();
        WriteSeparator();
        Console.WriteLine("  COMPARISON SUMMARY");
        WriteSeparator();

        if (!string.IsNullOrEmpty(context.ModelName))
        {
            Console.WriteLine($"  Model:           {context.ModelName}");
        }

        if (context.CommandName == "folder")
        {
            Console.WriteLine($"  Directory 1:     {context.Directory1}");
            Console.WriteLine($"  Directory 2:     {context.Directory2}");
        }
        else if (context.CommandName == "request")
        {
            Console.WriteLine($"  Endpoint A:      {context.EndpointA}");
            Console.WriteLine($"  Endpoint B:      {context.EndpointB}");
            if (!string.IsNullOrEmpty(context.JobId))
            {
                Console.WriteLine($"  Job ID:          {context.JobId}");
            }
        }

        Console.WriteLine($"  Total pairs:     {result.TotalPairsCompared}");
        Console.WriteLine($"  Equal:           {equalCount}");
        Console.WriteLine($"  Different:       {diffCount}");
        Console.WriteLine($"  Errors:          {errorCount}");
        Console.WriteLine($"  All equal:       {(result.AllEqual ? "YES" : "NO")}");
        Console.WriteLine($"  Elapsed:         {context.Elapsed.TotalSeconds:F2}s");
        WriteSeparator();

        // Show first N differences for quick triage
        var differencePairs = pairs
            .Where(p => !p.AreEqual && !p.HasError)
            .Take(20)
            .ToList();

        if (differencePairs.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"  DIFFERENCES (showing up to 20 of {diffCount}):");
            Console.WriteLine();

            foreach (var pair in differencePairs)
            {
                var diffCountForPair = pair.Result?.Differences?.Count ?? pair.RawTextDifferences?.Count ?? 0;
                var outcomeTag = pair.PairOutcome != null ? $" [{pair.PairOutcome}]" : string.Empty;
                Console.WriteLine($"    {pair.File1Name}{outcomeTag} — {diffCountForPair} difference(s)");

                // Show up to 3 property paths per pair
                if (pair.Result?.Differences != null)
                {
                    foreach (var diff in pair.Result.Differences.Take(3))
                    {
                        Console.WriteLine($"      • {diff.PropertyName}");
                        Console.WriteLine($"        Expected: {Truncate(diff.Object1Value, 80)}");
                        Console.WriteLine($"        Actual:   {Truncate(diff.Object2Value, 80)}");
                    }

                    if (pair.Result.Differences.Count > 3)
                    {
                        Console.WriteLine($"      ... and {pair.Result.Differences.Count - 3} more");
                    }
                }
            }
        }

        // Show errors
        var errorPairs = pairs.Where(p => p.HasError).Take(10).ToList();
        if (errorPairs.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"  ERRORS (showing up to 10 of {errorCount}):");
            Console.WriteLine();

            foreach (var pair in errorPairs)
            {
                Console.WriteLine($"    {pair.File1Name}: {pair.ErrorMessage}");
            }
        }

        Console.WriteLine();
    }

    private static void WriteSeparator()
    {
        Console.WriteLine($"  {string.Empty.PadRight(60, '─')}");
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "(null)";
        }

        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }
}
