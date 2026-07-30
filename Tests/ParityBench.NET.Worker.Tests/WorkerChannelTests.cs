using System.IO.Pipes;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Application.Runs.Worker;

namespace ParityBench.NET.Worker.Tests;

[TestClass]
public sealed class WorkerChannelTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [TestMethod]
    public async Task Channel_WhenFramesAreWrittenBothWays_TheOtherSideReadsThemInOrder()
    {
        (WorkerChannel server, WorkerChannel client) = await ConnectAsync();
        await using (server)
        await using (client)
        {
            await server.WriteFrameAsync("{\"first\":1}").WaitAsync(Timeout);
            await server.WriteFrameAsync("{\"second\":2}").WaitAsync(Timeout);

            Assert.AreEqual("{\"first\":1}", await client.ReadFrameAsync().WaitAsync(Timeout));
            Assert.AreEqual("{\"second\":2}", await client.ReadFrameAsync().WaitAsync(Timeout));

            // The channel is duplex: the client can answer on the same pipe.
            await client.WriteFrameAsync("{\"ack\":true}").WaitAsync(Timeout);
            Assert.AreEqual("{\"ack\":true}", await server.ReadFrameAsync().WaitAsync(Timeout));
        }
    }

    [TestMethod]
    public async Task ReadFrameAsync_WhenPeerCloses_ReturnsNull()
    {
        (WorkerChannel server, WorkerChannel client) = await ConnectAsync();
        await using (server)
        {
            // Closing the peer surfaces as end-of-stream, which the host reads as
            // "the worker died without a terminal frame".
            await client.DisposeAsync();
            Assert.IsNull(await server.ReadFrameAsync().WaitAsync(Timeout));
        }
    }

    // Each channel owns its pipe, mirroring how the host and worker construct them,
    // so disposing the channel is the only thing that closes the pipe.
    private static async Task<(WorkerChannel Server, WorkerChannel Client)> ConnectAsync()
    {
        string pipeName = $"paritybench-test-{Guid.NewGuid():n}";
        NamedPipeServerStream serverPipe = WorkerChannel.CreateServerPipe(pipeName);
        NamedPipeClientStream clientPipe = WorkerChannel.CreateClientPipe(pipeName);

        Task connect = serverPipe.WaitForConnectionAsync();
        await clientPipe.ConnectAsync(5_000);
        await connect.WaitAsync(Timeout);

        return (new WorkerChannel(serverPipe), new WorkerChannel(clientPipe));
    }
}
