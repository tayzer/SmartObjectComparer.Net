using ComparisonTool.Core.Serialization;
using ComparisonTool.Core.Utilities;

namespace ComparisonTool.Core.RequestComparison.AlternateContracts;

/// <summary>
/// Defines the mapping and serialization metadata needed to compare endpoint B against
/// a canonical response model while using a different request/response contract.
/// </summary>
public sealed class RequestComparisonAlternateContractProfile
{
    /// <summary>Gets the unique identifier for this profile.</summary>
    public required string ProfileId { get; init; }

    /// <summary>Gets the canonical comparison model name this profile targets.</summary>
    public required string CanonicalModelName { get; init; }

    /// <summary>Gets the canonical request type used to deserialize uploaded source requests.</summary>
    public required Type CanonicalRequestType { get; init; }

    /// <summary>Gets the alternate request type sent to endpoint B.</summary>
    public required Type AlternateRequestType { get; init; }

    /// <summary>Gets the canonical response type selected for comparison.</summary>
    public required Type CanonicalResponseType { get; init; }

    /// <summary>Gets the alternate response type returned by endpoint B.</summary>
    public required Type AlternateResponseType { get; init; }

    /// <summary>Gets the source request formats supported by this profile.</summary>
    public IReadOnlyCollection<SerializationFormat> SupportedSourceRequestFormats { get; init; } = new[] { SerializationFormat.Xml };

    /// <summary>Gets the format used when sending requests to endpoint B.</summary>
    public SerializationFormat AlternateRequestFormat { get; init; } = SerializationFormat.Json;

    /// <summary>Gets the content type used for endpoint B requests.</summary>
    public string AlternateRequestContentType { get; init; } = GetDefaultContentType(SerializationFormat.Json);

    /// <summary>Gets the expected format returned by endpoint B.</summary>
    public SerializationFormat AlternateResponseFormat { get; init; } = SerializationFormat.Json;

    /// <summary>
    /// Gets the canonical-to-alternate raw response property path translations used for endpoint B masking.
    /// </summary>
    public IReadOnlyDictionary<string, string> CanonicalToAlternateResponseMaskPathMap { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the mapper that converts the canonical request object to the alternate request object.</summary>
    public required Func<object, object> MapCanonicalRequestToAlternate { get; init; }

    /// <summary>Gets the mapper that converts the alternate response object to the canonical response object.</summary>
    public required Func<object, object> MapAlternateResponseToCanonical { get; init; }

    /// <summary>
    /// Gets the optional override used to deserialize the canonical request object from the uploaded request payload.
    /// </summary>
    public Func<Stream, SerializationFormat, DeserializationResult>? DeserializeCanonicalRequestOverride { get; init; }

    /// <summary>Gets the optional override used to serialize the alternate request body for endpoint B.</summary>
    public Func<object, byte[]>? SerializeAlternateRequestOverride { get; init; }

    /// <summary>
    /// Gets the optional override used to deserialize the alternate endpoint B response payload.
    /// </summary>
    public Func<Stream, string?, DeserializationResult>? DeserializeAlternateResponseOverride { get; init; }

    /// <summary>Gets the optional override used to serialize the canonical response payload for comparison.</summary>
    public Func<object, byte[]>? SerializeCanonicalResponseOverride { get; init; }

    /// <summary>
    /// Validates the profile configuration.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ProfileId))
        {
            throw new InvalidOperationException("Alternate contract profile id is required.");
        }

        if (string.IsNullOrWhiteSpace(CanonicalModelName))
        {
            throw new InvalidOperationException($"Alternate contract profile '{ProfileId}' must specify a canonical model name.");
        }

        if (SupportedSourceRequestFormats.Count == 0)
        {
            throw new InvalidOperationException($"Alternate contract profile '{ProfileId}' must support at least one source request format.");
        }

        if (MapCanonicalRequestToAlternate is null)
        {
            throw new InvalidOperationException($"Alternate contract profile '{ProfileId}' is missing a canonical request mapper.");
        }

        if (MapAlternateResponseToCanonical is null)
        {
            throw new InvalidOperationException($"Alternate contract profile '{ProfileId}' is missing an alternate response mapper.");
        }

        if (string.IsNullOrWhiteSpace(AlternateRequestContentType))
        {
            throw new InvalidOperationException($"Alternate contract profile '{ProfileId}' must specify an endpoint B request content type.");
        }

        foreach (var mapping in CanonicalToAlternateResponseMaskPathMap)
        {
            if (string.IsNullOrWhiteSpace(mapping.Key) || string.IsNullOrWhiteSpace(mapping.Value))
            {
                throw new InvalidOperationException(
                    $"Alternate contract profile '{ProfileId}' contains an invalid response mask path mapping.");
            }
        }
    }

