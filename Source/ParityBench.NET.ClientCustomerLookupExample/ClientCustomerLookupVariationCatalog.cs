namespace ParityBench.NET.ClientCustomerLookupExample;

public enum ClientCustomerLookupVariation
{
    ExactMatch,
    IgnoredFieldsOnly,
    AddressOrderOnly,
    TriggeredChecksOrderOnly,
    NameDiffOnly,
    CityDiffOnly,
    FraudResultDiffOnly,
    FlagsDiffOnly,
    CombinedDiff,
}

public static class ClientCustomerLookupVariationCatalog
{
    public const int CategoryCount = 9;

    public static ClientCustomerLookupVariation Resolve(string customerId) =>
        customerId switch
        {
            "2001" => ClientCustomerLookupVariation.ExactMatch,
            "2002" => ClientCustomerLookupVariation.CombinedDiff,
            _ when int.TryParse(customerId, out int id) => (ClientCustomerLookupVariation)(((id % CategoryCount) + CategoryCount) % CategoryCount),
            _ => ClientCustomerLookupVariation.ExactMatch,
        };

    public static string ToLabel(ClientCustomerLookupVariation variation) => variation switch
    {
        ClientCustomerLookupVariation.ExactMatch => "exact-match",
        ClientCustomerLookupVariation.IgnoredFieldsOnly => "ignored-fields-only",
        ClientCustomerLookupVariation.AddressOrderOnly => "address-order-only",
        ClientCustomerLookupVariation.TriggeredChecksOrderOnly => "triggered-checks-order-only",
        ClientCustomerLookupVariation.NameDiffOnly => "name-diff",
        ClientCustomerLookupVariation.CityDiffOnly => "city-diff",
        ClientCustomerLookupVariation.FraudResultDiffOnly => "fraud-result-diff",
        ClientCustomerLookupVariation.FlagsDiffOnly => "flags-diff",
        ClientCustomerLookupVariation.CombinedDiff => "combined-diff",
        _ => "unknown",
    };
}
