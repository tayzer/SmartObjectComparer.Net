using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Application.Runs;

/// <summary>
/// Coordinates cancellation tokens for active runs by run identity.
/// </summary>
public interface IRunCancellationRegistry
{
    /// <summary>
    /// Creates the execution token used by the active run and links it to the caller token.
    /// </summary>
    CancellationToken CreateLinkedToken(RunId runId, CancellationToken cancellationToken);

    /// <summary>
    /// Requests cancellation for an active run.
    /// </summary>
    bool RequestCancellation(RunId runId);

    /// <summary>
    /// Returns whether cancellation has been requested for an active run.
    /// </summary>
    bool IsCancellationRequested(RunId runId);

    /// <summary>
    /// Removes the active run registration after the run reaches a terminal state.
    /// </summary>
    void Complete(RunId runId);
}
