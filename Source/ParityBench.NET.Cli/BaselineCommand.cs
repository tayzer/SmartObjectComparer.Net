using ParityBench.NET.Application.Baselines;
using ParityBench.NET.Domain.Baselines;

namespace ParityBench.NET.Cli;

public enum BaselineCommandAction
{
    List,
    Export,
    Import,
    Delete,
}

public sealed record BaselineCommandOptions(
    BaselineCommandAction Action,
    BaselineId? Id = null,
    int? Version = null,
    string? Path = null);

public sealed record BaselineCommandParseResult(
    BaselineCommandOptions? Options,
    IReadOnlyList<string> Errors)
{
    public bool IsSuccess => Options is not null && Errors.Count == 0;
}

/// <summary>
/// Parses <c>baseline list|export|import|delete</c>, the CLI's window onto the
/// captured-baseline library.
/// </summary>
public static class BaselineCommandParser
{
    public static bool IsBaselineCommand(IReadOnlyList<string> args) =>
        args.Count > 0 && string.Equals(args[0], "baseline", StringComparison.OrdinalIgnoreCase);

    public static BaselineCommandParseResult Parse(IReadOnlyList<string> args)
    {
        if (!IsBaselineCommand(args) || args.Count < 2)
        {
            return Failure("Expected command: " + Usage);
        }

        string action = args[1];
        try
        {
            switch (action.ToLowerInvariant())
            {
                case "list":
                    return Success(new BaselineCommandOptions(BaselineCommandAction.List));

                case "export":
                    if (args.Count < 4)
                    {
                        return Failure("baseline export <id>[@<version>] <archive-path>");
                    }

                    (BaselineId exportId, int? exportVersion) = ParseReference(args[2]);
                    return Success(new BaselineCommandOptions(BaselineCommandAction.Export, exportId, exportVersion, args[3]));

                case "import":
                    if (args.Count < 3)
                    {
                        return Failure("baseline import <archive-path>");
                    }

                    return Success(new BaselineCommandOptions(BaselineCommandAction.Import, Path: args[2]));

                case "delete":
                    if (args.Count < 3)
                    {
                        return Failure("baseline delete <id>[@<version>]");
                    }

                    (BaselineId deleteId, int? deleteVersion) = ParseReference(args[2]);
                    return Success(new BaselineCommandOptions(BaselineCommandAction.Delete, deleteId, deleteVersion));

                default:
                    return Failure($"Unknown baseline action '{action}'. {Usage}");
            }
        }
        catch (ArgumentException ex)
        {
            return Failure(ex.Message);
        }
    }

    public static string Usage =>
        "baseline list | baseline export <id>[@<version>] <archive-path> | baseline import <archive-path> | baseline delete <id>[@<version>]";

    private static (BaselineId Id, int? Version) ParseReference(string value)
    {
        BaselineRunSelection selection = BaselineRunSelection.ParseReplay(value);
        return (selection.BaselineId!.Value, selection.Version);
    }

    private static BaselineCommandParseResult Success(BaselineCommandOptions options) =>
        new BaselineCommandParseResult(options, Array.Empty<string>());

    private static BaselineCommandParseResult Failure(string error) =>
        new BaselineCommandParseResult(null, new[] { error });
}

public sealed class BaselineCommandRunner
{
    private readonly IBaselineLibraryUseCases library;

    public BaselineCommandRunner(IBaselineLibraryUseCases library)
    {
        this.library = library ?? throw new ArgumentNullException(nameof(library));
    }

    public async Task<int> RunAsync(
        BaselineCommandOptions options,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        try
        {
            switch (options.Action)
            {
                case BaselineCommandAction.List:
                    return await ListAsync(output, cancellationToken).ConfigureAwait(false);

                case BaselineCommandAction.Export:
                    return await ExportAsync(options, output, error, cancellationToken).ConfigureAwait(false);

                case BaselineCommandAction.Import:
                    BaselinePackageManifest imported = await library
                        .ImportAsync(options.Path!, cancellationToken)
                        .ConfigureAwait(false);
                    await output
                        .WriteLineAsync($"Imported '{imported.Name}' as {imported.Id.Value}@{imported.Version} ({imported.Scenarios.Count} scenarios).")
                        .ConfigureAwait(false);
                    return 0;

                case BaselineCommandAction.Delete:
                    await library.DeleteAsync(options.Id!.Value, options.Version, cancellationToken).ConfigureAwait(false);
                    await output
                        .WriteLineAsync(options.Version is null
                            ? $"Deleted all versions of '{options.Id.Value.Value}'."
                            : $"Deleted '{options.Id.Value.Value}' v{options.Version}.")
                        .ConfigureAwait(false);
                    return 0;

                default:
                    await error.WriteLineAsync(BaselineCommandParser.Usage).ConfigureAwait(false);
                    return 2;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await error.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }
    }

    private async Task<int> ListAsync(TextWriter output, CancellationToken cancellationToken)
    {
        IReadOnlyList<BaselineSummary> baselines = await library.ListAsync(cancellationToken).ConfigureAwait(false);
        if (baselines.Count == 0)
        {
            await output.WriteLineAsync("No baselines captured.").ConfigureAwait(false);
            return 0;
        }

        foreach (BaselineSummary baseline in baselines)
        {
            await output
                .WriteLineAsync(
                    $"{baseline.Id.Value}@{baseline.Version}\t{baseline.Name}\t{baseline.ScenarioCount} scenarios"
                    + $"\tcaptured {baseline.CapturedAt:yyyy-MM-dd HH:mm}\t{baseline.PluginId}/{baseline.ComparisonId}"
                    + $"\t{baseline.EnvironmentName ?? "-"}")
                .ConfigureAwait(false);
        }

        return 0;
    }

    private async Task<int> ExportAsync(
        BaselineCommandOptions options,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        BaselineId id = options.Id!.Value;
        int? version = options.Version;
        if (version is null)
        {
            BaselinePackageManifest? latest = await library.GetAsync(id, null, cancellationToken).ConfigureAwait(false);
            if (latest is null)
            {
                await error.WriteLineAsync($"Baseline '{id.Value}' was not found.").ConfigureAwait(false);
                return 2;
            }

            version = latest.Version;
        }

        await library.ExportAsync(id, version.Value, options.Path!, cancellationToken).ConfigureAwait(false);
        await output.WriteLineAsync($"Exported {id.Value}@{version} to {options.Path}.").ConfigureAwait(false);
        return 0;
    }
}
