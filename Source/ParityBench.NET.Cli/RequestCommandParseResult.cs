namespace ParityBench.NET.Cli;

public sealed record RequestCommandParseResult(RequestCommandOptions? Options, IReadOnlyList<string> Errors)
{
    public bool IsSuccess => Options is not null && Errors.Count == 0;
}