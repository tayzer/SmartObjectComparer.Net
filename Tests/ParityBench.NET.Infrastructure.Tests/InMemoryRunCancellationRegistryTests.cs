using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Infrastructure;

namespace ParityBench.NET.Infrastructure.Tests;

[TestClass]
public sealed class InMemoryRunCancellationRegistryTests
{
    [TestMethod]
    public void RequestCancellation_WhenRunIsRegistered_CancelsLinkedToken()
    {
        InMemoryRunCancellationRegistry registry = new InMemoryRunCancellationRegistry();
        RunId runId = new RunId("run-1");
        CancellationToken token = registry.CreateLinkedToken(runId, CancellationToken.None);

        bool cancelled = registry.RequestCancellation(runId);

        Assert.IsTrue(cancelled);
        Assert.IsTrue(token.IsCancellationRequested);
        Assert.IsTrue(registry.IsCancellationRequested(runId));
    }

    [TestMethod]
    public void Complete_WhenRunIsRegistered_UnregistersCancellation()
    {
        InMemoryRunCancellationRegistry registry = new InMemoryRunCancellationRegistry();
        RunId runId = new RunId("run-1");
        registry.CreateLinkedToken(runId, CancellationToken.None);
        registry.RequestCancellation(runId);

        registry.Complete(runId);

        Assert.IsFalse(registry.IsCancellationRequested(runId));
        Assert.IsFalse(registry.RequestCancellation(runId));
    }
}
