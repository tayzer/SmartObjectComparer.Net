using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Application.Requests;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Engine;

namespace ParityBench.NET.Engine.Tests;

[TestClass]
public sealed class SelectableResponseComparerTests
{
    [TestMethod]
    public async Task CompareAsync_WhenModelNameIsAuto_UsesRawHashComparison()
    {
        FakeResponseComparer autoComparer = new FakeResponseComparer(RequestPairOutcome.Equal);
        FakeResponseComparer modelComparer = new FakeResponseComparer(RequestPairOutcome.Different);
        SelectableResponseComparer comparer = new SelectableResponseComparer(autoComparer, modelComparer, new FakeResponseModelRegistry());

        RequestPairResult result = await comparer
            .CompareAsync(CreateRequest(), CreateOptions("Auto"), null, null, null)
            .ConfigureAwait(false);

        Assert.AreEqual(RequestPairOutcome.Equal, result.Outcome);
        Assert.AreEqual(1, autoComparer.CallCount);
        Assert.AreEqual(0, modelComparer.CallCount);
    }

    [TestMethod]
    public async Task CompareAsync_WhenModelNameIsRegistered_UsesModelComparison()
    {
        FakeResponseComparer autoComparer = new FakeResponseComparer(RequestPairOutcome.Equal);
        FakeResponseComparer modelComparer = new FakeResponseComparer(RequestPairOutcome.Different);
        FakeResponseModelRegistry registry = new FakeResponseModelRegistry("CustomerResponse");
        SelectableResponseComparer comparer = new SelectableResponseComparer(autoComparer, modelComparer, registry);

        RequestPairResult result = await comparer
            .CompareAsync(CreateRequest(), CreateOptions("CustomerResponse"), null, null, null)
            .ConfigureAwait(false);

        Assert.AreEqual(RequestPairOutcome.Different, result.Outcome);
        Assert.AreEqual(0, autoComparer.CallCount);
        Assert.AreEqual(1, modelComparer.CallCount);
        Assert.AreEqual("CustomerResponse", registry.ResolvedModelName);
    }

    [TestMethod]
    public async Task CompareAsync_WhenModelNameIsUnknown_ThrowsInvalidOperationException()
    {
        SelectableResponseComparer comparer = new SelectableResponseComparer(
            new FakeResponseComparer(RequestPairOutcome.Equal),
            new FakeResponseComparer(RequestPairOutcome.Different),
            new FakeResponseModelRegistry("KnownModel"));

        await AssertThrowsAsync<InvalidOperationException>(() => comparer
            .CompareAsync(CreateRequest(), CreateOptions("UnknownModel"), null, null, null)).ConfigureAwait(false);
    }

    private static async Task AssertThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (TException)
        {
            return;
        }

        Assert.Fail($"Expected exception of type {typeof(TException).Name}.");
    }

    private static RequestItem CreateRequest() => new RequestItem("one.json");

    private static RunOptions CreateOptions(string modelName) =>
        new RunOptions(
            new RequestBatchReference("batch-1"),
            new EndpointDefinition(new Uri("https://a.example.test")),
            new EndpointDefinition(new Uri("https://b.example.test")),
            TimeSpan.FromSeconds(30),
            1,
            modelName);

    private sealed class FakeResponseComparer : IResponseComparer
    {
        private readonly RequestPairOutcome outcome;

        public FakeResponseComparer(RequestPairOutcome outcome)
        {
            this.outcome = outcome;
        }

        public int CallCount { get; private set; }

        public Task<RequestPairResult> CompareAsync(
            RequestItem request,
            RunOptions options,
            ResponseArtifactMetadata? responseA,
            ResponseArtifactMetadata? responseB,
            string? errorMessage,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new RequestPairResult(request.RelativePath, outcome, responseA, responseB, errorMessage));
        }
    }

    private sealed class FakeResponseModelRegistry : IResponseModelRegistry
    {
        private readonly string? registeredModelName;

        public FakeResponseModelRegistry(string? registeredModelName = null)
        {
            this.registeredModelName = registeredModelName;
        }

        public string? ResolvedModelName { get; private set; }

        public void Register<T>(string modelName) where T : class
        {
        }

        public Type Resolve(string modelName)
        {
            ResolvedModelName = modelName;
            if (!string.Equals(modelName, registeredModelName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Unknown model '{modelName}'.");
            }

            return typeof(object);
        }

        public IReadOnlyList<string> ListModelNames() =>
            registeredModelName is null ? Array.Empty<string>() : new[] { registeredModelName };
    }
}