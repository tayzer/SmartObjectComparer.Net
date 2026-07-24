using System.IO.Pipes;
using System.Text;

namespace ParityBench.NET.Application.Runs.Worker;

/// <summary>
/// Reads and writes newline-delimited JSON frames over a duplex stream (the named
/// pipe). Newline framing keeps the reader simple and lets either side send a
/// single frame without a length prefix.
/// </summary>
/// <remarks>
/// Frames are written as raw UTF-8 bytes rather than through a
/// <see cref="StreamWriter"/> on purpose: a StreamWriter flush calls the pipe's
/// <c>FlushFileBuffers</c>, which blocks until the peer has drained the buffer, and
/// that deadlocks whenever a side sends more than one frame before the other reads.
/// A direct <see cref="Stream.WriteAsync(ReadOnlyMemory{byte}, CancellationToken)"/>
/// on a byte-mode pipe delivers immediately without that barrier.
/// </remarks>
public sealed class WorkerChannel : IAsyncDisposable
{
    private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly Stream stream;
    private readonly StreamReader reader;
    private readonly SemaphoreSlim writeGate = new SemaphoreSlim(1, 1);

    public WorkerChannel(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        this.stream = stream;
        reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
    }

    public async Task WriteFrameAsync(string json, CancellationToken cancellationToken = default)
    {
        // The serializer produces single-line JSON; the trailing newline is the
        // frame boundary.
        byte[] payload = Utf8NoBom.GetBytes(json + "\n");

        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            writeGate.Release();
        }
    }

    public Task<string?> ReadFrameAsync(CancellationToken cancellationToken = default) =>
        reader.ReadLineAsync(cancellationToken).AsTask();

    // The buffer-less constructor overload defaults the pipe buffer to zero, which
    // makes every write block until the peer reads; a real buffer lets a side send
    // progress frames without stalling on the reader.
    private const int PipeBufferSize = 64 * 1024;

    public static NamedPipeServerStream CreateServerPipe(string pipeName) =>
        new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: PipeBufferSize,
            outBufferSize: PipeBufferSize);

    public static NamedPipeClientStream CreateClientPipe(string pipeName) =>
        new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

    public async ValueTask DisposeAsync()
    {
        reader.Dispose();
        await stream.DisposeAsync().ConfigureAwait(false);
        writeGate.Dispose();
    }
}
