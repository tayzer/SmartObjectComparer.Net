using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using ParityBench.NET.Application.ContractProfiles;
using ParityBench.NET.Application.Observability;
using ParityBench.NET.Application.Requests;
using ParityBench.NET.Application.Runs;
using ParityBench.NET.Domain.ContractProfiles;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Domain.Runs.Retention;
using ParityBench.NET.Engine.Comparers;
using ParityBench.NET.Engine.Pipeline;

namespace ParityBench.NET.Engine;

internal sealed class RunExecutionCounters
{
    private long responseBytesWritten;

    public long ResponseBytesWritten => Interlocked.Read(ref responseBytesWritten);

    public void AddResponseBytes(long bytesWritten) =>
        Interlocked.Add(ref responseBytesWritten, bytesWritten);
}
