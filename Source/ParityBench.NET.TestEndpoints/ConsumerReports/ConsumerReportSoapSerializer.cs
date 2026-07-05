using System.Xml.Linq;

namespace ParityBench.NET.TestEndpoints.ConsumerReports;

public static class ConsumerReportSoapSerializer
{
    private static readonly XNamespace Soap = "http://schemas.xmlsoap.org/soap/envelope/";
    private static readonly XNamespace ConsumerReport = "urn:paritybench:consumer-report";

    public static ConsumerReportRequest ReadRequest(string xml)
    {
        XDocument document = XDocument.Parse(xml);
        XElement request = document
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "ConsumerReportRequest")
            ?? throw new InvalidOperationException("SOAP body did not contain ConsumerReportRequest.");

        return new ConsumerReportRequest(
            ReadValue(request, "ReportRequestId"),
            ReadValue(request, "GivenName"),
            ReadValue(request, "FamilyName"),
            ReadValue(request, "ConsentReference"),
            ReadValue(document.Root, "CorrelationId"));
    }

    public static string WriteResponse(ConsumerReportResponse response)
    {
        XDocument document = new XDocument(
            new XElement(
                Soap + "Envelope",
                new XAttribute(XNamespace.Xmlns + "soap", Soap),
                new XAttribute(XNamespace.Xmlns + "cr", ConsumerReport),
                new XElement(
                    Soap + "Body",
                    new XElement(
                        ConsumerReport + "ConsumerReportResponse",
                        new XElement(ConsumerReport + "ReportRequestId", response.ReportRequestId),
                        new XElement(ConsumerReport + "ReportId", response.ReportId),
                        new XElement(ConsumerReport + "GeneratedAt", response.GeneratedAt),
                        new XElement(ConsumerReport + "ProviderTraceId", response.ProviderTraceId),
                        new XElement(ConsumerReport + "ProcessingMilliseconds", response.ProcessingMilliseconds),
                        WriteSubject(response.Subject),
                        WriteScore(response.Score),
                        new XElement(ConsumerReport + "RiskBand", response.RiskBand),
                        WriteAccounts(response.Accounts),
                        WritePublicRecords(response.PublicRecords),
                        WriteEnquiries(response.Enquiries),
                        WriteMetadata(response.Metadata)))));

        return document.ToString(SaveOptions.DisableFormatting);
    }

    private static string ReadValue(XElement? root, string localName) =>
        root?
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == localName)
            ?.Value
            ?.Trim() ?? string.Empty;

    private static XElement WriteSubject(ConsumerReportSubject subject) =>
        new XElement(
            ConsumerReport + "Subject",
            new XElement(ConsumerReport + "GivenName", subject.GivenName),
            new XElement(ConsumerReport + "FamilyName", subject.FamilyName),
            new XElement(ConsumerReport + "FullName", subject.FullName),
            new XElement(ConsumerReport + "DateOfBirth", subject.DateOfBirth),
            new XElement(ConsumerReport + "NationalIdentifier", subject.NationalIdentifier),
            new XElement(
                ConsumerReport + "CurrentAddress",
                new XElement(ConsumerReport + "Line1", subject.CurrentAddress.Line1),
                new XElement(ConsumerReport + "City", subject.CurrentAddress.City),
                new XElement(ConsumerReport + "Postcode", subject.CurrentAddress.Postcode),
                new XElement(ConsumerReport + "CountryCode", subject.CurrentAddress.CountryCode)));

    private static XElement WriteScore(ConsumerReportScore score) =>
        new XElement(
            ConsumerReport + "Score",
            new XElement(ConsumerReport + "Value", score.Value),
            new XElement(ConsumerReport + "Band", score.Band),
            new XElement(ConsumerReport + "ProbabilityOfDefault", score.ProbabilityOfDefault));

    private static XElement WriteAccounts(IReadOnlyList<ConsumerReportAccount> accounts) =>
        new XElement(
            ConsumerReport + "Accounts",
            accounts.Select(account =>
                new XElement(
                    ConsumerReport + "Account",
                    new XElement(ConsumerReport + "AccountId", account.AccountId),
                    new XElement(ConsumerReport + "ProductType", account.ProductType),
                    new XElement(ConsumerReport + "Status", account.Status),
                    new XElement(ConsumerReport + "Balance", account.Balance),
                    new XElement(ConsumerReport + "MonthsInArrears", account.MonthsInArrears))));

    private static XElement? WritePublicRecords(IReadOnlyList<ConsumerReportPublicRecord>? records) =>
        records is null
            ? null
            : new XElement(
                ConsumerReport + "PublicRecords",
                records.Select(record =>
                    new XElement(
                        ConsumerReport + "PublicRecord",
                        new XElement(ConsumerReport + "RecordId", record.RecordId),
                        new XElement(ConsumerReport + "RecordType", record.RecordType))));

    private static XElement WriteEnquiries(IReadOnlyList<ConsumerReportEnquiry> enquiries) =>
        new XElement(
            ConsumerReport + "Enquiries",
            enquiries.Select(enquiry =>
                new XElement(
                    ConsumerReport + "Enquiry",
                    new XElement(ConsumerReport + "EnquiryId", enquiry.EnquiryId),
                    new XElement(ConsumerReport + "Organisation", enquiry.Organisation),
                    new XElement(ConsumerReport + "Date", enquiry.Date),
                    new XElement(ConsumerReport + "Purpose", enquiry.Purpose))));

    private static XElement WriteMetadata(ConsumerReportMetadata metadata) =>
        new XElement(
            ConsumerReport + "Metadata",
            new XElement(ConsumerReport + "SourceSystem", metadata.SourceSystem),
            new XElement(ConsumerReport + "BureauRegion", metadata.BureauRegion),
            new XElement(ConsumerReport + "ReportVersion", metadata.ReportVersion));
}
