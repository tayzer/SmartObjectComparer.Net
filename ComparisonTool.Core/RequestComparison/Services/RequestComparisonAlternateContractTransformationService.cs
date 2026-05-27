using System.Text;
using System.Text.Json;
using System.Xml;
using ComparisonTool.Core.RequestComparison.AlternateContracts;
using ComparisonTool.Core.RequestComparison.Models;
using ComparisonTool.Core.Serialization;
using ComparisonTool.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Core.RequestComparison.Services;

/// <summary>
/// Handles endpoint B request transformation and response normalization for alternate contracts.
/// </summary>
public sealed class RequestComparisonAlternateContractTransformationService
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
    };

    private readonly IRequestComparisonAlternateContractProfileRegistry profileRegistry;
    private readonly IXmlDeserializationService xmlDeserializationService;
    private readonly JsonDeserializationService jsonDeserializationService;
    private readonly XmlSerializerFactory xmlSerializerFactory;
    private readonly IServiceProvider services;
    private readonly ILogger<RequestComparisonAlternateContractTransformationService> logger;

    public RequestComparisonAlternateContractTransformationService(
        IRequestComparisonAlternateContractProfileRegistry profileRegistry,
        IXmlDeserializationService xmlDeserializationService,
        JsonDeserializationService jsonDeserializationService,
        XmlSerializerFactory xmlSerializerFactory,
        IServiceProvider services,
        ILogger<RequestComparisonAlternateContractTransformationService> logger)
    {
        this.profileRegistry = profileRegistry;
        this.xmlDeserializationService = xmlDeserializationService;
        this.jsonDeserializationService = jsonDeserializationService;
        this.xmlSerializerFactory = xmlSerializerFactory;
        this.services = services;
        this.logger = logger;
    }

    /// <summary>
    /// Resolves the configured alternate contract profile for the job.
    /// </summary>
    public RequestComparisonAlternateContractProfile ResolveProfile(RequestComparisonJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        return profileRegistry.Resolve(job.ModelName, job.AlternateContractProfileId);
    }

    /// <summary>
    /// Tries to resolve the configured alternate contract profile for the job.
    /// </summary>
    public bool TryResolveProfile(RequestComparisonJob job, out RequestComparisonAlternateContractProfile? profile, out string? errorMessage)
    {
        ArgumentNullException.ThrowIfNull(job);
        return profileRegistry.TryResolve(job.ModelName, job.AlternateContractProfileId, out profile, out errorMessage);
    }

    /// <summary>
    /// Translates canonical mask rules to the raw endpoint B property paths exposed by the alternate contract.
    /// </summary>
    public IReadOnlyList<MaskRuleDto> GetEndpointBRawResponseMaskRules(RequestComparisonJob job)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (!job.UseAlternateContractForEndpointB || job.MaskRules.Count == 0)
        {
            return job.MaskRules;
        }

        var profile = ResolveProfile(job);

        return job.MaskRules
            .Select(rule => rule with
            {
                PropertyPath = profile.TranslateCanonicalResponseMaskPath(rule.PropertyPath),
            })
            .ToArray();
    }

    /// <summary>
    /// Gets the effective ignore rules for downstream structured comparison.
    /// Profile-owned defaults are applied before runtime job rules.
    /// </summary>
    public IReadOnlyList<IgnoreRuleDto> GetEffectiveIgnoreRules(RequestComparisonJob job)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (!job.UseAlternateContractForEndpointB)
        {
            return job.IgnoreRules;
        }

        var profile = ResolveProfile(job);
        if (profile.DefaultIgnoreRules.Count == 0)
        {
            return job.IgnoreRules;
        }

        return profile.DefaultIgnoreRules
            .Concat(job.IgnoreRules)
            .ToArray();
    }

    /// <summary>
    /// Transforms the uploaded canonical request into the endpoint B request payload.
    /// </summary>
    public Task<PreparedAlternateContractRequest> PrepareEndpointBRequestAsync(
        RequestComparisonJob job,
        RequestFileInfo request,
        byte[] sourceRequestBody,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sourceRequestBody);

        var profile = ResolveProfile(job);
        var sourceFormat = request.DetectedFormat;
        if (!sourceFormat.HasValue)
        {
            throw new InvalidOperationException(
                $"Request '{request.RelativePath}' does not have a supported serialization format for alternate contract processing.");
        }

        if (!profile.SupportedSourceRequestFormats.Contains(sourceFormat.Value))
        {
            throw new InvalidOperationException(
                $"Alternate contract profile '{profile.ProfileId}' does not support source request format '{sourceFormat.Value}' for request '{request.RelativePath}'.");
        }

        using var stream = new MemoryStream(sourceRequestBody, writable: false);
        var canonicalRequest = DeserializeCanonicalRequest(profile, stream, sourceFormat.Value, request.RelativePath);

        if (profile.PrepareAlternateRequestOverride != null)
        {
            return profile.PrepareAlternateRequestOverride(
                new AlternateContractRequestPreparationContext(
                    job,
                    request,
                    sourceRequestBody,
                    sourceFormat.Value,
                    canonicalRequest,
                    services),
                cancellationToken).AsTask();
        }

        var alternateRequest = profile.MapCanonicalRequestToAlternate(canonicalRequest);
        var serializedRequest = SerializeAlternateRequest(profile, alternateRequest);

        logger.LogDebug(
            "Prepared alternate endpoint B request for {RelativePath} using profile {ProfileId}",
            request.RelativePath,
            profile.ProfileId);

        return Task.FromResult(new PreparedAlternateContractRequest(
            serializedRequest,
            profile.AlternateRequestContentType,
            profile.AlternateRequestFormat,
            profile.ProfileId));
    }

    /// <summary>
    /// Normalizes an endpoint B response into canonical XML for downstream comparison.
    /// </summary>
    public Task<NormalizedAlternateContractResponse> NormalizeEndpointBResponseAsync(
        RequestComparisonJob job,
        RequestExecutionResult executionResult,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(executionResult);

        if (string.IsNullOrWhiteSpace(executionResult.ResponsePathB) || !File.Exists(executionResult.ResponsePathB))
        {
            throw new InvalidOperationException($"Endpoint B response file for request '{executionResult.Request.RelativePath}' is not available.");
        }

        var profile = ResolveProfile(job);
        using var stream = File.OpenRead(executionResult.ResponsePathB);
        var alternateResponse = DeserializeAlternateResponse(profile, stream, executionResult.ContentTypeB, executionResult.Request.RelativePath);
        var canonicalResponse = profile.MapAlternateResponseToCanonical(alternateResponse);
        var canonicalBytes = SerializeCanonicalResponse(profile, canonicalResponse);

        logger.LogDebug(
            "Normalized alternate endpoint B response for {RelativePath} using profile {ProfileId}",
            executionResult.Request.RelativePath,
            profile.ProfileId);

        return Task.FromResult(new NormalizedAlternateContractResponse(
            canonicalBytes,
            profile.CanonicalResponseFormat,
            profile.CanonicalResponseContentType,
            profile.ProfileId));
    }

    /// <summary>
    /// Normalizes an endpoint A response into canonical XML for downstream comparison.
    /// </summary>
    public async Task<NormalizedAlternateContractResponse> NormalizeEndpointAResponseAsync(
        RequestComparisonJob job,
        RequestExecutionResult executionResult,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(executionResult);

        if (string.IsNullOrWhiteSpace(executionResult.ResponsePathA) || !File.Exists(executionResult.ResponsePathA))
        {
            throw new InvalidOperationException($"Endpoint A response file for request '{executionResult.Request.RelativePath}' is not available.");
        }

        var profile = ResolveProfile(job);
        if (profile.NormalizeEndpointAResponseOverride != null)
        {
            var normalized = await profile.NormalizeEndpointAResponseOverride(
                new AlternateContractEndpointAResponseNormalizationContext(job, executionResult, services),
                cancellationToken).ConfigureAwait(false);

            return string.IsNullOrWhiteSpace(normalized.ProfileId)
                ? normalized with { ProfileId = profile.ProfileId }
                : normalized;
        }

        var modelType = profile.CanonicalResponseType;
        await using var stream = File.OpenRead(executionResult.ResponsePathA);
        var deserializationResult = DeserializeWithDetectedFormat(stream, executionResult.ContentTypeA, executionResult.ResponsePathA, modelType);
        if (!deserializationResult.Success)
        {
            throw new InvalidOperationException(
                $"Failed to deserialize endpoint A response for '{executionResult.Request.RelativePath}': {deserializationResult.ErrorMessage}");
        }

        var canonicalBytes = SerializeCanonicalResponse(profile, deserializationResult.Value!);
        return new NormalizedAlternateContractResponse(
            canonicalBytes,
            profile.CanonicalResponseFormat,
            profile.CanonicalResponseContentType,
            profile.ProfileId);
    }

    private object DeserializeCanonicalRequest(
        RequestComparisonAlternateContractProfile profile,
        Stream stream,
        SerializationFormat sourceFormat,
        string relativePath)
    {
        var result = profile.DeserializeCanonicalRequestOverride != null
            ? profile.DeserializeCanonicalRequestOverride(stream, sourceFormat)
            : DeserializeByFormat(stream, profile.CanonicalRequestType, sourceFormat);

        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"Failed to deserialize canonical request for '{relativePath}': {result.ErrorMessage}");
        }

        return result.Value!;
    }

    private object DeserializeAlternateResponse(
        RequestComparisonAlternateContractProfile profile,
        Stream stream,
        string? contentType,
        string relativePath)
    {
        var result = profile.DeserializeAlternateResponseOverride != null
            ? profile.DeserializeAlternateResponseOverride(stream, contentType)
            : DeserializeByFormat(stream, profile.AlternateResponseType, profile.AlternateResponseFormat);

        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"Failed to deserialize alternate endpoint B response for '{relativePath}': {result.ErrorMessage}");
        }

        return result.Value!;
    }

    private byte[] SerializeAlternateRequest(RequestComparisonAlternateContractProfile profile, object alternateRequest)
    {
        if (profile.SerializeAlternateRequestOverride != null)
        {
            return profile.SerializeAlternateRequestOverride(alternateRequest);
        }

        return SerializeByFormat(alternateRequest, profile.AlternateRequestType, profile.AlternateRequestFormat);
    }

    private byte[] SerializeCanonicalResponse(RequestComparisonAlternateContractProfile profile, object canonicalResponse)
    {
        if (profile.SerializeCanonicalResponseOverride != null)
        {
            return profile.SerializeCanonicalResponseOverride(canonicalResponse);
        }

        return SerializeByFormat(canonicalResponse, profile.CanonicalResponseType, profile.CanonicalResponseFormat);
    }

    private byte[] SerializeCanonicalResponseObject(object canonicalResponse, Type canonicalType)
    {
        using var stream = new MemoryStream();
        using var xmlWriter = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            OmitXmlDeclaration = false,
            Indent = false,
            NewLineHandling = NewLineHandling.None,
        });

        var serializer = xmlSerializerFactory.GetSerializer(canonicalType, ignoreNamespaces: xmlDeserializationService.IgnoreXmlNamespaces);
        serializer.Serialize(xmlWriter, canonicalResponse);
        xmlWriter.Flush();
        return stream.ToArray();
    }

    private DeserializationResult DeserializeByFormat(Stream stream, Type targetType, SerializationFormat format)
    {
        stream.Position = 0;
        return format switch
        {
            SerializationFormat.Xml => xmlDeserializationService.TryDeserializeXml(stream, targetType),
            SerializationFormat.Json => jsonDeserializationService.TryDeserialize(stream, targetType, SerializationFormat.Json),
            _ => DeserializationResult.Failure(
                $"Serialization format '{format}' is not supported.",
                DeserializationFailureKind.UnsupportedFormat)
        };
    }

    private DeserializationResult DeserializeWithDetectedFormat(Stream stream, string? contentType, string filePath, Type targetType)
    {
        stream.Position = 0;
        var detectedFormat = FileTypeDetector.DetectFormatFromContent(stream, logger);
        if (!detectedFormat.HasValue)
        {
            detectedFormat = !string.IsNullOrWhiteSpace(contentType) && contentType.Contains("json", StringComparison.OrdinalIgnoreCase)
                ? SerializationFormat.Json
                : SerializationFormat.Xml;
        }

        stream.Position = 0;
        return DeserializeByFormat(stream, targetType, detectedFormat.Value);
    }

    private byte[] SerializeByFormat(object value, Type valueType, SerializationFormat format) =>
        format switch
        {
            SerializationFormat.Json => JsonSerializer.SerializeToUtf8Bytes(value, valueType, JsonSerializerOptions),
            SerializationFormat.Xml => SerializeCanonicalResponseObject(value, valueType),
            _ => throw new NotSupportedException($"Serialization format '{format}' is not supported.")
        };
}

/// <summary>
/// Represents a transformed endpoint B request payload.
/// </summary>
public sealed record PreparedAlternateContractRequest(
    byte[] Body,
    string ContentType,
    SerializationFormat Format,
    string ProfileId);

/// <summary>
/// Represents a normalized canonical response payload.
/// </summary>
public sealed record NormalizedAlternateContractResponse(
    byte[] Body,
    SerializationFormat Format,
    string ContentType,
    string? ProfileId);
