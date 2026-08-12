using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.ContractProfiles;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Engine.Pipeline;
using ParityBench.NET.Engine.Pipeline.BuiltIn;
using ParityBench.PluginSdk.Pipeline;

namespace ParityBench.NET.Engine.Tests;

[TestClass]
public sealed class ComparisonPipelineTests
{
    [TestMethod]
    public async Task ExecuteEndpointAsync_WhenStepsDeclareLaterPhaseFirst_RunsInPhaseOrder()
    {
        List<string> executed = new List<string>();
        ComparisonPipeline pipeline = new ComparisonPipelineBuilder()
            // Registered out of order, and the mapping step claims an order that
            // would put it first if ordering were not phase-bucketed.
            .Add(new RecordingEndpointStep("map", PipelinePhase.Mapping, order: -100, executed))
            .Add(new RecordingEndpointStep("transport", PipelinePhase.Transport, order: 0, executed))
            .Add(new RecordingEndpointStep("input", PipelinePhase.Input, order: 50, executed))
            .Build();

        await pipeline.ExecuteEndpointAsync(CreateEndpointContext());

        CollectionAssert.AreEqual(new[] { "input", "transport", "map" }, executed);
    }

    [TestMethod]
    public async Task ExecuteEndpointAsync_WhenStepsShareAPhase_OrdersByOrderThenRegistration()
    {
        List<string> executed = new List<string>();
        ComparisonPipeline pipeline = new ComparisonPipelineBuilder()
            .Add(new RecordingEndpointStep("second", PipelinePhase.Request, order: 10, executed))
            .Add(new RecordingEndpointStep("third", PipelinePhase.Request, order: 10, executed))
            .Add(new RecordingEndpointStep("first", PipelinePhase.Request, order: 1, executed))
            .Build();

        await pipeline.ExecuteEndpointAsync(CreateEndpointContext());

        CollectionAssert.AreEqual(new[] { "first", "second", "third" }, executed);
    }

    [TestMethod]
    public async Task ExecuteEndpointAsync_WhenStepDoesNotCallNext_SkipsLaterSteps()
    {
        List<string> executed = new List<string>();
        ComparisonPipeline pipeline = new ComparisonPipelineBuilder()
            .Add(new RecordingEndpointStep("input", PipelinePhase.Input, order: 0, executed))
            .Add(new ShortCircuitEndpointStep("stop", PipelinePhase.Request, executed))
            .Add(new RecordingEndpointStep("transport", PipelinePhase.Transport, order: 0, executed))
            .Build();

        EndpointPipelineContext context = CreateEndpointContext();
        await pipeline.ExecuteEndpointAsync(context);

        CollectionAssert.AreEqual(new[] { "input", "stop" }, executed);
        Assert.IsTrue(context.IsFailed);
        Assert.AreEqual("stopped by stop", context.FailureReason);
    }

    [TestMethod]
    public async Task ExecuteEndpointAsync_WhenStepFailsButCallsNext_StopsBeforeLaterSteps()
    {
        List<string> executed = new List<string>();
        ComparisonPipeline pipeline = new ComparisonPipelineBuilder()
            .Add(new FailingButContinuingEndpointStep("fail", PipelinePhase.Request, executed))
            .Add(new RecordingEndpointStep("transport", PipelinePhase.Transport, order: 0, executed))
            .Build();

        EndpointPipelineContext context = CreateEndpointContext();
        await pipeline.ExecuteEndpointAsync(context);

        CollectionAssert.AreEqual(new[] { "fail" }, executed);
        Assert.AreEqual("failed in fail", context.FailureReason);
    }

    [TestMethod]
    public void Add_WhenEndpointStepDeclaresPairPhase_Throws()
    {
        ComparisonPipelineBuilder builder = new ComparisonPipelineBuilder();

        InvalidOperationException exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
            builder.Add(new RecordingEndpointStep("mismatch", PipelinePhase.Comparison, order: 0, new List<string>())));

