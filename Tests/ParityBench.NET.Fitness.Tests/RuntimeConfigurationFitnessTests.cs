using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Application.Workflow;
using ParityBench.NET.Cli;

namespace ParityBench.NET.Fitness.Tests;

[TestClass]
[TestCategory("Fitness")]
public sealed class RuntimeConfigurationFitnessTests
{
    [TestMethod]
    public void CliServices_WhenRunDefaultsAreConfigured_BindRequestComparisonDefaults()
    {
        string workspaceRoot = CreateTempDirectory();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RequestComparison:Defaults:MaxConcurrency"] = "35",
                ["RequestComparison:Defaults:TimeoutSeconds"] = "45",
                ["RequestComparison:EndpointOptions:Endpoints:0:Id"] = "client/customer-lookup/soap",
                ["RequestComparison:EndpointOptions:Endpoints:0:Name"] = "Client Customer Lookup SOAP",
                ["RequestComparison:EndpointOptions:Endpoints:0:Url"] = "http://localhost:5056/client/customer-lookup/soap",
                ["RequestComparison:EndpointOptions:Endpoints:1:Id"] = "client/customer-lookup/json",
                ["RequestComparison:EndpointOptions:Endpoints:1:Name"] = "Client Customer Lookup JSON",
                ["RequestComparison:EndpointOptions:Endpoints:1:Url"] = "http://localhost:5056/client/customer-lookup/json",
            })
            .Build();
        ServiceCollection services = new ServiceCollection();

        try
        {
            CliApplication.RegisterServices(services, workspaceRoot, configuration);
            using ServiceProvider provider = services.BuildServiceProvider();

            RequestComparisonRunDefaults defaults = provider.GetRequiredService<IOptions<RequestComparisonRunDefaults>>().Value;

            Assert.AreEqual(35, defaults.MaxConcurrency);
            Assert.AreEqual(45, defaults.TimeoutSeconds);
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    [TestMethod]
    public async Task DefaultsService_WhenRunDefaultsAreConfigured_ExposesThemToWorkflow()
    {
        RequestComparisonDefaultsService service = new RequestComparisonDefaultsService(
            new EmptyResponseModelRegistry(),
            new EmptyContractProfileRegistry(),
            new InMemoryRequestComparisonEndpointRegistry(),
            new InMemoryRequestComparisonPresetRegistry(),
            Options.Create(new RequestComparisonRunDefaults(maxConcurrency: 24, timeoutSeconds: 60)));

        RequestComparisonDefaults defaults = await service.LoadDefaultsAsync().ConfigureAwait(false);

        Assert.AreEqual(24, defaults.RunDefaults.MaxConcurrency);
        Assert.AreEqual(60, defaults.RunDefaults.TimeoutSeconds);
    }

    private static string CreateTempDirectory()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), "ParityBenchFitnessTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        return tempDirectory;
    }
}
