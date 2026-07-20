using System.Xml.Serialization;

namespace ParityBench.NET.Infrastructure;

[XmlRoot("Envelope")]
public sealed class ConsumerReportSoapResponseEnvelope
{
    public ConsumerReportSoapResponseBody Body { get; set; } = new ConsumerReportSoapResponseBody();
}

public sealed class ConsumerReportSoapResponseBody
{
    public ConsumerReportSoapResponse ConsumerReportResponse { get; set; } = new ConsumerReportSoapResponse();
}

public sealed class ConsumerReportSoapResponse
{
    public string ReportRequestId { get; set; } = string.Empty;

    public string ReportId { get; set; } = string.Empty;

    public string GeneratedAt { get; set; } = string.Empty;

    public string ProviderTraceId { get; set; } = string.Empty;

    public int ProcessingMilliseconds { get; set; }

    public ConsumerReportSubject Subject { get; set; } = new ConsumerReportSubject();

    public ConsumerReportScore Score { get; set; } = new ConsumerReportScore();

    public string RiskBand { get; set; } = string.Empty;

    public ConsumerReportAccountCollection Accounts { get; set; } = new ConsumerReportAccountCollection();

    public ConsumerReportPublicRecordCollection? PublicRecords { get; set; }

    public ConsumerReportEnquiryCollection Enquiries { get; set; } = new ConsumerReportEnquiryCollection();

    public ConsumerReportMetadata Metadata { get; set; } = new ConsumerReportMetadata();
}

public sealed class ConsumerReportJsonResponse
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

    public ConsumerReportContactProfile ContactProfile { get; set; } = new ConsumerReportContactProfile();
}

public sealed class ConsumerReportAddress
{
    public string Line1 { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;

    public string Postcode { get; set; } = string.Empty;

    public string CountryCode { get; set; } = string.Empty;
}

public sealed class ConsumerReportContactProfile
{
    public ConsumerReportContactChannel PrimaryChannel { get; set; } = new ConsumerReportContactChannel();

    public ConsumerReportNotificationPreference NotificationPreference { get; set; } = new ConsumerReportNotificationPreference();
}

public sealed class ConsumerReportContactChannel
{
    public string EmailAddress { get; set; } = string.Empty;

    public string MobileNumber { get; set; } = string.Empty;
}

public sealed class ConsumerReportNotificationPreference
{
    public string StatementDelivery { get; set; } = string.Empty;

    public string MarketingConsent { get; set; } = string.Empty;
}

public sealed class ConsumerReportScore
{
    public int Value { get; set; }

    public string Band { get; set; } = string.Empty;

    public decimal ProbabilityOfDefault { get; set; }
}

public sealed class ConsumerReportAccountCollection
{
    [XmlElement("Account")]
    public List<ConsumerReportAccount> Items { get; set; } = new List<ConsumerReportAccount>();
}

public sealed class ConsumerReportAccount
{
    public string AccountId { get; set; } = string.Empty;

    public string ProductType { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public decimal Balance { get; set; }

    public int MonthsInArrears { get; set; }
}

public sealed class ConsumerReportPublicRecordCollection
{
    [XmlElement("PublicRecord")]
    public List<ConsumerReportPublicRecord> Items { get; set; } = new List<ConsumerReportPublicRecord>();
}

public sealed class ConsumerReportPublicRecord
{
    public string RecordId { get; set; } = string.Empty;

    public string RecordType { get; set; } = string.Empty;
}

public sealed class ConsumerReportEnquiryCollection
{
    [XmlElement("Enquiry")]
    public List<ConsumerReportEnquiry> Items { get; set; } = new List<ConsumerReportEnquiry>();
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
