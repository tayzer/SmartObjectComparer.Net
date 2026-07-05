using ParityBench.NET.Domain.ContractProfiles;
using ParityBench.NET.Domain.Requests;

namespace ParityBench.NET.Application.ContractProfiles;

public sealed record ContractRequestPreparationContext(
    RequestItem Request,
    Func<CancellationToken, ValueTask<Stream>> OpenSourceRequestBodyAsync,
    PayloadFormat SourceFormat,
    string SourceContentType);

public sealed record ContractRequestPreparationContext<TSourceRequest>(
    RequestItem Request,
    Func<CancellationToken, ValueTask<Stream>> OpenSourceRequestBodyAsync,
    PayloadFormat SourceFormat,
    string SourceContentType,
    TSourceRequest SourceRequest)
    where TSourceRequest : class;
