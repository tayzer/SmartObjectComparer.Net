using System.Text.Json;
using System.Xml.Linq;
using System.Xml.Serialization;

using ParityBench.NET.Application.Requests;
using ParityBench.NET.Domain.Comparison;

namespace ParityBench.NET.Infrastructure;

public sealed class JsonXmlResponseBodyDeserializer : IResponseBodyDeserializer
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IResponseModelRegistry modelRegistry;

    public JsonXmlResponseBodyDeserializer(IResponseModelRegistry modelRegistry)
    {
        this.modelRegistry = modelRegistry;
    }

    public async Task<object> DeserializeAsync(
        string modelName,
        Stream body,
        string? contentType,
        ComparisonOptions comparisonOptions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(comparisonOptions);

        Type modelType = modelRegistry.Resolve(modelName);
        if (IsXml(contentType))
        {
            return await DeserializeXmlAsync(body, modelType, comparisonOptions, cancellationToken)
                .ConfigureAwait(false);
        }

        object? result = await JsonSerializer
            .DeserializeAsync(body, modelType, JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        return result ?? throw new InvalidOperationException($"Response body could not be deserialized as '{modelName}'.");
    }

    private static async Task<object> DeserializeXmlAsync(
        Stream body,
        Type modelType,
        ComparisonOptions comparisonOptions,
        CancellationToken cancellationToken)
    {
        using StreamReader reader = new StreamReader(body);
        string xml = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

        if (comparisonOptions.IgnoreXmlNamespaces)
        {
            XDocument document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
            xml = StripNamespaces(document).ToString(SaveOptions.DisableFormatting);
        }

        XmlSerializer serializer = new XmlSerializer(modelType);
        using StringReader stringReader = new StringReader(xml);
        object? result = serializer.Deserialize(stringReader);

        return result ?? throw new InvalidOperationException($"XML response body could not be deserialized as '{modelType.Name}'.");
    }

    private static XDocument StripNamespaces(XDocument document) =>
        new XDocument(document.Declaration, document.Root is null ? null : StripNamespaces(document.Root));

    private static XElement StripNamespaces(XElement element) =>
        new XElement(
            element.Name.LocalName,
            element.Attributes()
                .Where(attribute => !attribute.IsNamespaceDeclaration)
                .Select(attribute => new XAttribute(attribute.Name.LocalName, attribute.Value)),
            element.Nodes().Select(node => node is XElement child ? StripNamespaces(child) : node));

    private static bool IsXml(string? contentType) =>
        contentType?.Contains("xml", StringComparison.OrdinalIgnoreCase) == true;
}
