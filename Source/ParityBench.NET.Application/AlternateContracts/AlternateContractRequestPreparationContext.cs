using ParityBench.NET.Domain.AlternateContracts;
using ParityBench.NET.Domain.Requests;

namespace ParityBench.NET.Application.AlternateContracts;

public sealed record AlternateContractRequestPreparationContext(
    RequestItem Request,
    Func<CancellationToken, ValueTask<Stream>> OpenSourceRequestBodyAsync,
    PayloadFormat SourceFormat);

public sealed record AlternateContractRequestPreparationContext<TCanonicalRequest>(
    RequestItem Request,
    Func<CancellationToken, ValueTask<Stream>> OpenSourceRequestBodyAsync,
    PayloadFormat SourceFormat,
    TCanonicalRequest CanonicalRequest)
    where TCanonicalRequest : class;
