using System.Xml.Linq;

namespace ParityBench.NET.TestEndpoints.SampleCustomerLookup;

public static class SampleCustomerLookupSoapSerializer
{
    private static readonly XNamespace Soap = "http://schemas.xmlsoap.org/soap/envelope/";

    public static SampleCustomerLookupRequest ReadRequest(string xml)
    {
        XDocument document = XDocument.Parse(xml);
        XElement request = document
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "CustomerLookupRequest")
            ?? throw new InvalidOperationException("SOAP body did not contain CustomerLookupRequest.");

        return new SampleCustomerLookupRequest
        {
            CustomerId = ReadValue(request, "CustomerId"),
            SensitiveToken = ReadValue(request, "SensitiveToken"),
        };
    }

    public static string WriteResponse(SampleCustomerLookupSoapResponse response)
    {
        XDocument document = new XDocument(
            new XElement(
                Soap + "Envelope",
                new XAttribute(XNamespace.Xmlns + "soap", Soap),
                new XElement(
                    Soap + "Body",
                    new XElement(
                        "CustomerLookupResponse",
                        new XElement("StatusCode", response.StatusCode),
                        new XElement("CustomerName", response.CustomerName),
                        new XElement("SensitiveToken", response.SensitiveToken)))));

        return document.ToString(SaveOptions.DisableFormatting);
    }

    private static string ReadValue(XElement root, string localName) =>
        root
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == localName)
            ?.Value
            ?.Trim() ?? string.Empty;
}
