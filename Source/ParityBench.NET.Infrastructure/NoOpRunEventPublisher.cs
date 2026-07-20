using ParityBench.NET.Application.Runs;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Infrastructure;

public sealed class NoOpRunEventPublisher : IRunEventPublisher
{
    public Task PublishAsync(
        RunEvent runEvent,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
