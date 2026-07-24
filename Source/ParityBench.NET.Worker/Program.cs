using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using ParityBench.NET.Application.Runs;
using ParityBench.NET.Application.Runs.Worker;
using ParityBench.NET.Composition;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Worker;

/// <summary>
/// The out-of-process run worker. It builds the same workspace composition the
/// host uses, loads the run the host already persisted, executes it in-process
/// (loading any selected plugin into an isolated context here, never in the host),
/// and streams progress and the final summary back over the named pipe.
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        WorkerArguments arguments;
        try
        {
            arguments = WorkerArguments.Parse(args);
        }
        catch (Exception exception)
        {
            await Console.Error.WriteLineAsync($"Invalid worker arguments: {exception.Message}").ConfigureAwait(false);
            return 2;
        }

        await using System.IO.Pipes.NamedPipeClientStream pipe = WorkerChannel.CreateClientPipe(arguments.PipeName);
        try
        {
            await pipe.ConnectAsync(30_000).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is TimeoutException or IOException)
        {
            await Console.Error.WriteLineAsync($"Could not connect to host pipe: {exception.Message}").ConfigureAwait(false);
            return 3;
        }

        await using WorkerChannel channel = new WorkerChannel(pipe);
        using CancellationTokenSource cancellation = new CancellationTokenSource();

        // Watch for a cancel frame from the host in the background so a cancellation
        // arriving mid-run unwinds the executor cooperatively.
        Task cancelWatch = WatchForCancellationAsync(channel, cancellation);

        try
        {
            RunResultSummary summary = await ExecuteRunAsync(arguments, channel, cancellation.Token).ConfigureAwait(false);
            await channel
                .WriteFrameAsync(WorkerProtocol.Serialize(new WorkerProtocol.WorkerFrame(WorkerProtocol.WorkerFrameKind.Summary, Summary: summary)))
                .ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            await channel
                .WriteFrameAsync(WorkerProtocol.Serialize(new WorkerProtocol.WorkerFrame(
                    WorkerProtocol.WorkerFrameKind.Error,
                    ErrorMessage: "The run was cancelled.")))
                .ConfigureAwait(false);
            return 0;
        }
        catch (Exception exception)
        {
            await Console.Error.WriteLineAsync(exception.ToString()).ConfigureAwait(false);
            await channel
                .WriteFrameAsync(WorkerProtocol.Serialize(new WorkerProtocol.WorkerFrame(
                    WorkerProtocol.WorkerFrameKind.Error,
                    ErrorMessage: exception.Message)))
                .ConfigureAwait(false);
            return 1;
        }
        finally
        {
            cancellation.Cancel();
            await cancelWatch.ConfigureAwait(false);
        }
    }

    private static async Task<RunResultSummary> ExecuteRunAsync(
        WorkerArguments arguments,
        WorkerChannel channel,
        CancellationToken cancellationToken)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            // Environment variables carry secrets (PB_SECRET_*) and any plugin-directory
            // overrides through to the worker without a shared config file.
            .AddEnvironmentVariables()
            .Build();

        ServiceCollection services = new ServiceCollection();
        // The workspace composition depends on ILogger<T>; the worker logs to stderr,
        // which the host captures for diagnostics if the run fails.
        services.AddLogging(builder => builder.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace));
        services.AddParityBenchWorkspaceServices(
            configuration,
            arguments.WorkspaceRoot,
            arguments.FixtureBaseUrl);

        await using ServiceProvider provider = services.BuildServiceProvider();

        IRunStore runStore = provider.GetRequiredService<IRunStore>();
        ComparisonRun run = await runStore.LoadAsync(new RunId(arguments.RunId), cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Run '{arguments.RunId}' was not found in the workspace.");

        IComparisonRunExecutor executor = provider.GetRequiredService<IComparisonRunExecutor>();
        return await executor
            .ExecuteAsync(run, new PipeProgressReporter(channel), cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task WatchForCancellationAsync(WorkerChannel channel, CancellationTokenSource cancellation)
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                string? line = await channel.ReadFrameAsync(cancellation.Token).ConfigureAwait(false);
                if (line is null)
                {
                    return;
                }

                WorkerProtocol.HostFrame? frame = WorkerProtocol.Deserialize<WorkerProtocol.HostFrame>(line);
                if (frame?.Kind == WorkerProtocol.HostFrameKind.Cancel)
                {
                    cancellation.Cancel();
                    return;
                }
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or ObjectDisposedException)
        {
        }
    }

    /// <summary>
    /// Forwards executor progress to the host as progress frames.
    /// </summary>
    private sealed class PipeProgressReporter : IRunProgressReporter
    {
        private readonly WorkerChannel channel;

        public PipeProgressReporter(WorkerChannel channel) => this.channel = channel;

        public Task ReportAsync(RunStatus status, RunProgress progress, CancellationToken cancellationToken = default) =>
            ReportAsync(status, progress, cancellationToken, force: false);

        public async Task ReportAsync(RunStatus status, RunProgress progress, CancellationToken cancellationToken, bool force)
        {
            string frame = WorkerProtocol.Serialize(new WorkerProtocol.WorkerFrame(
                WorkerProtocol.WorkerFrameKind.Progress,
                Progress: WorkerProtocol.ProgressPayload.From(status, progress, force)));
            await channel.WriteFrameAsync(frame, cancellationToken).ConfigureAwait(false);
        }
    }
}
