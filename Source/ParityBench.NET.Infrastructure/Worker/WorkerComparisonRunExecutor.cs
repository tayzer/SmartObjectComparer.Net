using System.Diagnostics;

using Microsoft.Extensions.Options;

using ParityBench.NET.Application.Runs;
using ParityBench.NET.Application.Runs.Worker;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Infrastructure.Worker;

/// <summary>
/// Runs a comparison in a separate worker process and relays its progress and
/// result back across a named pipe.
/// </summary>
/// <remarks>
/// Client plugin code executes only in the worker, so a plugin crash, hang or
/// dependency conflict fails the run without taking the host down. The worker
/// writes artifacts and paged details to the same workspace, so only the summary
/// crosses the pipe and the large-run bounded-memory property is preserved.
/// </remarks>
public sealed class WorkerComparisonRunExecutor : IComparisonRunExecutor
{
    private readonly WorkerExecutionOptions options;

    public WorkerComparisonRunExecutor(IOptions<WorkerExecutionOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.options = options.Value;
    }

    public async Task<RunResultSummary> ExecuteAsync(
        ComparisonRun run,
        IRunProgressReporter progressReporter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(progressReporter);

        string pipeName = $"paritybench-run-{Guid.NewGuid():n}";

        await using System.IO.Pipes.NamedPipeServerStream pipeServer = WorkerChannel.CreateServerPipe(pipeName);

        using Process process = StartWorkerProcess(run.Id, pipeName);
        StderrCollector stderr = new StderrCollector(process);

        try
        {
            await WaitForConnectionAsync(pipeServer, process, stderr, cancellationToken).ConfigureAwait(false);

            await using WorkerChannel channel = new WorkerChannel(pipeServer);
            return await PumpFramesAsync(channel, process, stderr, progressReporter, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await TerminateAsync(process).ConfigureAwait(false);
            throw;
        }
        finally
        {
            await EnsureExitedAsync(process).ConfigureAwait(false);
        }
    }

    // Races the pipe connection against the worker exiting: a worker that fails to
    // start (missing executable, bad args) exits without connecting, and we want to
    // surface that as a startup failure immediately rather than after the timeout.
    private static async Task WaitForConnectionAsync(
        System.IO.Pipes.NamedPipeServerStream pipeServer,
        Process process,
        StderrCollector stderr,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectTimeout.CancelAfter(TimeSpan.FromSeconds(30));

        Task connectTask = pipeServer.WaitForConnectionAsync(connectTimeout.Token);
        Task exitTask = process.WaitForExitAsync(connectTimeout.Token);

        Task completed = await Task.WhenAny(connectTask, exitTask).ConfigureAwait(false);
        if (completed == connectTask)
        {
            await connectTask.ConfigureAwait(false);
            return;
        }

        // The process exited before it connected.
        if (cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        throw new InvalidOperationException(BuildWorkerFailureMessage(process, stderr));
    }

    private async Task<RunResultSummary> PumpFramesAsync(
        WorkerChannel channel,
        Process process,
        StderrCollector stderr,
        IRunProgressReporter progressReporter,
        CancellationToken cancellationToken)
    {
        // A cancellation request is forwarded as a frame first; the worker unwinds
        // gracefully and the grace-period kill in TerminateAsync is the backstop.
        await using CancellationTokenRegistration cancelForward = cancellationToken.Register(() =>
            _ = ForwardCancelAsync(channel, process));

        while (true)
        {
            string? line = await channel.ReadFrameAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                // Pipe closed with no terminal frame: the worker died mid-run.
                throw new InvalidOperationException(BuildWorkerFailureMessage(process, stderr));
            }

            WorkerProtocol.WorkerFrame? frame = WorkerProtocol.Deserialize<WorkerProtocol.WorkerFrame>(line);
            if (frame is null)
            {
                continue;
            }

            switch (frame.Kind)
            {
                case WorkerProtocol.WorkerFrameKind.Progress when frame.Progress is not null:
                    await progressReporter
                        .ReportAsync(frame.Progress.Status, frame.Progress.ToProgress(), cancellationToken, frame.Progress.Force)
                        .ConfigureAwait(false);
                    break;

                case WorkerProtocol.WorkerFrameKind.Summary when frame.Summary is not null:
                    return frame.Summary;

                case WorkerProtocol.WorkerFrameKind.Error:
                    throw new InvalidOperationException(
                        frame.ErrorMessage ?? "The run worker reported an unspecified failure.");
            }
        }
    }

    private static async Task ForwardCancelAsync(WorkerChannel channel, Process process)
    {
        try
        {
            await channel
                .WriteFrameAsync(WorkerProtocol.Serialize(new WorkerProtocol.HostFrame(WorkerProtocol.HostFrameKind.Cancel)))
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            // The worker may have exited already; the kill path handles the rest.
        }
    }

    private Process StartWorkerProcess(RunId runId, string pipeName)
    {
        string workerPath = options.ResolveWorkerExecutablePath();
        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // A .dll is run through the dotnet muxer; a self-contained exe is run directly.
        if (workerPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.FileName = "dotnet";
            startInfo.ArgumentList.Add(workerPath);
        }
        else
        {
            startInfo.FileName = workerPath;
        }

        startInfo.ArgumentList.Add("--workspace");
        startInfo.ArgumentList.Add(options.WorkspaceRoot);
        startInfo.ArgumentList.Add("--run");
        startInfo.ArgumentList.Add(runId.Value);
        startInfo.ArgumentList.Add("--pipe");
        startInfo.ArgumentList.Add(pipeName);
        startInfo.ArgumentList.Add("--fixture-base-url");
        startInfo.ArgumentList.Add(options.FixtureBaseUrl);

        Process process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start the run worker process '{workerPath}'.");
        }

        return process;
    }

