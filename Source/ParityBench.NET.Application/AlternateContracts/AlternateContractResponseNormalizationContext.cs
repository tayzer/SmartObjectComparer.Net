using ParityBench.NET.Domain.AlternateContracts;
using ParityBench.NET.Domain.Requests;

namespace ParityBench.NET.Application.AlternateContracts;

public sealed record AlternateContractResponseNormalizationContext(
    RequestItem Request,
    EndpointSlot Endpoint,
    byte[] SourceResponseBody,
    string? ContentType,
    PayloadFormat SourceFormat);
