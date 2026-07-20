namespace ParityBench.NET.TestEndpoints.SampleCustomerLookup;

public static class SampleCustomerLookupFixtures
{
    public static SampleCustomerLookupSoapResponse CreateSoapResponse(
        EndpointVariant variant,
        SampleCustomerLookupRequest request)
    {
        CustomerLookupScenario scenario = ResolveScenario(request.CustomerId, variant);
        return new SampleCustomerLookupSoapResponse
        {
            StatusCode = scenario.StatusCode,
            CustomerName = scenario.CustomerName,
            SensitiveToken = scenario.SensitiveToken ?? request.SensitiveToken,
        };
    }

    public static SampleCustomerLookupJsonResponse CreateJsonResponse(
        EndpointVariant variant,
        SampleCustomerLookupJsonRequest request)
    {
        CustomerLookupScenario scenario = ResolveScenario(request.LookupId, variant);
        return new SampleCustomerLookupJsonResponse
        {
            StatusCode = scenario.StatusCode,
            CustomerName = scenario.CustomerName,
            Payload = new SampleCustomerLookupPayload
            {
                RawToken = scenario.SensitiveToken ?? request.RawToken,
            },
        };
    }

    private static CustomerLookupScenario ResolveScenario(
        string lookupId,
        EndpointVariant variant)
    {
        string normalized = string.IsNullOrWhiteSpace(lookupId) ? "1001" : lookupId.Trim();
        return normalized switch
        {
            "1002" when variant == EndpointVariant.B => new CustomerLookupScenario("OK", "Blair Chen Updated", null),
            "1002" => new CustomerLookupScenario("OK", "Blair Chen", null),
            "1003" when variant == EndpointVariant.B => new CustomerLookupScenario("OK", "Casey Patel", "json-credential-9999"),
            "1003" => new CustomerLookupScenario("OK", "Casey Patel", "soap-credential-9999"),
            _ => new CustomerLookupScenario("OK", "Avery Stone", null),
        };
    }

    private sealed record CustomerLookupScenario(
        string StatusCode,
        string CustomerName,
        string? SensitiveToken);
}
