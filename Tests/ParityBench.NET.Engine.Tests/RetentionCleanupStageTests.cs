using System.Text;

using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Application.Requests;
using ParityBench.NET.Application.Runs.Retention;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Domain.Runs.Retention;
using ParityBench.NET.Engine.Pipeline;
using ParityBench.NET.Workspaces;

namespace ParityBench.NET.Engine.Tests;

[TestClass]
public sealed class RetentionCleanupStageTests
{
    [TestMethod]
    public async Task CleanupAsync_WhenDurableAppendIsFalse_ThrowsBeforeDelete()
    {
        TrackingArtifactStore artifactStore = new TrackingArtifactStore();
        RetentionCleanupStage stage = new RetentionCleanupStage(
            artifactStore,
            new NoOpRunDetailStore(),
            new RetentionPolicyEvaluator(),
            Options.Create(new RetentionConfiguration()));

        ComparisonRun run = CreateRun(new RunId("run-delete-guard"));
        CleanupStageContext context = new CleanupStageContext(
            run.Options,
            new RunDetailReference("runs/run-delete-guard/details/manifest.json"),
            new[]
            {
                new ComparedExecutionRecord(0, new RequestPairResult("one.json", RequestPairOutcome.Equal, CreateResponse(EndpointSlot.A, "runs/run-delete-guard/artifacts/A/one.json"), CreateResponse(EndpointSlot.B, "runs/run-delete-guard/artifacts/B/one.json"))),
            },
            DurableAppendCompleted: false);

        await AssertThrowsAsync<InvalidOperationException>(() => stage.CleanupAsync(run, context));
        Assert.AreEqual(0, artifactStore.DeleteCalls);
    }

