namespace ParityBench.NET.Domain.ContractProfiles;

public sealed record ContractProfileSelection
{
    public const string SameContractProfileId = "same-contract";

    public ContractProfileSelection(
        string profileId,
        string? profileVersion = null,
        IReadOnlyDictionary<string, string>? options = null)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            throw new ArgumentException("Contract profile id must not be empty.", nameof(profileId));
        }

        ProfileId = profileId.Trim();
        ProfileVersion = string.IsNullOrWhiteSpace(profileVersion) ? null : profileVersion.Trim();
        Options = new Dictionary<string, string>(
            options ?? new Dictionary<string, string>(),
            StringComparer.Ordinal);
    }

    public string ProfileId { get; }

    public string? ProfileVersion { get; }

    public IReadOnlyDictionary<string, string> Options { get; }
}
