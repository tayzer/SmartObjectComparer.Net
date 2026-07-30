using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.ContractProfiles;
using ParityBench.NET.Application.ContractProfiles;

using ParityBench.PluginSdk.Comparisons;
using ParityBench.PluginSdk.Configuration;
using ParityBench.PluginSdk.Pipeline;
using ParityBench.PluginSdk.Plugin;
using ParityBench.PluginSdk.Profiles;

namespace ParityBench.TestPlugin;

public sealed class TestComparison
{
    public string Status { get; init; } = string.Empty;
}

public sealed class TestPlugin : IParityBenchPlugin
{
    public const string Id = "parity.test-plugin";

    public string PluginId => Id;

    public void Configure(IPluginBuilder builder)
    {
        builder
            .AddComparison(new TestComparisonDefinition())
            .AddMiddleware(new TestRequestStep())
            .AddConfigurationSchema(new PluginConfigurationSchema(
                TestRequestStep.Id,
                "Test step",
                new[]
                {
                    new PluginConfigurationField("apiKey", "API key", PluginFieldKind.Secret, isRequired: true),
                }))
            .AddEnvironment(new PluginEnvironment(
                "QA",
                new Uri("https://qa.example.test/a"),
                new Uri("https://qa.example.test/b")))
            .AddProfileTemplate(new PluginProfileTemplate(
                "test-template",
                "Test template",
                TestComparisonDefinition.Id,
                environmentName: "QA"));
    }
}

public sealed class TestComparisonDefinition : IComparisonDefinition<TestComparison>
{
    public const string Id = "parity.test-plugin.comparison";

    public string ComparisonId => Id;

    public string DisplayName => "Test comparison";

    public Type ComparisonType => typeof(TestComparison);

    public ContractEndpointProfile EndpointA { get; } =
        new ContractEndpointProfile(PayloadFormat.Json, "application/json", PayloadFormat.Json);

    public ContractEndpointProfile EndpointB { get; } =
        new ContractEndpointProfile(PayloadFormat.Json, "application/json", PayloadFormat.Json);

    public IReadOnlyList<string> DefaultStepIds { get; } = new[] { TestRequestStep.Id };

    public IReadOnlyList<string> RequiredStepIds { get; } = new[] { TestRequestStep.Id };

    public ComparisonRuleDefaults DefaultComparisonRules { get; } = new ComparisonRuleDefaults(ignoreCollectionOrder: true);
}

public sealed class TestRequestStep : IEndpointComparisonMiddleware
{
    public const string Id = "parity.test-plugin.request";

    public string StepId => Id;

    public PipelinePhase Phase => PipelinePhase.Request;

    public int Order => 0;

    public ValueTask InvokeAsync(IEndpointPipelineContext context, PipelineDelegate next, CancellationToken cancellationToken)
    {
        context.RequestHeaders["X-Test-Plugin"] = context.Configuration.ForStep(Id).GetString("apiKey", "unset")!;
        return next(cancellationToken);
    }
}
