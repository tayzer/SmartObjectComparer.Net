namespace ParityBench.NET.Domain.AlternateContracts;

public sealed record AlternateContractOptions
{
    public AlternateContractOptions(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            throw new ArgumentException("Alternate contract profile id must not be empty.", nameof(profileId));
        }

        ProfileId = profileId.Trim();
    }

    public string ProfileId { get; }
}