    internal string TranslateCanonicalResponseMaskPath(string canonicalPropertyPath)
    {
        var normalizedCanonicalPath = PropertyPathNormalizer.NormalizePropertyPath(canonicalPropertyPath);

        if (CanonicalToAlternateResponseMaskPathMap.TryGetValue(normalizedCanonicalPath, out var translatedPropertyPath))
        {
            return PropertyPathNormalizer.NormalizePropertyPath(translatedPropertyPath);
        }

        foreach (var mapping in CanonicalToAlternateResponseMaskPathMap)
        {
            if (string.Equals(
                PropertyPathNormalizer.NormalizePropertyPath(mapping.Key),
                normalizedCanonicalPath,
                StringComparison.OrdinalIgnoreCase))
            {
                return PropertyPathNormalizer.NormalizePropertyPath(mapping.Value);
            }
        }

        return normalizedCanonicalPath;
    }

    internal static string GetDefaultContentType(SerializationFormat format) =>
        format switch
        {
            SerializationFormat.Json => "application/json",
            SerializationFormat.Xml => "application/xml",
            _ => "application/octet-stream"
        };
}

/// <summary>
/// Builder used to create strongly-typed alternate contract profiles while storing runtime delegates.
/// </summary>
public sealed class RequestComparisonAlternateContractProfileBuilder<TCanonicalRequest, TAlternateRequest, TCanonicalResponse, TAlternateResponse>
    where TCanonicalRequest : class
    where TAlternateRequest : class
    where TCanonicalResponse : class
    where TAlternateResponse : class
{
    private readonly HashSet<SerializationFormat> supportedSourceRequestFormats = new() { SerializationFormat.Xml };
    private SerializationFormat alternateRequestFormat = SerializationFormat.Json;
    private string alternateRequestContentType = RequestComparisonAlternateContractProfile.GetDefaultContentType(SerializationFormat.Json);
    private SerializationFormat alternateResponseFormat = SerializationFormat.Json;
    private readonly Dictionary<string, string> canonicalToAlternateResponseMaskPathMap = new(StringComparer.OrdinalIgnoreCase);
    private Func<Stream, SerializationFormat, TCanonicalRequest>? deserializeCanonicalRequestOverride;
    private Func<TAlternateRequest, byte[]>? serializeAlternateRequestOverride;
    private Func<Stream, string?, TAlternateResponse>? deserializeAlternateResponseOverride;
    private Func<TCanonicalResponse, byte[]>? serializeCanonicalResponseOverride;

    /// <summary>
    /// Replaces the set of supported source request formats.
    /// </summary>
    public RequestComparisonAlternateContractProfileBuilder<TCanonicalRequest, TAlternateRequest, TCanonicalResponse, TAlternateResponse> SupportSourceRequestFormats(params SerializationFormat[] formats)
    {
        supportedSourceRequestFormats.Clear();
        foreach (var format in formats.Distinct())
        {
            supportedSourceRequestFormats.Add(format);
        }

        return this;
    }

    /// <summary>
    /// Configures the format and content type used for endpoint B requests.
    /// </summary>
    public RequestComparisonAlternateContractProfileBuilder<TCanonicalRequest, TAlternateRequest, TCanonicalResponse, TAlternateResponse> UseAlternateRequestFormat(
        SerializationFormat format,
        string? contentType = null)
    {
        alternateRequestFormat = format;
        alternateRequestContentType = string.IsNullOrWhiteSpace(contentType)
            ? RequestComparisonAlternateContractProfile.GetDefaultContentType(format)
            : contentType;
        return this;
    }

    /// <summary>
    /// Configures the expected endpoint B response format.
    /// </summary>
    public RequestComparisonAlternateContractProfileBuilder<TCanonicalRequest, TAlternateRequest, TCanonicalResponse, TAlternateResponse> UseAlternateResponseFormat(SerializationFormat format)
    {
        alternateResponseFormat = format;
        return this;
    }

    /// <summary>
    /// Maps a canonical response property path to the raw endpoint B property path used for masking.
    /// </summary>
    public RequestComparisonAlternateContractProfileBuilder<TCanonicalRequest, TAlternateRequest, TCanonicalResponse, TAlternateResponse> MapCanonicalResponsePropertyPath(
        string canonicalPropertyPath,
        string alternatePropertyPath)
    {
        if (string.IsNullOrWhiteSpace(canonicalPropertyPath))
        {
            throw new ArgumentException("Canonical property path is required.", nameof(canonicalPropertyPath));
        }

        if (string.IsNullOrWhiteSpace(alternatePropertyPath))
        {
            throw new ArgumentException("Alternate property path is required.", nameof(alternatePropertyPath));
        }

        canonicalToAlternateResponseMaskPathMap[PropertyPathNormalizer.NormalizePropertyPath(canonicalPropertyPath)] =
            PropertyPathNormalizer.NormalizePropertyPath(alternatePropertyPath);

        return this;
    }

    /// <summary>
    /// Supplies a custom source-request deserializer.
    /// </summary>
    public RequestComparisonAlternateContractProfileBuilder<TCanonicalRequest, TAlternateRequest, TCanonicalResponse, TAlternateResponse> UseCanonicalRequestDeserializer(
        Func<Stream, SerializationFormat, TCanonicalRequest> deserializer)
    {
        deserializeCanonicalRequestOverride = deserializer;
        return this;
    }

    /// <summary>
    /// Supplies a custom endpoint B request serializer.
    /// </summary>
    public RequestComparisonAlternateContractProfileBuilder<TCanonicalRequest, TAlternateRequest, TCanonicalResponse, TAlternateResponse> UseAlternateRequestSerializer(
        Func<TAlternateRequest, byte[]> serializer,
        string? contentType = null)
    {
        serializeAlternateRequestOverride = serializer;
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            alternateRequestContentType = contentType;
        }

        return this;
    }

    /// <summary>
    /// Supplies a custom endpoint B response deserializer.
    /// </summary>
    public RequestComparisonAlternateContractProfileBuilder<TCanonicalRequest, TAlternateRequest, TCanonicalResponse, TAlternateResponse> UseAlternateResponseDeserializer(
        Func<Stream, string?, TAlternateResponse> deserializer)
    {
        deserializeAlternateResponseOverride = deserializer;
        return this;
    }

    /// <summary>
    /// Supplies a custom canonical response serializer.
    /// </summary>
    public RequestComparisonAlternateContractProfileBuilder<TCanonicalRequest, TAlternateRequest, TCanonicalResponse, TAlternateResponse> UseCanonicalResponseSerializer(
        Func<TCanonicalResponse, byte[]> serializer)
    {
        serializeCanonicalResponseOverride = serializer;
        return this;
    }

    internal RequestComparisonAlternateContractProfile Build(
        string canonicalModelName,
        string profileId,
        Func<TCanonicalRequest, TAlternateRequest> requestMapper,
        Func<TAlternateResponse, TCanonicalResponse> responseMapper)
    {
        var profile = new RequestComparisonAlternateContractProfile
        {
            ProfileId = profileId,
            CanonicalModelName = canonicalModelName,
            CanonicalRequestType = typeof(TCanonicalRequest),
            AlternateRequestType = typeof(TAlternateRequest),
            CanonicalResponseType = typeof(TCanonicalResponse),
            AlternateResponseType = typeof(TAlternateResponse),
            SupportedSourceRequestFormats = supportedSourceRequestFormats.ToArray(),
            AlternateRequestFormat = alternateRequestFormat,
            AlternateRequestContentType = alternateRequestContentType,
            AlternateResponseFormat = alternateResponseFormat,
            CanonicalToAlternateResponseMaskPathMap = new Dictionary<string, string>(canonicalToAlternateResponseMaskPathMap, StringComparer.OrdinalIgnoreCase),
            MapCanonicalRequestToAlternate = request => requestMapper((TCanonicalRequest)request),
            MapAlternateResponseToCanonical = response => responseMapper((TAlternateResponse)response),
            DeserializeCanonicalRequestOverride = deserializeCanonicalRequestOverride == null
                ? null
                : (stream, format) => ExecuteDeserializer(() => deserializeCanonicalRequestOverride(stream, format)),
            SerializeAlternateRequestOverride = serializeAlternateRequestOverride == null
                ? null
                : value => serializeAlternateRequestOverride((TAlternateRequest)value),
            DeserializeAlternateResponseOverride = deserializeAlternateResponseOverride == null
                ? null
                : (stream, contentType) => ExecuteDeserializer(() => deserializeAlternateResponseOverride(stream, contentType)),
            SerializeCanonicalResponseOverride = serializeCanonicalResponseOverride == null
                ? null
                : value => serializeCanonicalResponseOverride((TCanonicalResponse)value)
        };

        profile.Validate();
        return profile;
    }

    private static DeserializationResult ExecuteDeserializer(Func<object> deserializer)
    {
        try
        {
            var result = deserializer();
            return result == null
                ? DeserializationResult.Failure("Deserializer returned null.", DeserializationFailureKind.NullResult)
                : DeserializationResult.Ok(result);
        }
        catch (Exception ex)
        {
            return DeserializationResult.Failure(ex.Message, DeserializationFailureKind.DeserializationError);
        }
    }
}
