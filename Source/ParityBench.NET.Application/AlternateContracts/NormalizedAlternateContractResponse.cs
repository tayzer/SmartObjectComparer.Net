using ParityBench.NET.Domain.AlternateContracts;

namespace ParityBench.NET.Application.AlternateContracts;

public sealed record NormalizedAlternateContractResponse(
    ContractPayload Body,
    string ProfileId)
{
    public string ContentType => Body.ContentType;

    public PayloadFormat Format => Body.Format;
}