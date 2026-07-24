using Mapster;

using Microsoft.Extensions.DependencyInjection;

using ParityBench.NET.Application.ContractProfiles;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.ContractProfiles;

using ParityBench.PluginSdk.Comparisons;
using ParityBench.PluginSdk.Configuration;
using ParityBench.PluginSdk.Plugin;
using ParityBench.PluginSdk.Profiles;

namespace ParityBench.ClientCustomerLookupPlugin;

/// <summary>
/// Reference plugin: compares a SOAP endpoint (A) against a JSON endpoint (B) that
/// needs a chained bearer token, normalizing both onto one canonical response.
/// </summary>
public sealed class ClientCustomerLookupPlugin : IParityBenchPlugin
{
    public const string Id = "client.customer-lookup";
    public const string ComparisonId = "client.customer-lookup.soap-vs-json";

    public string PluginId => Id;

    public void Configure(IPluginBuilder builder)
    {
        // Mapster config and the token exchange are run-scoped services the two
        // middleware steps take by constructor injection. The token cache lives for
        // the run, so a volume batch performs one exchange per distinct credential.
        builder.Services.AddSingleton(_ => ClientCustomerLookupMapsterConfig.CreateConfig());
        builder.Services.AddSingleton(provider => new ClientCustomerLookupTokenExchange(
            provider.GetRequiredService<HttpClient>()));

        builder
            .AddComparison(new ClientCustomerLookupComparisonDefinition())
            .AddMiddleware<ClientCustomerLookupRequestMiddleware>()
            .AddMiddleware<ClientCustomerLookupMappingMiddleware>()
            .AddConfigurationSchema(new PluginConfigurationSchema(
                ClientCustomerLookupRequestMiddleware.Id,
                "Token exchange",
                new[]
                {
                    new PluginConfigurationField("primaryTokenUrl", "Primary token URL", PluginFieldKind.Uri, isRequired: true),
                    new PluginConfigurationField("primaryTokenSubscriptionKey", "Primary token subscription key", PluginFieldKind.Secret, isRequired: true),
                    new PluginConfigurationField("finalTokenUrl", "Final token URL", PluginFieldKind.Uri, isRequired: true),
                    new PluginConfigurationField("finalTokenSubscriptionKey", "Final token subscription key", PluginFieldKind.Secret, isRequired: true),
                    new PluginConfigurationField("endpointBSubscriptionKey", "Endpoint B subscription key", PluginFieldKind.Secret, isRequired: true),
                    new PluginConfigurationField("soapAction", "SOAPAction header", PluginFieldKind.String, defaultValue: "urn:ClientCustomerLookup"),
                }))
            .AddEnvironment(new PluginEnvironment(
                "Local",
                new Uri("http://localhost:5056/client/customer-lookup/soap"),
                new Uri("http://localhost:5056/client/customer-lookup/json")))
            .AddProfileTemplate(new PluginProfileTemplate(
                "client-customer-lookup-local",
                "Client Customer Lookup — Local",
                ComparisonId,
                environmentName: "Local"));
    }

    private sealed class ClientCustomerLookupComparisonDefinition : IComparisonDefinition<ClientCustomerLookupResponse>
    {
        // Known-noisy fields the endpoints legitimately differ on, so every run
        // using this comparison starts from a sane baseline.
        private static readonly ComparisonRuleDefaults Defaults = new ComparisonRuleDefaults(
            ignoreXmlNamespaces: true,
            ignoreRules: new[]
            {
                new IgnoreRuleDefinition("details.traceId"),
                new IgnoreRuleDefinition("details.decisionEngine"),
                new IgnoreRuleDefinition("apps.profile.addresses", ignoreCompletely: false, ignoreCollectionOrder: true),
                new IgnoreRuleDefinition("apps.ruleEvaluations.outcomes.triggeredChecks", ignoreCompletely: false, ignoreCollectionOrder: true),
            });

        public string ComparisonId => ClientCustomerLookupPlugin.ComparisonId;

        public string DisplayName => "Client Customer Lookup (SOAP vs JSON)";

        public Type ComparisonType => typeof(ClientCustomerLookupResponse);

        public ContractEndpointProfile EndpointA { get; } = new ContractEndpointProfile(
            PayloadFormat.Xml,
            "text/xml",
            PayloadFormat.Xml,
            supportedSourceRequestFormats: new[] { PayloadFormat.Xml });

        public ContractEndpointProfile EndpointB { get; } = new ContractEndpointProfile(
            PayloadFormat.Json,
            "application/json",
            PayloadFormat.Json);

        public IReadOnlyList<string> DefaultStepIds { get; } = new[]
        {
            ClientCustomerLookupRequestMiddleware.Id,
            ClientCustomerLookupMappingMiddleware.Id,
        };

        public IReadOnlyList<string> RequiredStepIds => DefaultStepIds;

        public ComparisonRuleDefaults DefaultComparisonRules => Defaults;
    }
}