        StringAssert.Contains(exception.Message, "Comparison");
    }

    [TestMethod]
    public void Add_WhenStepIdIsAlreadyRegistered_Throws()
    {
        ComparisonPipelineBuilder builder = new ComparisonPipelineBuilder()
            .Add(new RecordingEndpointStep("duplicate", PipelinePhase.Input, order: 0, new List<string>()));

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            builder.Add(new RecordingEndpointStep("duplicate", PipelinePhase.Request, order: 0, new List<string>())));
    }

    [TestMethod]
    public async Task ExecutePairAsync_WhenPairStepsRegistered_RunsThemInPhaseOrder()
    {
        List<string> executed = new List<string>();
        ComparisonPipeline pipeline = new ComparisonPipelineBuilder()
            .Add(new RecordingPairStep("post", PipelinePhase.ResultProcessing, executed))
            .Add(new RecordingPairStep("compare", PipelinePhase.Comparison, executed))
            .Add(new RecordingEndpointStep("input", PipelinePhase.Input, order: 0, executed))
            .Build();

        EndpointPipelineContext endpointA = CreateEndpointContext(EndpointSlot.A);
        EndpointPipelineContext endpointB = CreateEndpointContext(EndpointSlot.B);
        PairPipelineContext context = new PairPipelineContext(
            "run-1",
            endpointA.Request,
            endpointA,
            endpointB,
            new ComparisonOptions(),
            EmptyServiceProvider.Instance,
            new PipelineConfiguration());

        await pipeline.ExecutePairAsync(context);

        // The endpoint step is not part of the pair chain.
        CollectionAssert.AreEqual(new[] { "compare", "post" }, executed);
    }

    [TestMethod]
    public async Task ExecutePairAsync_AfterBuiltInComparison_DownstreamStepObservesOriginalModels()
    {
        MutableComparisonModel leftModel = new() { Ignored = "left", Values = [2, 1] };
        MutableComparisonModel rightModel = new() { Ignored = "right", Values = [1, 2] };
        EndpointPipelineContext endpointA = CreateEndpointContext(EndpointSlot.A);
        EndpointPipelineContext endpointB = CreateEndpointContext(EndpointSlot.B);
        endpointA.ComparisonInstance = leftModel;
        endpointB.ComparisonInstance = rightModel;
        bool observedOriginal = false;
        ComparisonPipeline pipeline = new ComparisonPipelineBuilder()
            .Add(new CompareNetObjectsMiddleware())
            .Add(new ObservingPairStep(context =>
            {
                MutableComparisonModel observedLeft = (MutableComparisonModel)context.ComparisonA!;
                MutableComparisonModel observedRight = (MutableComparisonModel)context.ComparisonB!;
                observedOriginal = observedLeft.Ignored == "left"
                    && observedRight.Ignored == "right"
                    && observedLeft.Values!.SequenceEqual([2, 1])
                    && observedRight.Values!.SequenceEqual([1, 2]);
            }))
            .Build();
        PairPipelineContext context = new(
            "run-1",
            endpointA.Request,
            endpointA,
            endpointB,
            new ComparisonOptions(ignoreCollectionOrder: true, ignoreRules: [new IgnoreRuleDefinition("Ignored")]),
            EmptyServiceProvider.Instance,
            new PipelineConfiguration());

        await pipeline.ExecutePairAsync(context);

        Assert.IsTrue(observedOriginal);
        Assert.IsTrue(context.Result.AreEqual);
    }

    private static EndpointPipelineContext CreateEndpointContext(EndpointSlot endpoint = EndpointSlot.A) =>
        new EndpointPipelineContext(
            "run-1",
            new RequestItem("one.json", "application/json"),
            endpoint,
            new EndpointDefinition(new Uri("https://example.test/a")),
            PayloadFormat.Json,
            "application/json",
            _ => ValueTask.FromResult<Stream>(new MemoryStream()),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            EmptyServiceProvider.Instance,
            new PipelineConfiguration());

    private sealed class RecordingEndpointStep : IEndpointComparisonMiddleware
    {
        private readonly List<string> executed;

        public RecordingEndpointStep(string stepId, PipelinePhase phase, int order, List<string> executed)
        {
            StepId = stepId;
            Phase = phase;
            Order = order;
            this.executed = executed;
        }

        public string StepId { get; }

        public PipelinePhase Phase { get; }

        public int Order { get; }

        public ValueTask InvokeAsync(IEndpointPipelineContext context, PipelineDelegate next, CancellationToken cancellationToken)
        {
            executed.Add(StepId);
            return next(cancellationToken);
        }
    }

    private sealed class ShortCircuitEndpointStep : IEndpointComparisonMiddleware
    {
        private readonly List<string> executed;

        public ShortCircuitEndpointStep(string stepId, PipelinePhase phase, List<string> executed)
        {
            StepId = stepId;
            Phase = phase;
            this.executed = executed;
        }

        public string StepId { get; }

        public PipelinePhase Phase { get; }

        public int Order => 0;

        public ValueTask InvokeAsync(IEndpointPipelineContext context, PipelineDelegate next, CancellationToken cancellationToken)
        {
            executed.Add(StepId);
            context.Fail($"stopped by {StepId}");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FailingButContinuingEndpointStep : IEndpointComparisonMiddleware
    {
        private readonly List<string> executed;

        public FailingButContinuingEndpointStep(string stepId, PipelinePhase phase, List<string> executed)
        {
            StepId = stepId;
            Phase = phase;
            this.executed = executed;
        }

        public string StepId { get; }

        public PipelinePhase Phase { get; }

        public int Order => 0;

        public ValueTask InvokeAsync(IEndpointPipelineContext context, PipelineDelegate next, CancellationToken cancellationToken)
        {
            executed.Add(StepId);
            context.Fail($"failed in {StepId}");
            return next(cancellationToken);
        }
    }

    private sealed class RecordingPairStep : IPairComparisonMiddleware
    {
        private readonly List<string> executed;

        public RecordingPairStep(string stepId, PipelinePhase phase, List<string> executed)
        {
            StepId = stepId;
            Phase = phase;
            this.executed = executed;
        }

        public string StepId { get; }

        public PipelinePhase Phase { get; }

        public int Order => 0;

        public ValueTask InvokeAsync(IPairPipelineContext context, PipelineDelegate next, CancellationToken cancellationToken)
        {
            executed.Add(StepId);
            return next(cancellationToken);
        }
    }

    private sealed class ObservingPairStep(Action<IPairPipelineContext> observe) : IPairComparisonMiddleware
    {
        public string StepId => "observe-original-models";
        public PipelinePhase Phase => PipelinePhase.ResultProcessing;
        public int Order => 0;

        public ValueTask InvokeAsync(IPairPipelineContext context, PipelineDelegate next, CancellationToken cancellationToken)
        {
            observe(context);
            return next(cancellationToken);
        }
    }

    private sealed class MutableComparisonModel
    {
        public string? Ignored { get; set; }
        public int[]? Values { get; set; }
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static readonly EmptyServiceProvider Instance = new EmptyServiceProvider();

        public object? GetService(Type serviceType) => null;
    }
}
