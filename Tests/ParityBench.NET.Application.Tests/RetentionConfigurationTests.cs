using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Application.Runs.Retention;
using ParityBench.NET.Domain.Runs.Retention;

namespace ParityBench.NET.Application.Tests;

[TestClass]
public sealed class RetentionConfigurationTests
{
    [TestMethod]
    public void AddRetentionConfiguration_WhenSectionMissing_UsesNorthStarDefaults()
    {
        IConfiguration configuration = new ConfigurationBuilder().Build();
        ServiceCollection services = new ServiceCollection();

        services.AddRetentionConfiguration(configuration);

        using ServiceProvider provider = services.BuildServiceProvider();
        RetentionConfiguration options = provider.GetRequiredService<IOptions<RetentionConfiguration>>().Value;

        Assert.AreEqual(RetentionMode.TrimmedEqualsAndIgnoredPaths, options.Mode);
        Assert.AreEqual(NonSuccessRetentionOverride.KeepBounded, options.NonSuccessOverride);
        Assert.AreEqual(14, options.NonSuccessDiagnosticRetentionWindowDays);
        Assert.AreEqual(5368709120, options.NonSuccessDiagnosticRetentionMaxBytesPerRun);
        Assert.AreEqual(53687091200, options.NonSuccessDiagnosticRetentionMaxBytesWorkspace);
    }

    [TestMethod]
    public void AddRetentionConfiguration_WhenSectionProvided_BindsConfiguredValues()
    {
        Dictionary<string, string?> values = new Dictionary<string, string?>
        {
            ["ParityBench:Retention:Mode"] = "TrimmedIgnoredPaths",
            ["ParityBench:Retention:NonSuccessOverride"] = "TrimAll",
            ["ParityBench:Retention:NonSuccessDiagnosticRetentionWindowDays"] = "21",
            ["ParityBench:Retention:NonSuccessDiagnosticRetentionMaxBytesPerRun"] = "1234",
            ["ParityBench:Retention:NonSuccessDiagnosticRetentionMaxBytesWorkspace"] = "4321",
        };
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        ServiceCollection services = new ServiceCollection();

        services.AddRetentionConfiguration(configuration);

        using ServiceProvider provider = services.BuildServiceProvider();
        RetentionConfiguration options = provider.GetRequiredService<IOptions<RetentionConfiguration>>().Value;

        Assert.AreEqual(RetentionMode.TrimmedIgnoredPaths, options.Mode);
        Assert.AreEqual(NonSuccessRetentionOverride.TrimAll, options.NonSuccessOverride);
        Assert.AreEqual(21, options.NonSuccessDiagnosticRetentionWindowDays);
        Assert.AreEqual(1234, options.NonSuccessDiagnosticRetentionMaxBytesPerRun);
        Assert.AreEqual(4321, options.NonSuccessDiagnosticRetentionMaxBytesWorkspace);
    }

    [TestMethod]
    public void AddRetentionConfiguration_WhenModeValueIsInvalid_Throws()
    {
        Dictionary<string, string?> values = new Dictionary<string, string?>
        {
            ["ParityBench:Retention:Mode"] = "Nope",
        };
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        ServiceCollection services = new ServiceCollection();

        InvalidOperationException exception = AssertThrows<InvalidOperationException>(() =>
        {
            services.AddRetentionConfiguration(configuration);
            return 0;
        });

        StringAssert.Contains(exception.Message, "ParityBench:Retention:Mode");
    }

    [TestMethod]
    public void AddRetentionConfiguration_WhenWindowDaysIsNonPositive_ThrowsOptionsValidationException()
    {
        Dictionary<string, string?> values = new Dictionary<string, string?>
        {
            ["ParityBench:Retention:NonSuccessDiagnosticRetentionWindowDays"] = "0",
        };
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        ServiceCollection services = new ServiceCollection();

        services.AddRetentionConfiguration(configuration);
        using ServiceProvider provider = services.BuildServiceProvider();

        _ = AssertThrows<OptionsValidationException>(() =>
        {
            _ = provider.GetRequiredService<IOptions<RetentionConfiguration>>().Value;
            return 0;
        });
    }

    private static TException AssertThrows<TException>(Func<int> action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException exception)
        {
            return exception;
        }

        Assert.Fail($"Expected exception of type {typeof(TException).Name} was not thrown.");
        throw new InvalidOperationException("Unreachable.");
    }
}
