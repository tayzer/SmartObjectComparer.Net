namespace ParityBench.NET.Worker;

/// <summary>
/// The command-line arguments the host passes to the worker.
/// </summary>
internal sealed record WorkerArguments(
    string WorkspaceRoot,
    string RunId,
    string PipeName,
    string FixtureBaseUrl)
{
    public static WorkerArguments Parse(string[] args)
    {
        Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index + 1 < args.Length; index += 2)
        {
            values[args[index].TrimStart('-')] = args[index + 1];
        }

        return new WorkerArguments(
            Require(values, "workspace"),
            Require(values, "run"),
            Require(values, "pipe"),
            values.TryGetValue("fixture-base-url", out string? fixtureBaseUrl) ? fixtureBaseUrl : "http://localhost");
    }

    private static string Require(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"Missing required argument '--{key}'.");
}
