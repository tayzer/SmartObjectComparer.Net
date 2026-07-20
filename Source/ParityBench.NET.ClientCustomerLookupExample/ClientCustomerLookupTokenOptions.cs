namespace ParityBench.NET.ClientCustomerLookupExample;

public sealed class ClientCustomerLookupTokenOptions
{
    public string PrimaryTokenUrl { get; init; } = string.Empty;

    public string PrimaryTokenSubscriptionKey { get; init; } = string.Empty;

    public string FinalTokenUrl { get; init; } = string.Empty;

    public string FinalTokenSubscriptionKey { get; init; } = string.Empty;
}
