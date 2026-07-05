using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;

using ParityBench.NET.Application.AlternateContracts;
using ParityBench.NET.Domain.AlternateContracts;

namespace ParityBench.NET.Infrastructure;

public sealed class JsonXmlContractPayloadSerializer : IContractPayloadSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
    };

    public async Task<object> DeserializeAsync(
        Type targetType,
        Stream body,
        PayloadFormat format,
        bool ignoreXmlNamespaces = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetType);
        ArgumentNullException.ThrowIfNull(body);

        return format switch
        {
            PayloadFormat.Json => await DeserializeJsonAsync(targetType, body, cancellationToken).ConfigureAwait(false),
            PayloadFormat.Xml => await DeserializeXmlAsync(targetType, body, ignoreXmlNamespaces, cancellationToken).ConfigureAwait(false),
            _ => throw new NotSupportedException($"Payload format '{format}' is not supported."),
        };
    }

    public Task<byte[]> SerializeAsync(
        object value,
        Type valueType,
        PayloadFormat format,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(valueType);

        byte[] bytes = format switch
        {
            PayloadFormat.Json => JsonSerializer.SerializeToUtf8Bytes(value, valueType, JsonOptions),
            PayloadFormat.Xml => SerializeXml(value, valueType),
            _ => throw new NotSupportedException($"Payload format '{format}' is not supported."),
        };

        return Task.FromResult(bytes);
    }

    private static async Task<object> DeserializeJsonAsync(
        Type targetType,
        Stream body,
        CancellationToken cancellationToken)
    {
        object? result = await JsonSerializer
            .DeserializeAsync(body, targetType, JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        return result ?? throw new InvalidOperationException($"JSON payload could not be deserialized as '{targetType.Name}'.");
    }

    private static async Task<object> DeserializeXmlAsync(
        Type targetType,
        Stream body,
        bool ignoreXmlNamespaces,
        CancellationToken cancellationToken)
    {
        using StreamReader reader = new StreamReader(body, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: false);
        string xml = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

        if (ignoreXmlNamespaces)
        {
            XDocument document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
            xml = StripNamespaces(document).ToString(SaveOptions.DisableFormatting);
        }

        XmlSerializer serializer = new XmlSerializer(targetType);
        using StringReader stringReader = new StringReader(xml);
        object? result = serializer.Deserialize(stringReader);

        return result ?? throw new InvalidOperationException($"XML payload could not be deserialized as '{targetType.Name}'.");
    }

    private static byte[] SerializeXml(object value, Type valueType)
    {
        using MemoryStream stream = new MemoryStream();
        using XmlWriter writer = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            OmitXmlDeclaration = false,
            Indent = false,
            NewLineHandling = NewLineHandling.None,
        });

        XmlSerializer serializer = new XmlSerializer(valueType);
        serializer.Serialize(writer, value);
        writer.Flush();
        return stream.ToArray();
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
}
