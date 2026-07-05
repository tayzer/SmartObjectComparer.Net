using ParityBench.NET.Domain.AlternateContracts;
using ParityBench.NET.Domain.Requests;

namespace ParityBench.NET.Application.AlternateContracts;

public sealed record AlternateContractRequestPreparationContext(
    RequestItem Request,
    byte[] SourceRequestBody,
    PayloadFormat SourceFormat);

public sealed record AlternateContractRequestPreparationContext<TCanonicalRequest>(
    RequestItem Request,
    byte[] SourceRequestBody,
    PayloadFormat SourceFormat,
    TCanonicalRequest CanonicalRequest)
    where TCanonicalRequest : class;
