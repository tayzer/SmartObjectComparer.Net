namespace ParityBench.NET.Infrastructure.Worker;

/// <summary>
/// Configures how the host launches the out-of-process run worker.
/// </summary>
public sealed class WorkerExecutionOptions
{
    /// <summary>Gets or sets the workspace root the worker reads and writes.</summary>
    public string WorkspaceRoot { get; set; } = string.Empty;

    /// <summary>Gets or sets the fixture base URL the worker's composition needs.</summary>
    public string FixtureBaseUrl { get; set; } = "http://localhost";

    /// <summary>
    /// Gets or sets the worker executable. A <c>.dll</c> is launched via the
    /// <c>dotnet</c> muxer; anything else is launched directly. Defaults to
    /// <c>ParityBench.NET.Worker.dll</c> beside the host binary.
    /// </summary>
    public string? WorkerExecutablePath { get; set; }

    /// <summary>
    /// Gets or sets how long to wait after requesting cancellation before killing
    /// the worker process.
    /// </summary>
    public TimeSpan CancellationGracePeriod { get; set; } = TimeSpan.FromSeconds(10);

    public string ResolveWorkerExecutablePath()
    {
        if (!string.IsNullOrWhiteSpace(WorkerExecutablePath))
        {
            return Path.IsPathFullyQualified(WorkerExecutablePath)
                ? WorkerExecutablePath
                : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, WorkerExecutablePath));
        }

        string workerRoot = Path.Combine(AppContext.BaseDirectory, "worker");
        string executable = Path.Combine(workerRoot, OperatingSystem.IsWindows()
            ? "ParityBench.NET.Worker.exe"
            : "ParityBench.NET.Worker");
        return File.Exists(executable)
            ? executable
            : Path.Combine(workerRoot, "ParityBench.NET.Worker.dll");
    }
}
