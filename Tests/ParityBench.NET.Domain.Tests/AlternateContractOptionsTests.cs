using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Domain.AlternateContracts;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Domain.Tests;

[TestClass]
public sealed class AlternateContractOptionsTests
{
    [TestMethod]
    public void Create_WhenProfileIdIsEmpty_ThrowsArgumentException()
    {
        AssertThrows<ArgumentException>(() => new AlternateContractOptions(" "));
    }

    [TestMethod]
    public void Create_WhenRunOptionsIncludeAlternateContract_StoresProfileSelection()
    {
        RunOptions options = new RunOptions(
            new RequestBatchReference("batch-1"),
            new EndpointDefinition(new Uri("https://service-a.example.test")),
            new EndpointDefinition(new Uri("https://service-b.example.test")),
            TimeSpan.FromSeconds(30),
            2,
            alternateContractOptions: new AlternateContractOptions("profile-a"));

        Assert.AreEqual("profile-a", options.AlternateContract?.ProfileId);
    }

    private static void AssertThrows<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
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
}
