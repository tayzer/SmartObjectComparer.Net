using ParityBench.NET.Domain.ContractProfiles;
using ParityBench.NET.Domain.Requests;

namespace ParityBench.NET.Application.ContractProfiles;

public sealed record ContractResponseNormalizationContext(
    RequestItem Request,
    EndpointSlot Endpoint,
    Func<CancellationToken, ValueTask<Stream>> OpenSourceResponseBodyAsync,
    string? ContentType,
    PayloadFormat SourceFormat);
