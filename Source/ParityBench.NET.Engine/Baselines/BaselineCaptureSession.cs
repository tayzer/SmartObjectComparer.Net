using ParityBench.NET.Application.Baselines;
using ParityBench.NET.Application.ContractProfiles;
using ParityBench.NET.Application.Plugins;
using ParityBench.NET.Application.Requests;
using ParityBench.NET.Domain.Baselines;
using ParityBench.NET.Domain.ContractProfiles;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Engine.Pipeline.BuiltIn;

using ParityBench.PluginSdk.Pipeline;

namespace ParityBench.NET.Engine.Baselines;

/// <summary>
/// Records the scenarios a capture run executed into a new baseline package version.
/// </summary>
/// <remarks>
/// Each scenario is written as three payloads: the request that produced it, the raw
/// response as it came off the wire, and the comparison model it mapped to. The model
/// is what a later replay compares against, so it is serialized here rather than
/// reused from the run's artifacts — that keeps the stored expected value in one
/// format (JSON) whatever the endpoint originally spoke.
/// </remarks>
public sealed class BaselineCaptureSession
{
    private readonly IBaselineStore store;
    private readonly IRequestBatchStore requestBatchStore;
    private readonly IRunArtifactStore runArtifactStore;
    private readonly IContractPayloadSerializer serializer;
    private readonly Type comparisonType;
    private readonly RequestBatchReference requestBatch;

    private int capturedCount;

    private BaselineCaptureSession(
        IBaselineStore store,
        IRequestBatchStore requestBatchStore,
        IRunArtifactStore runArtifactStore,
        IContractPayloadSerializer serializer,
        Type comparisonType,
        RequestBatchReference requestBatch,
        BaselineBinding binding,
        BaselinePackageManifest manifest)
    {
        this.store = store;
        this.requestBatchStore = requestBatchStore;
        this.runArtifactStore = runArtifactStore;
        this.serializer = serializer;
        this.comparisonType = comparisonType;
        this.requestBatch = requestBatch;
        Binding = binding;
        Manifest = manifest;
    }

    public BaselineBinding Binding { get; }

    /// <summary>Gets the reserved package version this run is writing into.</summary>
    public BaselinePackageManifest Manifest { get; }

    public int CapturedCount => Volatile.Read(ref capturedCount);

    public static async Task<BaselineCaptureSession> BeginAsync(
        IBaselineStore store,
        IRequestBatchStore requestBatchStore,
        IRunArtifactStore runArtifactStore,
        IContractPayloadSerializer? serializer,
        ComparisonRun run,
        RunOptions comparisonOptions,
        ComparisonExecutionPlan plan,
        BaselineBinding binding,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(binding);

        if (serializer is null)
        {
            throw new InvalidOperationException("A contract payload serializer is required to capture a baseline.");
        }

        PluginComparisonSelection selection = comparisonOptions.PluginComparison
            ?? throw new InvalidOperationException("Baseline capture requires a plugin comparison.");

        EndpointDefinition captureEndpoint = binding.BaselineSlot == EndpointSlot.A
            ? comparisonOptions.EndpointA
            : comparisonOptions.EndpointB;

        BaselineCaptureRequest request = new BaselineCaptureRequest(
            binding.CaptureName ?? throw new InvalidOperationException("A baseline capture run must name the baseline."),
            captureEndpoint.Uri,
            selection.PluginId,
            selection.ComparisonId,
            run.StartedAt ?? DateTimeOffset.UtcNow,
            run.Id.Value,
            selection.PluginVersion,
            selection.EnvironmentName,
            captureEndpoint.Label,
            comparisonOptions.ComparisonRulesSnapshotHash,
            comparisonOptions.Comparison);

        BaselinePackageManifest manifest = await store.BeginCaptureAsync(request, cancellationToken).ConfigureAwait(false);

        return new BaselineCaptureSession(
            store,
            requestBatchStore,
            runArtifactStore,
            serializer,
            plan.Definition.ComparisonType,
            comparisonOptions.RequestBatch,
            binding,
            manifest);
    }

    /// <summary>
    /// Records one successfully executed scenario. Called from the compare pool, after
    /// the mapping phase has produced the comparison instance.
    /// </summary>
    public async Task CaptureAsync(
        RequestItem request,
        IEndpointPipelineContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        object comparisonInstance = context.ComparisonInstance
            ?? throw new InvalidOperationException(
                $"Endpoint {context.Endpoint} of '{request.RelativePath}' produced no comparison model to capture.");

        ResponseArtifactMetadata mappedArtifact = context.ResponseArtifact
            ?? throw new InvalidOperationException(
                $"Endpoint {context.Endpoint} of '{request.RelativePath}' persisted no response to capture.");

        ResponseArtifactMetadata rawArtifact = context.Items.TryGetValue(BuiltInStepIds.RawResponseArtifactItem, out object? stashed)
            && stashed is ResponseArtifactMetadata rawMetadata
            ? rawMetadata
            : mappedArtifact;

        MemoryStream canonicalBody = new MemoryStream();
        try
        {
            await serializer
                .SerializeAsync(comparisonInstance, comparisonType, PayloadFormat.Json, canonicalBody, cancellationToken)
                .ConfigureAwait(false);
            canonicalBody.Position = 0;

            await store.AppendScenarioAsync(
                Manifest.Id,
                Manifest.Version,
                new BaselineScenarioCapture(
                    request.RelativePath,
                    request.ContentType,
                    request.Headers,
                    mappedArtifact.StatusCode,
                    rawArtifact.ContentType,
                    token => requestBatchStore.OpenRequestBodyAsync(requestBatch, request, token),
                    _ =>
                    {
                        canonicalBody.Position = 0;
                        return Task.FromResult<Stream>(new NonDisposingStream(canonicalBody));
                    },
                    token => runArtifactStore.OpenReadAsync(rawArtifact.Artifact, token)),
                cancellationToken).ConfigureAwait(false);

            Interlocked.Increment(ref capturedCount);
        }
        finally
        {
            await canonicalBody.DisposeAsync().ConfigureAwait(false);
        }
    }

    public Task<BaselinePackageManifest> CompleteAsync(CancellationToken cancellationToken) =>
        store.CompleteCaptureAsync(Manifest.Id, Manifest.Version, cancellationToken);

    public Task AbandonAsync(CancellationToken cancellationToken) =>
        store.AbandonCaptureAsync(Manifest.Id, Manifest.Version, CancellationToken.None);

    /// <summary>
    /// Lets the store own stream lifetime uniformly while the caller keeps the buffer
    /// it is about to reuse.
    /// </summary>
    private sealed class NonDisposingStream : Stream
    {
        private readonly Stream inner;

        public NonDisposingStream(Stream inner)
        {
            this.inner = inner;
        }

        public override bool CanRead => inner.CanRead;

        public override bool CanSeek => inner.CanSeek;

        public override bool CanWrite => false;

        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            inner.ReadAsync(buffer, offset, count, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
        }

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
