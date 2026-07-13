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

internal sealed class PreparedRequest : IAsyncDisposable
{
    private readonly IAsyncDisposable? owner;

    public PreparedRequest(
        Stream body,
        string contentType,
        IReadOnlyDictionary<string, string> headers,
        IAsyncDisposable? owner = null)
    {
        Body = body;
        ContentType = contentType;
        Headers = headers;
        this.owner = owner;
    }

    public Stream Body { get; }

    public string ContentType { get; }

    public IReadOnlyDictionary<string, string> Headers { get; }

    public async ValueTask DisposeAsync()
    {
        await Body.DisposeAsync().ConfigureAwait(false);

        if (owner is not null)
        {
            await owner.DisposeAsync().ConfigureAwait(false);
        }
    }
}
