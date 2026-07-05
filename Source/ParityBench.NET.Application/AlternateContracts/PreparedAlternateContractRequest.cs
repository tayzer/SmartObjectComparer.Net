using ParityBench.NET.Domain.AlternateContracts;

namespace ParityBench.NET.Application.AlternateContracts;

public sealed record PreparedAlternateContractRequest(
    byte[] Body,
    string ContentType,
    PayloadFormat Format,
    string ProfileId,
    IReadOnlyDictionary<string, string>? Headers = null);
