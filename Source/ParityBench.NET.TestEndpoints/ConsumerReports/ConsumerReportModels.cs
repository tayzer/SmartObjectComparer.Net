namespace ParityBench.NET.TestEndpoints.ConsumerReports;

public sealed record ConsumerReportRequest(
    string ReportRequestId,
    string GivenName,
    string FamilyName,
    string ConsentReference,
    string CorrelationId)
{
    public static ConsumerReportRequest FromJson(ConsumerReportJsonRequest? request)
    {
        if (request is null)
        {
            throw new InvalidOperationException("Consumer report JSON request body was empty.");
        }

        return new ConsumerReportRequest(
            request.ReportRequestId,
            request.Consumer?.GivenName ?? string.Empty,
            request.Consumer?.FamilyName ?? string.Empty,
            request.Consent?.ConsentReference ?? string.Empty,
            request.CorrelationId);
    }
}

public sealed class ConsumerReportJsonRequest
{
    public string ReportRequestId { get; set; } = string.Empty;

    public string CorrelationId { get; set; } = string.Empty;

    public ConsumerReportJsonConsumer? Consumer { get; set; }

    public ConsumerReportJsonConsent? Consent { get; set; }
}

public sealed class ConsumerReportJsonConsumer
{
    public string GivenName { get; set; } = string.Empty;

    public string FamilyName { get; set; } = string.Empty;
}

public sealed class ConsumerReportJsonConsent
{
    public string ConsentReference { get; set; } = string.Empty;
}

public sealed class ConsumerReportResponse
{
    public string ReportRequestId { get; set; } = string.Empty;

    public string ReportId { get; set; } = string.Empty;

    public string GeneratedAt { get; set; } = string.Empty;

    public string ProviderTraceId { get; set; } = string.Empty;

    public int ProcessingMilliseconds { get; set; }

    public ConsumerReportSubject Subject { get; set; } = new ConsumerReportSubject();

    public ConsumerReportScore Score { get; set; } = new ConsumerReportScore();

    public string RiskBand { get; set; } = string.Empty;

    public List<ConsumerReportAccount> Accounts { get; set; } = new List<ConsumerReportAccount>();

    public List<ConsumerReportPublicRecord>? PublicRecords { get; set; } = new List<ConsumerReportPublicRecord>();

    public List<ConsumerReportEnquiry> Enquiries { get; set; } = new List<ConsumerReportEnquiry>();

    public ConsumerReportMetadata Metadata { get; set; } = new ConsumerReportMetadata();
}

public sealed class ConsumerReportSubject
{
    public string GivenName { get; set; } = string.Empty;

    public string FamilyName { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string DateOfBirth { get; set; } = string.Empty;

    public string NationalIdentifier { get; set; } = string.Empty;

    public ConsumerReportAddress CurrentAddress { get; set; } = new ConsumerReportAddress();
}

public sealed class ConsumerReportAddress
{
    public string Line1 { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;

    public string Postcode { get; set; } = string.Empty;

    public string CountryCode { get; set; } = string.Empty;
}

public sealed class ConsumerReportScore
{
    public int Value { get; set; }

    public string Band { get; set; } = string.Empty;

    public decimal ProbabilityOfDefault { get; set; }
}

public sealed class ConsumerReportAccount
{
    public string AccountId { get; set; } = string.Empty;

    public string ProductType { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public decimal Balance { get; set; }

    public int MonthsInArrears { get; set; }
}

public sealed class ConsumerReportPublicRecord
{
    public string RecordId { get; set; } = string.Empty;

    public string RecordType { get; set; } = string.Empty;
}

public sealed class ConsumerReportEnquiry
{
    public string EnquiryId { get; set; } = string.Empty;

    public string Organisation { get; set; } = string.Empty;

    public string Date { get; set; } = string.Empty;

    public string Purpose { get; set; } = string.Empty;
}

public sealed class ConsumerReportMetadata
{
    public string SourceSystem { get; set; } = string.Empty;

    public string BureauRegion { get; set; } = string.Empty;

    public string ReportVersion { get; set; } = string.Empty;
}
