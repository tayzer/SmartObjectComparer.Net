using ParityBench.NET.Domain.AlternateContracts;

namespace ParityBench.NET.Application.AlternateContracts;

public sealed record NormalizedAlternateContractResponse(
    byte[] Body,
    PayloadFormat Format,
    string ContentType,
    string ProfileId);
