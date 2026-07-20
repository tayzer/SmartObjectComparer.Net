namespace ParityBench.NET.TestEndpoints.ConsumerReports;

public static class ConsumerReportFixtures
{
    public static ConsumerReportResponse CreateResponse(
        EndpointVariant variant,
        ConsumerReportRequest request)
    {
        string scenarioId = string.IsNullOrWhiteSpace(request.ReportRequestId)
            ? "CR-1001"
            : request.ReportRequestId.Trim().ToUpperInvariant();

        ConsumerReportResponse response = CreateBaseResponse(variant, scenarioId, request);
        return scenarioId switch
        {
            "CR-1002" => WithFormattingAndOrderingDifferences(response, variant),
            "CR-1003" => WithNullEmptyCollectionDifference(response, variant),
            "CR-1004" => WithMaskableIdentifierDifference(response, variant),
            "CR-2001" => WithRealRiskDifference(response, variant),
            "CR-2002" => WithNestedContactPreferenceDifferences(response, variant),
            _ => response,
        };
    }

    private static ConsumerReportResponse CreateBaseResponse(
        EndpointVariant variant,
        string scenarioId,
        ConsumerReportRequest request) =>
        new ConsumerReportResponse
        {
            ReportRequestId = scenarioId,
            ReportId = $"PB-{variant}-{scenarioId}-20260705",
            GeneratedAt = variant == EndpointVariant.A ? "2026-07-05T10:15:00Z" : "2026-07-05T10:16:04Z",
            ProviderTraceId = $"trace-{variant.ToString().ToLowerInvariant()}-{scenarioId}",
            ProcessingMilliseconds = variant == EndpointVariant.A ? 124 : 177,
            Subject = new ConsumerReportSubject
            {
                GivenName = string.IsNullOrWhiteSpace(request.GivenName) ? "Alex" : request.GivenName,
                FamilyName = string.IsNullOrWhiteSpace(request.FamilyName) ? "Morgan" : request.FamilyName,
                FullName = $"{(string.IsNullOrWhiteSpace(request.GivenName) ? "Alex" : request.GivenName)} {(string.IsNullOrWhiteSpace(request.FamilyName) ? "Morgan" : request.FamilyName)}",
                DateOfBirth = "1984-03-12",
                NationalIdentifier = "NINO-AB123456C",
                CurrentAddress = new ConsumerReportAddress
                {
                    Line1 = "42 Market Street",
                    City = "Bristol",
                    Postcode = "BS1 8PB",
                    CountryCode = "GB",
                },
                ContactProfile = new ConsumerReportContactProfile
                {
                    PrimaryChannel = new ConsumerReportContactChannel
                    {
                        EmailAddress = "alex.morgan@example.test",
                        MobileNumber = "+447700900123",
                    },
                    NotificationPreference = new ConsumerReportNotificationPreference
                    {
                        StatementDelivery = "Postal",
                        MarketingConsent = "Accepted",
                    },
                },
            },
            Score = new ConsumerReportScore
            {
                Value = 742,
                Band = "Low",
                ProbabilityOfDefault = 0.014m,
            },
            RiskBand = "Low",
            Accounts = new List<ConsumerReportAccount>
            {
                new ConsumerReportAccount
                {
                    AccountId = "ACC-001",
                    ProductType = "CurrentAccount",
                    Status = "Open",
                    Balance = 1280.55m,
                    MonthsInArrears = 0,
                },
                new ConsumerReportAccount
                {
                    AccountId = "ACC-002",
                    ProductType = "Mortgage",
                    Status = "Open",
                    Balance = 188420.12m,
                    MonthsInArrears = 0,
                },
            },
            PublicRecords = new List<ConsumerReportPublicRecord>(),
            Enquiries = new List<ConsumerReportEnquiry>
            {
                new ConsumerReportEnquiry
                {
                    EnquiryId = "ENQ-100",
                    Organisation = "Northwind Lending",
                    Date = "2026-06-15",
                    Purpose = "Affordability",
                },
            },
            Metadata = new ConsumerReportMetadata
            {
                SourceSystem = variant == EndpointVariant.A ? "legacy-soap bureau" : "modern-json bureau",
                BureauRegion = "UK",
                ReportVersion = "2026.07",
            },
        };

    private static ConsumerReportResponse WithFormattingAndOrderingDifferences(
        ConsumerReportResponse response,
        EndpointVariant variant)
    {
        if (variant == EndpointVariant.B)
        {
            response.Subject.FullName = response.Subject.FullName.ToLowerInvariant() + "   ";
            response.RiskBand = response.RiskBand.ToLowerInvariant();
            response.Accounts.Reverse();
        }

        return response;
    }

    private static ConsumerReportResponse WithNullEmptyCollectionDifference(
        ConsumerReportResponse response,
        EndpointVariant variant)
    {
        response.PublicRecords = variant == EndpointVariant.A
            ? null
            : new List<ConsumerReportPublicRecord>();
        return response;
    }

    private static ConsumerReportResponse WithMaskableIdentifierDifference(
        ConsumerReportResponse response,
        EndpointVariant variant)
    {
        response.Subject.NationalIdentifier = variant == EndpointVariant.A
            ? "NINO-AB123456C"
            : "NINO-ZZ923456C";
        return response;
    }

    private static ConsumerReportResponse WithRealRiskDifference(
        ConsumerReportResponse response,
        EndpointVariant variant)
    {
        if (variant == EndpointVariant.B)
        {
            response.Score.Value = 688;
            response.Score.Band = "Medium";
            response.Score.ProbabilityOfDefault = 0.071m;
            response.RiskBand = "Medium";
            response.Accounts[0].MonthsInArrears = 2;
        }

        return response;
    }

    private static ConsumerReportResponse WithNestedContactPreferenceDifferences(
        ConsumerReportResponse response,
        EndpointVariant variant)
    {
        if (variant == EndpointVariant.B)
        {
            response.Subject.ContactProfile.NotificationPreference.StatementDelivery = "Email";
            response.Subject.ContactProfile.NotificationPreference.MarketingConsent = "Declined";
        }

        return response;
    }
}
