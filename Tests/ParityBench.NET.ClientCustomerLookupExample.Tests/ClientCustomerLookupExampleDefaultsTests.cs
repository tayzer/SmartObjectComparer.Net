using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Application.Workflow;
using ParityBench.NET.ClientCustomerLookupExample;

namespace ParityBench.NET.ClientCustomerLookupExample.Tests;

[TestClass]
public sealed class ClientCustomerLookupExampleDefaultsTests
{
    [TestMethod]
    public void Register_WhenConfigurationIsProvided_AddsEndpointsAndPresetWithHeaders()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RequestComparison:EndpointOptions:Endpoints:0:Id"] = ClientCustomerLookupProfileFactory.SuggestedEndpointAId,
                ["RequestComparison:EndpointOptions:Endpoints:0:Name"] = "Client SOAP",
                ["RequestComparison:EndpointOptions:Endpoints:0:Url"] = "https://fixture.example.test/client/customer-lookup/soap",
                ["RequestComparison:EndpointOptions:Endpoints:0:ContentType"] = "text/xml",
                ["RequestComparison:EndpointOptions:Endpoints:0:DefaultHeaders:SOAPAction"] = "urn:ClientCustomerLookup",
                ["RequestComparison:EndpointOptions:Endpoints:1:Id"] = ClientCustomerLookupProfileFactory.SuggestedEndpointBId,
                ["RequestComparison:EndpointOptions:Endpoints:1:Name"] = "Client JSON",
                ["RequestComparison:EndpointOptions:Endpoints:1:Url"] = "https://fixture.example.test/client/customer-lookup/json",
                ["RequestComparison:EndpointOptions:Endpoints:1:ContentType"] = "application/json",
                ["RequestComparison:EndpointOptions:Endpoints:1:DefaultHeaders:Ocp-Apim-Subscription-Key"] = "endpoint-b-key",
                ["ClientCustomerLookup:Profile:ResponseModel"] = ClientCustomerLookupProfileFactory.ResponseModelName,
                ["ClientCustomerLookup:Profile:ProfileId"] = ClientCustomerLookupProfileFactory.ProfileId,
            })
            .Build();
        InMemoryRequestComparisonEndpointRegistry endpointRegistry = new InMemoryRequestComparisonEndpointRegistry();
        InMemoryRequestComparisonPresetRegistry presetRegistry = new InMemoryRequestComparisonPresetRegistry();

        ClientCustomerLookupExampleDefaults.Register(
            endpointRegistry,
            presetRegistry,
            configuration,
            "Examples/ParityBench.NET.ManualRuns");

        IReadOnlyList<EndpointOption> endpoints = endpointRegistry.ListEndpoints();
        Assert.AreEqual(2, endpoints.Count);
        EndpointOption endpointA = endpoints.Single(endpoint => endpoint.EndpointId == ClientCustomerLookupProfileFactory.SuggestedEndpointAId);
        Assert.AreEqual("text/xml", endpointA.DefaultHeaders?["Content-Type"]);
        Assert.AreEqual("urn:ClientCustomerLookup", endpointA.DefaultHeaders?["SOAPAction"]);

        RequestComparisonPresetOption preset = presetRegistry.ListPresets().Single();
        Assert.AreEqual(ClientCustomerLookupProfileFactory.ResponseModelName, preset.ModelName);
        Assert.AreEqual(ClientCustomerLookupProfileFactory.ProfileId, preset.ContractProfileId);
        Assert.AreEqual("endpoint-b-key", preset.EndpointBHeaders?["Ocp-Apim-Subscription-Key"]);
    }

    [TestMethod]
    public void RegisterVolumePreset_WhenConfigurationIsProvided_AddsSecondPresetWithVolumeDirectory()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RequestComparison:EndpointOptions:Endpoints:0:Id"] = ClientCustomerLookupProfileFactory.SuggestedEndpointAId,
                ["RequestComparison:EndpointOptions:Endpoints:0:Name"] = "Client SOAP",
                ["RequestComparison:EndpointOptions:Endpoints:0:Url"] = "https://fixture.example.test/client/customer-lookup/soap",
                ["RequestComparison:EndpointOptions:Endpoints:0:ContentType"] = "text/xml",
                ["RequestComparison:EndpointOptions:Endpoints:1:Id"] = ClientCustomerLookupProfileFactory.SuggestedEndpointBId,
                ["RequestComparison:EndpointOptions:Endpoints:1:Name"] = "Client JSON",
                ["RequestComparison:EndpointOptions:Endpoints:1:Url"] = "https://fixture.example.test/client/customer-lookup/json",
                ["RequestComparison:EndpointOptions:Endpoints:1:ContentType"] = "application/json",
            })
            .Build();
        InMemoryRequestComparisonPresetRegistry presetRegistry = new InMemoryRequestComparisonPresetRegistry();

        ClientCustomerLookupExampleDefaults.RegisterVolumePreset(
            presetRegistry,
            configuration,
            "Examples/ParityBench.NET.ManualRuns");

        RequestComparisonPresetOption preset = presetRegistry.ListPresets().Single();
        Assert.AreEqual(ClientCustomerLookupExampleDefaults.VolumePresetId, preset.PresetId);
        Assert.AreEqual(
            Path.Combine("Examples/ParityBench.NET.ManualRuns", "client-soap-json-token", "volume"),
            preset.RequestDirectory);
    }
}
