using ParityBench.NET.Domain.AlternateContracts;

namespace ParityBench.NET.Application.AlternateContracts;

public sealed record PreparedAlternateContractRequest(
    ContractPayload Body,
    string ProfileId,
    IReadOnlyDictionary<string, string>? Headers = null)
{
    public string ContentType => Body.ContentType;

    public PayloadFormat Format => Body.Format;
}