using System.Text.Json;
using ComparisonTool.Core.Comparison.Configuration;
using ComparisonTool.Core.RequestComparison.Models;
using ComparisonTool.Core.RequestComparison.Services;
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
    /// Gets the format used for the normalized comparison payload persisted for downstream comparison.
    /// Defaults to XML for backward compatibility with the original alternate-contract flow.
    /// </summary>
    public SerializationFormat CanonicalResponseFormat { get; init; } = SerializationFormat.Xml;

    /// <summary>
    /// Gets the content type used for the normalized comparison payload persisted for downstream comparison.
    /// </summary>
    public string CanonicalResponseContentType { get; init; } = GetDefaultContentType(SerializationFormat.Xml);

    public string? SuggestEndpointA { get; set; }

    public string? SuggestEndpointB { get; set; }

    /// <summary>
    /// Gets profile-owned ignore rules applied ahead of runtime request-comparison ignore rules.
    /// These rules are expressed against the canonical comparison model used downstream.
    /// </summary>
    public IReadOnlyList<IgnoreRule> DefaultIgnoreRules { get; init; } = Array.Empty<IgnoreRule>();

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
    /// Gets the optional override used to prepare the outbound endpoint B request.
    /// This supports custom per-request processing such as token-service lookups.
    /// </summary>
    public Func<AlternateContractRequestPreparationContext, CancellationToken, ValueTask<PreparedAlternateContractRequest>>? PrepareAlternateRequestOverride { get; init; }

    /// <summary>
    /// Gets the optional override used to normalize the endpoint A response into the canonical comparison payload.
    /// </summary>
    public Func<AlternateContractEndpointAResponseNormalizationContext, CancellationToken, ValueTask<NormalizedAlternateContractResponse>>? NormalizeEndpointAResponseOverride { get; init; }

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

        if (string.IsNullOrWhiteSpace(CanonicalResponseContentType))
        {
            throw new InvalidOperationException($"Alternate contract profile '{ProfileId}' must specify a canonical comparison response content type.");
        }

        foreach (var ignoreRule in DefaultIgnoreRules)
        {
            if (ignoreRule is null)
            {
                throw new InvalidOperationException($"Alternate contract profile '{ProfileId}' cannot contain null default ignore rules.");
            }

            if (string.IsNullOrWhiteSpace(ignoreRule.PropertyPath))
            {
                throw new InvalidOperationException($"Alternate contract profile '{ProfileId}' contains a default ignore rule with an empty property path.");
            }
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
    private static readonly JsonSerializerOptions DefaultIgnoreRulesJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HashSet<SerializationFormat> supportedSourceRequestFormats = new() { SerializationFormat.Xml };
    private SerializationFormat alternateRequestFormat = SerializationFormat.Json;
    private string alternateRequestContentType = RequestComparisonAlternateContractProfile.GetDefaultContentType(SerializationFormat.Json);
    private SerializationFormat alternateResponseFormat = SerializationFormat.Json;
    private SerializationFormat canonicalResponseFormat = SerializationFormat.Xml;
    private string canonicalResponseContentType = RequestComparisonAlternateContractProfile.GetDefaultContentType(SerializationFormat.Xml);
    private string? suggestEndpointA;
    private string? suggestEndpointB;
    private readonly Dictionary<string, string> canonicalToAlternateResponseMaskPathMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IgnoreRule> defaultIgnoreRules = new();
    private Func<Stream, SerializationFormat, TCanonicalRequest>? deserializeCanonicalRequestOverride;
    private Func<TAlternateRequest, byte[]>? serializeAlternateRequestOverride;
    private Func<Stream, string?, TAlternateResponse>? deserializeAlternateResponseOverride;
    private Func<TCanonicalResponse, byte[]>? serializeCanonicalResponseOverride;
    private Func<AlternateContractRequestPreparationContext<TCanonicalRequest>, CancellationToken, ValueTask<PreparedAlternateContractRequest>>? prepareAlternateRequestOverride;
    private Func<AlternateContractEndpointAResponseNormalizationContext, CancellationToken, ValueTask<NormalizedAlternateContractResponse>>? normalizeEndpointAResponseOverride;

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

    public RequestComparisonAlternateContractProfileBuilder<TCanonicalRequest, TAlternateRequest, TCanonicalResponse, TAlternateResponse> SuggestEndpointA(string endpointA, string endpointB)
    {
        suggestEndpointA = endpointA;
        suggestEndpointB = endpointB;
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
    /// Configures the format and content type used for the normalized comparison payload.
    /// </summary>
    public RequestComparisonAlternateContractProfileBuilder<TCanonicalRequest, TAlternateRequest, TCanonicalResponse, TAlternateResponse> UseCanonicalResponseFormat(
        SerializationFormat format,
        string? contentType = null)
    {
        canonicalResponseFormat = format;
        canonicalResponseContentType = string.IsNullOrWhiteSpace(contentType)
            ? RequestComparisonAlternateContractProfile.GetDefaultContentType(format)
            : contentType;
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

    /// <summary>
    /// Supplies a custom endpoint B request preparation delegate.
    /// This can perform custom processing such as auth/token lookups before sending endpoint B.
    /// </summary>
    public RequestComparisonAlternateContractProfileBuilder<TCanonicalRequest, TAlternateRequest, TCanonicalResponse, TAlternateResponse> UseAlternateRequestPreparation(
        Func<AlternateContractRequestPreparationContext<TCanonicalRequest>, CancellationToken, ValueTask<PreparedAlternateContractRequest>> prepareRequest)
    {
        prepareAlternateRequestOverride = prepareRequest;
        return this;
    }

    /// <summary>
    /// Supplies a custom endpoint A response normalization delegate.
    /// This can map endpoint A's native response contract into the canonical comparison payload.
    /// </summary>
    public RequestComparisonAlternateContractProfileBuilder<TCanonicalRequest, TAlternateRequest, TCanonicalResponse, TAlternateResponse> UseEndpointAResponseNormalizer(
        Func<AlternateContractEndpointAResponseNormalizationContext, CancellationToken, ValueTask<NormalizedAlternateContractResponse>> normalizer)
    {
        normalizeEndpointAResponseOverride = normalizer;
        return this;
    }

    /// <summary>
    /// Adds a profile-owned default ignore rule expressed against the canonical comparison model.
    /// </summary>
    public RequestComparisonAlternateContractProfileBuilder<TCanonicalRequest, TAlternateRequest, TCanonicalResponse, TAlternateResponse> AddDefaultIgnoreRule(IgnoreRule ignoreRule)
    {
        ArgumentNullException.ThrowIfNull(ignoreRule);
        defaultIgnoreRules.Add(ignoreRule);
        return this;
    }

    /// <summary>
    /// Adds multiple profile-owned default ignore rules expressed against the canonical comparison model.
    /// </summary>
    public RequestComparisonAlternateContractProfileBuilder<TCanonicalRequest, TAlternateRequest, TCanonicalResponse, TAlternateResponse> AddDefaultIgnoreRules(IEnumerable<IgnoreRule> ignoreRules)
    {
        ArgumentNullException.ThrowIfNull(ignoreRules);
        defaultIgnoreRules.AddRange(ignoreRules);
        return this;
    }

    /// <summary>
    /// Adds profile-owned default ignore rules from JSON. Supports a rule array, a single rule object, or an exported configuration bundle.
    /// </summary>
    public RequestComparisonAlternateContractProfileBuilder<TCanonicalRequest, TAlternateRequest, TCanonicalResponse, TAlternateResponse> AddDefaultIgnoreRulesFromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("Ignore rules JSON cannot be empty.", nameof(json));
        }

        defaultIgnoreRules.AddRange(ParseDefaultIgnoreRules(json));
        return this;
    }

    /// <summary>
    /// Adds profile-owned default ignore rules from a JSON file. Supports a rule array, a single rule object, or an exported configuration bundle.
    /// </summary>
    public RequestComparisonAlternateContractProfileBuilder<TCanonicalRequest, TAlternateRequest, TCanonicalResponse, TAlternateResponse> AddDefaultIgnoreRulesFromFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Ignore rules file path cannot be empty.", nameof(filePath));
        }

        return AddDefaultIgnoreRulesFromJson(File.ReadAllText(filePath));
    }

    private static List<IgnoreRule> ParseDefaultIgnoreRules(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        return root.ValueKind switch
        {
            JsonValueKind.Array => DeserializeIgnoreRules(root),
            JsonValueKind.Object => DeserializeIgnoreRulesObject(root),
            _ => throw new JsonException("Ignore rules JSON must be an array, rule object, or configuration bundle.")
        };
    }

    private static List<IgnoreRule> DeserializeIgnoreRulesObject(JsonElement root)
    {
        if (TryGetProperty(root, "ignoreRules", out var ignoreRulesElement))
        {
            if (ignoreRulesElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return new List<IgnoreRule>();
            }

            if (ignoreRulesElement.ValueKind != JsonValueKind.Array)
            {
                throw new JsonException("The ignoreRules property must be an array.");
            }

            return DeserializeIgnoreRules(ignoreRulesElement);
        }

        if (TryGetProperty(root, "propertyPath", out _))
        {
            var rule = JsonSerializer.Deserialize<IgnoreRule>(root.GetRawText(), DefaultIgnoreRulesJsonOptions);
            return rule == null ? new List<IgnoreRule>() : new List<IgnoreRule> { rule };
        }

        if (TryGetProperty(root, "globalSettings", out _))
        {
            return new List<IgnoreRule>();
        }

        throw new JsonException("Ignore rules object must contain ignoreRules or propertyPath.");
    }

    private static List<IgnoreRule> DeserializeIgnoreRules(JsonElement element)
    {
        return JsonSerializer.Deserialize<List<IgnoreRule>>(element.GetRawText(), DefaultIgnoreRulesJsonOptions)
            ?? new List<IgnoreRule>();
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement property)
    {
        foreach (var candidate in element.EnumerateObject())
        {
            if (string.Equals(candidate.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                property = candidate.Value;
                return true;
            }
        }

        property = default;
        return false;
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
            CanonicalResponseFormat = canonicalResponseFormat,
            CanonicalResponseContentType = canonicalResponseContentType,
            SuggestEndpointA = suggestEndpointA,
            SuggestEndpointB = suggestEndpointB,
            DefaultIgnoreRules = defaultIgnoreRules.ToArray(),
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
                : value => serializeCanonicalResponseOverride((TCanonicalResponse)value),
            PrepareAlternateRequestOverride = prepareAlternateRequestOverride == null
                ? null
                : (context, cancellationToken) => prepareAlternateRequestOverride(
                    new AlternateContractRequestPreparationContext<TCanonicalRequest>(
                        context.Job,
                        context.Request,
                        context.SourceRequestBody,
                        context.SourceFormat,
                        (TCanonicalRequest)context.CanonicalRequest,
                        context.Services),
                    cancellationToken),
            NormalizeEndpointAResponseOverride = normalizeEndpointAResponseOverride
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

/// <summary>
/// Provides the data needed to prepare an alternate endpoint B request.
/// </summary>
public sealed record AlternateContractRequestPreparationContext(
    RequestComparisonJob Job,
    RequestFileInfo Request,
    byte[] SourceRequestBody,
    SerializationFormat SourceFormat,
    object CanonicalRequest,
    IServiceProvider Services);

/// <summary>
/// Strongly typed request-preparation context for alternate endpoint B requests.
/// </summary>
public sealed record AlternateContractRequestPreparationContext<TCanonicalRequest>(
    RequestComparisonJob Job,
    RequestFileInfo Request,
    byte[] SourceRequestBody,
    SerializationFormat SourceFormat,
    TCanonicalRequest CanonicalRequest,
    IServiceProvider Services)
    where TCanonicalRequest : class;

/// <summary>
/// Provides the data needed to normalize an endpoint A response into the canonical comparison payload.
/// </summary>
public sealed record AlternateContractEndpointAResponseNormalizationContext(
    RequestComparisonJob Job,
    RequestExecutionResult ExecutionResult,
    IServiceProvider Services);
