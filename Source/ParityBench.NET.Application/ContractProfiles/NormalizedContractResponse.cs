using ParityBench.NET.Domain.ContractProfiles;

namespace ParityBench.NET.Application.ContractProfiles;

public sealed record NormalizedContractResponse(
    ContractPayload Body,
    string ProfileId)
{
    public string ContentType => Body.ContentType;

    public PayloadFormat Format => Body.Format;
}