    [TestMethod]
    public async Task CleanupAsync_WhenRetentionApplied_PersistsRetentionMetadataAndDeletesTrimmedArtifacts()
    {
        string workspaceRoot = CreateTempDirectory();
        try
        {
            RunId runId = new RunId("run-retention");
            FileSystemRunArtifactStore artifactStore = new FileSystemRunArtifactStore(workspaceRoot);
            FileSystemRunDetailStore detailStore = new FileSystemRunDetailStore(workspaceRoot);

            RequestItem request = new RequestItem("one.json", "application/json", 12);
            ResponseArtifactMetadata responseA = await artifactStore.SaveResponseAsync(runId, EndpointSlot.A, request, 200, "application/json", CreateStream("same-a"));
            ResponseArtifactMetadata responseB = await artifactStore.SaveResponseAsync(runId, EndpointSlot.B, request, 200, "application/json", CreateStream("same-b"));
            ResponseArtifactMetadata focusedA = await artifactStore.SaveResponseAsync(runId, EndpointSlot.A, new RequestItem("focused/A/one.json", "application/json", 6), 200, "application/json", CreateStream("focus-a"));
            ResponseArtifactMetadata focusedB = await artifactStore.SaveResponseAsync(runId, EndpointSlot.B, new RequestItem("focused/B/one.json", "application/json", 6), 200, "application/json", CreateStream("focus-b"));

            RequestPairResult persisted = new RequestPairResult(
                "one.json",
                RequestPairOutcome.Equal,
                responseA,
                responseB,
                focusedResponseA: focusedA,
                focusedResponseB: focusedB,
                focusedRawContentIgnorePaths: new[] { "token" });

            RunDetailReference detailReference = await detailStore.SaveDetailsAsync(runId, new[] { persisted });
            CleanupStageContext context = new CleanupStageContext(
                CreateRunOptions(),
                detailReference,
                new[] { new ComparedExecutionRecord(0, persisted) },
                DurableAppendCompleted: true);

            RetentionCleanupStage stage = new RetentionCleanupStage(
                artifactStore,
                detailStore,
                new RetentionPolicyEvaluator(),
                Options.Create(new RetentionConfiguration
                {
                    Mode = RetentionMode.TrimmedEqualsAndIgnoredPaths,
                    NonSuccessOverride = NonSuccessRetentionOverride.KeepBounded,
                }));

            await stage.CleanupAsync(CreateRun(runId), context);

            IReadOnlyList<RequestPairResult> updated = await detailStore.LoadDetailsAsync(detailReference);
            Assert.AreEqual(1, updated.Count);
            Assert.IsNotNull(updated[0].RetentionAppliedAt);
            Assert.AreEqual(ArtifactRetentionState.TrimmedByPolicy, updated[0].ArtifactRetentionState.RawResponseA);
            Assert.AreEqual(ArtifactRetentionState.TrimmedByPolicy, updated[0].ArtifactRetentionState.RawResponseB);
            Assert.AreEqual(ArtifactRetentionState.TrimmedByPolicy, updated[0].ArtifactRetentionState.FocusedResponseA);
            Assert.AreEqual(ArtifactRetentionState.TrimmedByPolicy, updated[0].ArtifactRetentionState.FocusedResponseB);

            Assert.IsFalse(await artifactStore.ExistsAsync(responseA.Artifact));
            Assert.IsFalse(await artifactStore.ExistsAsync(responseB.Artifact));
            Assert.IsFalse(await artifactStore.ExistsAsync(focusedA.Artifact));
            Assert.IsFalse(await artifactStore.ExistsAsync(focusedB.Artifact));
        }
        finally
        {
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }
        }
    }

    private static ComparisonRun CreateRun(RunId runId) =>
        ComparisonRun.Create(runId, CreateRunOptions());

    private static RunOptions CreateRunOptions() =>
        new RunOptions(
            new RequestBatchReference("batch-1"),
            new EndpointDefinition(new Uri("https://service-a.example.test")),
            new EndpointDefinition(new Uri("https://service-b.example.test")),
            TimeSpan.FromSeconds(30),
            2,
            comparisonOptions: new ComparisonOptions());

    private static ResponseArtifactMetadata CreateResponse(EndpointSlot endpoint, string artifactId) =>
        new ResponseArtifactMetadata(
            endpoint,
            new ArtifactReference(artifactId, "application/json"),
            200,
            "application/json",
            2,
            "abc");

    private static MemoryStream CreateStream(string value) =>
        new MemoryStream(Encoding.UTF8.GetBytes(value));

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "ParityBenchNET.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
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
        catch (Exception ex)
        {
            Assert.Fail($"Expected {typeof(TException).Name}, but got {ex.GetType().Name}.");
        }

        Assert.Fail($"Expected {typeof(TException).Name}, but no exception was thrown.");
    }

    private sealed class TrackingArtifactStore : IRunArtifactStore
    {
        public int DeleteCalls { get; private set; }

        public Task<ResponseArtifactMetadata> SaveResponseAsync(
            RunId runId,
            EndpointSlot endpoint,
            RequestItem request,
            int statusCode,
            string? contentType,
            Stream body,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Stream> OpenReadAsync(ArtifactReference artifact, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> DeleteIfExistsAsync(ArtifactReference artifact, CancellationToken cancellationToken = default)
        {
            DeleteCalls++;
            return Task.FromResult(true);
        }
    }

    private sealed class NoOpRunDetailStore : IRunDetailStore
    {
        public Task<RunDetailReference> SaveDetailsAsync(RunId runId, IReadOnlyList<RequestPairResult> results, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RunDetailReference($"runs/{runId.Value}/details/manifest.json"));

        public Task<IReadOnlyList<RequestPairResult>> LoadDetailsAsync(RunDetailReference detailReference, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RequestPairResult>>(Array.Empty<RequestPairResult>());

        public Task<ParityBench.NET.Domain.Results.RunDetailPage> LoadPageAsync(ParityBench.NET.Domain.Runs.RunDetailReference detailReference, ParityBench.NET.Domain.Results.RunDetailQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