    private async Task TerminateAsync(Process process)
    {
        try
        {
            if (process.HasExited)
            {
                return;
            }

            // Give the worker the grace period to unwind after the cancel frame,
            // then force it down so a wedged plugin cannot keep the process alive.
            using CancellationTokenSource graceTimeout = new CancellationTokenSource(options.CancellationGracePeriod);
            await process.WaitForExitAsync(graceTimeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            KillTree(process);
        }
        catch (InvalidOperationException)
        {
            // The process already exited between the check and the wait.
        }
    }

    private static async Task EnsureExitedAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                KillTree(process);
                await process.WaitForExitAsync().ConfigureAwait(false);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static void KillTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    private static string BuildWorkerFailureMessage(Process process, StderrCollector stderr)
    {
        string exitInfo = process.HasExited ? $" (exit code {process.ExitCode})" : string.Empty;
        string tail = stderr.Tail();
        return string.IsNullOrWhiteSpace(tail)
            ? $"The run worker exited without producing a result{exitInfo}."
            : $"The run worker exited without producing a result{exitInfo}: {tail}";
    }

    /// <summary>
    /// Captures the worker's stderr so a crash surfaces a reason instead of a bare
    /// exit code. Only the tail is kept — enough to diagnose, bounded in memory.
    /// </summary>
    private sealed class StderrCollector
    {
        private const int MaxLines = 50;
        private readonly Queue<string> lines = new Queue<string>();
        private readonly object gate = new object();

        public StderrCollector(Process process)
        {
            process.ErrorDataReceived += (_, args) =>
            {
                if (args.Data is null)
                {
                    return;
                }

                lock (gate)
                {
                    lines.Enqueue(args.Data);
                    while (lines.Count > MaxLines)
                    {
                        lines.Dequeue();
                    }
                }
            };
            process.BeginErrorReadLine();
        }

        public string Tail()
        {
            lock (gate)
            {
                return string.Join(Environment.NewLine, lines);
            }
        }
    }
}
