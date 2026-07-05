using System.Text;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Application.AlternateContracts;
using ParityBench.NET.Domain.AlternateContracts;

namespace ParityBench.NET.Application.Tests;

[TestClass]
public sealed class ContractPayloadTests
{
    [TestMethod]
    public async Task OpenReadAsync_WhenContractPayloadIsDisposed_ThrowsObjectDisposedException()
    {
        ContractPayload payload = ContractPayload.FromBytes(
            Encoding.UTF8.GetBytes("{}"),
            PayloadFormat.Json,
            "application/json");
        await payload.DisposeAsync();

        await AssertThrowsAsync<ObjectDisposedException>(() => payload.OpenReadAsync().AsTask());
    }

    [TestMethod]
    public async Task OpenReadAsync_WhenPayloadIsCreatedFromBytes_ReturnsReadableStream()
    {
        ContractPayload payload = ContractPayload.FromBytes(
            Encoding.UTF8.GetBytes("{\"ok\":true}"),
            PayloadFormat.Json,
            "application/json");

        await using (payload)
        await using (Stream stream = await payload.OpenReadAsync())
        using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
        {
            Assert.AreEqual("{\"ok\":true}", await reader.ReadToEndAsync());
        }
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
}
