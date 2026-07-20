using ParityBench.NET.Domain.ContractProfiles;

namespace ParityBench.NET.Application.ContractProfiles;

public sealed record PreparedContractRequest(
    ContractPayload Body,
    string ProfileId,
    IReadOnlyDictionary<string, string>? Headers = null)
{
    public string ContentType => Body.ContentType;

    public PayloadFormat Format => Body.Format;
}
