using Mapster;

namespace ParityBench.ClientCustomerLookupPlugin;

public static class ClientCustomerLookupMapsterConfig
{
    public static TypeAdapterConfig CreateConfig()
    {
        TypeAdapterConfig config = new TypeAdapterConfig();
        Register(config);
        return config;
    }

    public static void Register(TypeAdapterConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        config.NewConfig<ClientCustomerLookupSoapRequestEnvelope, ClientCustomerLookupJsonRequest>()
            .Map(destination => destination.CustomerId, source => source.Body.LookupRequest.CustomerId)
            .Map(destination => destination.CorrelationId, source => source.Body.LookupRequest.CorrelationId);

        config.NewConfig<ClientCustomerLookupSoapResponseEnvelope, ClientCustomerLookupResponse>()
            .Map(destination => destination.Details, source => new ClientCustomerLookupDetails
            {
                ResultCode = source.Body.LookupResponse.StatusCode,
                TraceId = source.Body.LookupResponse.TraceId,
                DecisionEngine = "EndpointA-SOAP-Normalizer",
            })
            .Map(destination => destination.Applicants, source => new[]
            {
                new ClientCustomerLookupApplicant
                {
                    ApplicantId = source.Body.LookupResponse.TraceId,
                    Profile = new ClientCustomerLookupApplicantProfile
                    {
                        FullName = source.Body.LookupResponse.CustomerName,
                        Addresses = new[]
                        {
                            new ClientCustomerLookupAddress
                            {
                                Type = "HOME",
                                City = "Seattle",
                                Country = "US",
                            },
                            new ClientCustomerLookupAddress
                            {
                                Type = "MAILING",
                                City = "Tacoma",
                                Country = "US",
                            },
                        },
                    },
                    RuleEvaluations = new[]
                    {
                        new ClientCustomerLookupRuleEvaluation
                        {
                            RuleSet = "identity",
                            Outcomes = new[]
                            {
                                new ClientCustomerLookupRuleOutcome
                                {
                                    Code = "ID_DOC_MATCH",
                                    Result = "PASS",
                                    TriggeredChecks = new[] { "name_match", "dob_match" },
                                },
                                new ClientCustomerLookupRuleOutcome
                                {
                                    Code = "SANCTIONS_SCREEN",
                                    Result = "PASS",
                                    TriggeredChecks = new[] { "ofac", "pep" },
                                },
                            },
                        },
                        new ClientCustomerLookupRuleEvaluation
                        {
                            RuleSet = "fraud",
                            Outcomes = new[]
                            {
                                new ClientCustomerLookupRuleOutcome
                                {
                                    Code = "DEVICE_RISK",
                                    Result = "REVIEW",
                                    TriggeredChecks = new[] { "ip_velocity", "device_reuse" },
                                },
                            },
                        },
                    },
                    Flags = new[] { "KYC_COMPLETE", "MANUAL_REVIEW_REQUIRED" },
                },
            });

        config.NewConfig<ClientCustomerLookupJsonResponse, ClientCustomerLookupResponse>();
    }
}
