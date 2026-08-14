using System.Collections.Concurrent;
using System.Buffers;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

using ParityBench.NET.Application.Requests;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Engine.Comparers;

namespace ParityBench.NET.Engine;

internal static class FocusedRawContentBuilder
{
    internal static readonly TimeSpan MatchRegexTimeout = TimeSpan.FromSeconds(1);
    private static readonly UTF8Encoding Utf8WithoutBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

    // The ignore-rule set is fixed for the whole run, but TryPrune used to rebuild an
    // IgnorePathMatcher (and recompile every RegexOptions.Compiled pattern in it) on every
    // call - twice per request, once per endpoint. Regex.Compiled construction uses
    // Reflection.Emit; under dozens of concurrent compare workers doing that
    // simultaneously, dynamic codegen contends heavily and construction cost dominates
    // the whole compare phase. Build each distinct matcher once and reuse it.
    private static readonly ConcurrentDictionary<string, IgnorePathMatcher> MatcherCache =
        new ConcurrentDictionary<string, IgnorePathMatcher>(StringComparer.Ordinal);

    public static async Task<RequestPairResult> TryAttachFocusedRawContentAsync(
        RequestPairResult result,
        RunId runId,
        ComparisonOptions comparisonOptions,
        IRunArtifactStore artifactStore,
        DetailedCompareMetricsCollector? timing = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(comparisonOptions);
        ArgumentNullException.ThrowIfNull(artifactStore);

        IReadOnlyList<string> ignorePaths = BuildFocusedIgnorePaths(comparisonOptions);

        if (ignorePaths.Count == 0 || result.ResponseA is null || result.ResponseB is null)
        {
            return result;
        }

        Task<FocusedContent?> focusedATask = TryBuildFocusedContentAsync(result.ResponseA, ignorePaths, artifactStore, timing, cancellationToken);
        Task<FocusedContent?> focusedBTask = TryBuildFocusedContentAsync(result.ResponseB, ignorePaths, artifactStore, timing, cancellationToken);
        FocusedContent? focusedA = null;
        FocusedContent? focusedB = null;
        try
        {
            await Task.WhenAll(focusedATask, focusedBTask).ConfigureAwait(false);
            focusedA = await focusedATask.ConfigureAwait(false);
            focusedB = await focusedBTask.ConfigureAwait(false);

            if (focusedA is null || focusedB is null || (!focusedA.WasPruned && !focusedB.WasPruned))
            {
                return result;
            }

            focusedA = EnsureFormatted(focusedA);
            focusedB = EnsureFormatted(focusedB);

            Task<ResponseArtifactMetadata> responseATask = SaveFocusedResponseAsync(runId, result.RelativePath, result.ResponseA, focusedA.Content, artifactStore, cancellationToken);
            Task<ResponseArtifactMetadata> responseBTask = SaveFocusedResponseAsync(runId, result.RelativePath, result.ResponseB, focusedB.Content, artifactStore, cancellationToken);
            await Task.WhenAll(responseATask, responseBTask).ConfigureAwait(false);
            ResponseArtifactMetadata focusedResponseA = await responseATask.ConfigureAwait(false);
            ResponseArtifactMetadata focusedResponseB = await responseBTask.ConfigureAwait(false);

            return result.WithFocusedRawContent(focusedResponseA, focusedResponseB, ignorePaths.Select(ToDisplayIgnorePath));
        }
        finally
        {
            (focusedA ?? (focusedATask.Status == TaskStatus.RanToCompletion ? focusedATask.Result : null))?.Dispose();
            (focusedB ?? (focusedBTask.Status == TaskStatus.RanToCompletion ? focusedBTask.Result : null))?.Dispose();
        }
    }


    private static IReadOnlyList<string> BuildFocusedIgnorePaths(ComparisonOptions comparisonOptions)
    {
        IEnumerable<string> ignoreRulePaths = comparisonOptions.IgnoreRules
            .Where(rule => rule.IgnoreCompletely && !string.IsNullOrWhiteSpace(rule.PropertyPath))
            .Select(rule => rule.PropertyPath.Trim());

        IEnumerable<string> smartPropertyPaths = comparisonOptions.SmartIgnoreRules
            .Where(rule => rule.IsEnabled && rule.Kind == SmartIgnoreRuleKind.PropertyName && !string.IsNullOrWhiteSpace(rule.Value))
            .SelectMany(rule => new[] { rule.Value.Trim(), $"*.{rule.Value.Trim()}" });

        IEnumerable<string> smartNamePatterns = comparisonOptions.SmartIgnoreRules
            .Where(rule => rule.IsEnabled
                && rule.Kind == SmartIgnoreRuleKind.NamePattern
                && !string.IsNullOrWhiteSpace(rule.Value))
            .Select(rule => "regex:" + rule.Value.Trim());

        return ignoreRulePaths
            .Concat(smartPropertyPaths)
            .Concat(smartNamePatterns)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
    private static string ToDisplayIgnorePath(string ignorePath) =>
        ignorePath.StartsWith("regex:", StringComparison.OrdinalIgnoreCase) ? ignorePath["regex:".Length..] : ignorePath;

    private static async Task<FocusedContent?> TryBuildFocusedContentAsync(
        ResponseArtifactMetadata response,
        IReadOnlyCollection<string> ignorePaths,
        IRunArtifactStore artifactStore,
        DetailedCompareMetricsCollector? timing,
        CancellationToken cancellationToken)
    {
        Stream opened = await artifactStore.OpenReadAsync(response.Artifact, cancellationToken).ConfigureAwait(false);
        await using Stream stream = timing is null ? opened : new CountingReadStream(opened, timing.AddArtifactBytesRead);
        if (response.ContentLength is > 0 and <= 1024 * 1024)
        {
            int length = checked((int)response.ContentLength);
            byte[] rented = ArrayPool<byte>.Shared.Rent(length);
            PooledBufferOwner owner = new(rented);
            try
            {
                int read = 0;
                while (read < length)
                {
                    int count = await stream.ReadAsync(rented.AsMemory(read, length - read), cancellationToken).ConfigureAwait(false);
                    if (count == 0) break;
                    read += count;
                }

                ReadOnlyMemory<byte> pooledContent = rented.AsMemory(0, read);
                if (pooledContent.Span.StartsWith(Utf8Bom)) pooledContent = pooledContent[Utf8Bom.Length..];
                FocusedContent? pooledResult = TryPrune(pooledContent, response.ContentType, response.Artifact.ArtifactId, ignorePaths);
                if (pooledResult is null)
                {
                    owner.Dispose();
                    return null;
                }

                return pooledResult with { Owner = CombineOwners(pooledResult.Owner, owner) };
            }
            catch
            {
                owner.Dispose();
                throw;
            }
        }

        using MemoryStream buffer = response.ContentLength is > 0 and <= int.MaxValue
            ? new MemoryStream((int)response.ContentLength)
            : new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        if (!buffer.TryGetBuffer(out ArraySegment<byte> segment))
        {
            return null;
        }

        ReadOnlyMemory<byte> content = segment.AsMemory();
        if (content.Span.StartsWith(Utf8Bom))
        {
            content = content[Utf8Bom.Length..];
        }

        return TryPrune(content, response.ContentType, response.Artifact.ArtifactId, ignorePaths);
    }

    private static async Task<ResponseArtifactMetadata> SaveFocusedResponseAsync(
        RunId runId,
        string relativePath,
        ResponseArtifactMetadata sourceResponse,
        ReadOnlyMemory<byte> content,
        IRunArtifactStore artifactStore,
        CancellationToken cancellationToken)
    {
        if (!MemoryMarshal.TryGetArray(content, out ArraySegment<byte> segment) || segment.Array is null)
        {
            segment = new ArraySegment<byte>(content.ToArray());
        }

        byte[] bytes = segment.Array!;
        await using MemoryStream stream = new MemoryStream(bytes, segment.Offset, segment.Count, writable: false);
        RequestItem focusedRequest = new RequestItem($"focused/{relativePath}", sourceResponse.ContentType ?? "text/plain", segment.Count);
        return await artifactStore.SaveResponseAsync(
            runId,
            sourceResponse.Endpoint,
            focusedRequest,
            sourceResponse.StatusCode,
            sourceResponse.ContentType,
            stream,
            cancellationToken).ConfigureAwait(false);
    }

    private static FocusedContent? TryPrune(
        ReadOnlyMemory<byte> content,
        string? contentType,
        string? fileName,
        IReadOnlyCollection<string> ignorePaths)
    {
        if (content.IsEmpty || ignorePaths.Count == 0)
        {
            return new FocusedContent(content, WasPruned: false, StructuredDocumentKind.Unknown, IsFormatted: false);
        }

        IgnorePathMatcher matcher = GetOrCreateMatcher(ignorePaths);
        return DetectDocumentKind(contentType, fileName, content) switch
        {
            StructuredDocumentKind.Json => TryPruneJson(content, matcher),
            StructuredDocumentKind.Xml => TryPruneXml(Encoding.UTF8.GetString(content.Span), matcher),
            _ => null,
        };
    }

    private static FocusedContent? TryPruneJson(ReadOnlyMemory<byte> content, IgnorePathMatcher matcher)
    {
        try
        {
            Utf8JsonReader reader = new Utf8JsonReader(content.Span);
            if (!reader.Read())
            {
                return new FocusedContent(content, WasPruned: false, StructuredDocumentKind.Json, IsFormatted: false);
            }

            using PooledOutputWriter output = new(Math.Min(content.Length, 64 * 1024));
            using Utf8JsonWriter writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = true });
            int removedCount = WritePrunedJsonValue(ref reader, writer, string.Empty, matcher);
            if (removedCount == 0)
            {
                return new FocusedContent(content, WasPruned: false, StructuredDocumentKind.Json, IsFormatted: false);
            }

            writer.Flush();
            (ReadOnlyMemory<byte> written, IDisposable owner) = output.Detach();
            return new FocusedContent(written, WasPruned: true, StructuredDocumentKind.Json, IsFormatted: true, owner);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int WritePrunedJsonValue(
        ref Utf8JsonReader reader,
        Utf8JsonWriter writer,
        string path,
        IgnorePathMatcher matcher)
    {
        int removedCount = 0;
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            writer.WriteStartObject();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                string propertyName = reader.GetString() ?? string.Empty;
                bool ignoreDirectly = matcher.IsDirectChildMatch(path, propertyName);
                string propertyPath = ignoreDirectly ? string.Empty : AppendPath(path, propertyName);
                if (!reader.Read())
                {
                    throw new JsonException("JSON property has no value.");
                }

                if (ignoreDirectly || ShouldIgnorePath(propertyPath, matcher))
                {
                    reader.Skip();
                    removedCount++;
                    continue;
                }

                writer.WritePropertyName(propertyName);
                removedCount += WritePrunedJsonValue(ref reader, writer, propertyPath, matcher);
            }

            writer.WriteEndObject();
        }
        else if (reader.TokenType == JsonTokenType.StartArray)
        {
            writer.WriteStartArray();
            int index = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                removedCount += WritePrunedJsonValue(ref reader, writer, $"{path}[{index}]", matcher);
                index++;
            }

            writer.WriteEndArray();
        }
        else
        {
            WriteJsonScalar(ref reader, writer);
        }

        return removedCount;
    }

    private static void WriteJsonScalar(ref Utf8JsonReader reader, Utf8JsonWriter writer)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                writer.WriteStringValue(reader.GetString());
                break;
            case JsonTokenType.Number:
                writer.WriteRawValue(reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan, skipInputValidation: true);
                break;
            case JsonTokenType.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonTokenType.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonTokenType.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new JsonException($"Unexpected JSON token {reader.TokenType}.");
        }
    }

    private static FocusedContent? TryPruneXml(string content, IgnorePathMatcher matcher)
    {
        try
        {
            XDocument document = XDocument.Parse(content, LoadOptions.PreserveWhitespace);
            if (document.Root is null)
            {
                return new FocusedContent(Utf8WithoutBom.GetBytes(content), WasPruned: false, StructuredDocumentKind.Xml, IsFormatted: false);
            }

            int removedCount = PruneXmlChildren(document.Root, new[] { document.Root.Name.LocalName }, matcher);
            if (removedCount == 0)
            {
                return new FocusedContent(Utf8WithoutBom.GetBytes(content), WasPruned: false, StructuredDocumentKind.Xml, IsFormatted: false);
            }

            XmlWriterSettings settings = new XmlWriterSettings
            {
                Encoding = Utf8WithoutBom,
                OmitXmlDeclaration = document.Declaration is null,
                Indent = true,
                NewLineChars = "\n",
                NewLineHandling = NewLineHandling.Replace,
            };

            StringBuilder builder = new StringBuilder();
            using StringWriter stringWriter = new StringWriter(builder, CultureInfo.InvariantCulture);
            using XmlWriter writer = XmlWriter.Create(stringWriter, settings);
            document.Save(writer);
            writer.Flush();
            return new FocusedContent(Utf8WithoutBom.GetBytes(builder.ToString()), WasPruned: true, StructuredDocumentKind.Xml, IsFormatted: true);
        }
        catch (Exception ex) when (ex is XmlException or InvalidOperationException)
        {
            return null;
        }
    }

    private static FocusedContent EnsureFormatted(FocusedContent content)
    {
        if (content.IsFormatted)
        {
            return content;
        }

        ReadOnlyMemory<byte> formatted = content.Kind switch
        {
            StructuredDocumentKind.Json => FormatJson(content.Content),
            StructuredDocumentKind.Xml => Utf8WithoutBom.GetBytes(TryFormatXml(Encoding.UTF8.GetString(content.Content.Span))),
            _ => content.Content,
        };
        return content with { Content = formatted, IsFormatted = true };
    }

    private static ReadOnlyMemory<byte> FormatJson(ReadOnlyMemory<byte> content)
    {
        try
        {
            Utf8JsonReader reader = new(content.Span);
            if (!reader.Read()) { return content; }
            ArrayBufferWriter<byte> output = new(Math.Min(content.Length, 64 * 1024));
            using Utf8JsonWriter writer = new(output, new JsonWriterOptions { Indented = true });
            CopyJsonValue(ref reader, writer);
            writer.Flush();
            return output.WrittenMemory;
        }
        catch (JsonException)
        {
            return content;
        }
    }

    private static void CopyJsonValue(ref Utf8JsonReader reader, Utf8JsonWriter writer)
    {
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            writer.WriteStartObject();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                writer.WritePropertyName(reader.GetString() ?? string.Empty);
                if (!reader.Read()) { throw new JsonException("JSON property has no value."); }
                CopyJsonValue(ref reader, writer);
            }
            writer.WriteEndObject();
            return;
        }

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            writer.WriteStartArray();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                CopyJsonValue(ref reader, writer);
            }
            writer.WriteEndArray();
            return;
        }

        WriteJsonScalar(ref reader, writer);
    }

    private static int PruneXmlChildren(XElement parent, IReadOnlyCollection<string> parentPaths, IgnorePathMatcher matcher)
    {
        int removedCount = 0;
        List<XElement> children = parent.Elements().ToList();
        Dictionary<string, int> siblingTotals = children
            .GroupBy(element => element.Name.LocalName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        Dictionary<string, int> siblingIndexes = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (XElement child in children)
        {
            string name = child.Name.LocalName;
            siblingIndexes.TryGetValue(name, out int nextIndex);
            siblingIndexes[name] = nextIndex + 1;

            List<string> childPaths = parentPaths.Select(parentPath => AppendPath(parentPath, name)).ToList();
            if (siblingTotals[name] > 1)
            {
                childPaths.AddRange(parentPaths.Select(parentPath => $"{AppendPath(parentPath, name)}[{nextIndex}]"));
                childPaths.AddRange(parentPaths.Select(parentPath => $"{AppendPath(parentPath, name)}[*]"));
            }

            if (childPaths.Any(path => ShouldIgnorePath(path, matcher)))
            {
                child.Remove();
                removedCount++;
                continue;
            }

            removedCount += PruneXmlChildren(child, childPaths, matcher);
        }

        return removedCount;
    }

    private static string TryFormatXml(string text)
    {
        try
        {
            XDocument document = XDocument.Parse(text, LoadOptions.None);
            StringBuilder builder = new StringBuilder();
            using StringWriter stringWriter = new StringWriter(builder, CultureInfo.InvariantCulture);
            using XmlWriter writer = XmlWriter.Create(stringWriter, new XmlWriterSettings
            {
                OmitXmlDeclaration = document.Declaration is null,
                Indent = true,
                NewLineChars = "\n",
                NewLineHandling = NewLineHandling.Replace,
            });

            document.Save(writer);
            writer.Flush();
            return builder.ToString();
        }
        catch (Exception ex) when (ex is XmlException or InvalidOperationException)
        {
            return text;
        }
    }

    private static IgnorePathMatcher GetOrCreateMatcher(IReadOnlyCollection<string> ignorePaths)
    {
        string cacheKey = string.Join('', ignorePaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
        return MatcherCache.GetOrAdd(cacheKey, _ => new IgnorePathMatcher(BuildMatchPatterns(ignorePaths)));
    }

    private static IReadOnlyList<string> BuildMatchPatterns(IEnumerable<string> paths)
    {
        HashSet<string> patterns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            string normalized = path.Trim();
            patterns.Add(normalized);
            int firstDot = normalized.IndexOf('.', StringComparison.Ordinal);
            if (firstDot > 0 && firstDot < normalized.Length - 1)
            {
                patterns.Add(normalized[(firstDot + 1)..]);
            }
        }

        return patterns.ToList();
    }

    private static bool ShouldIgnorePath(string path, IgnorePathMatcher matcher)
    {
        if (matcher.IsMatch(path))
        {
            return true;
        }

        int firstDot = path.IndexOf('.', StringComparison.Ordinal);
        return firstDot > 0 && firstDot < path.Length - 1 && matcher.IsMatch(path[(firstDot + 1)..]);
    }

    private static string AppendPath(string parent, string child) =>
        string.IsNullOrWhiteSpace(parent) ? child : $"{parent}.{child}";

    private static StructuredDocumentKind DetectDocumentKind(string? contentType, string? fileName, ReadOnlyMemory<byte> content)
    {
        if (contentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true) { return StructuredDocumentKind.Json; }
        if (contentType?.Contains("xml", StringComparison.OrdinalIgnoreCase) == true) { return StructuredDocumentKind.Xml; }
        string extension = Path.GetExtension(fileName ?? string.Empty);
        if (string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase)) { return StructuredDocumentKind.Json; }
        if (string.Equals(extension, ".xml", StringComparison.OrdinalIgnoreCase)) { return StructuredDocumentKind.Xml; }
        foreach (byte value in content.Span)
        {
            if (value is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') { continue; }
            return value switch
            {
                (byte)'{' or (byte)'[' => StructuredDocumentKind.Json,
                (byte)'<' => StructuredDocumentKind.Xml,
                _ => StructuredDocumentKind.Unknown,
            };
        }

        return StructuredDocumentKind.Unknown;
    }

    private enum StructuredDocumentKind
    {
        Unknown,
        Json,
        Xml,
    }

    private sealed record FocusedContent(
        ReadOnlyMemory<byte> Content,
        bool WasPruned,
        StructuredDocumentKind Kind,
        bool IsFormatted,
        IDisposable? Owner = null) : IDisposable
    {
        public void Dispose() => Owner?.Dispose();
    }

    private static IDisposable CombineOwners(IDisposable? first, IDisposable second) =>
        first is null ? second : new CompositeOwner(first, second);

    private sealed class PooledBufferOwner(byte[] buffer) : IDisposable
    {
        private byte[] ownedBuffer = buffer;

        public void Dispose()
        {
            byte[] returned = Interlocked.Exchange(ref ownedBuffer, Array.Empty<byte>());
            if (returned.Length > 0) ArrayPool<byte>.Shared.Return(returned, clearArray: true);
        }
    }

    private sealed class CompositeOwner(IDisposable first, IDisposable second) : IDisposable
    {
        private IDisposable? firstOwner = first;
        private IDisposable? secondOwner = second;

        public void Dispose()
        {
            Interlocked.Exchange(ref firstOwner, null)?.Dispose();
            Interlocked.Exchange(ref secondOwner, null)?.Dispose();
        }
    }

    private sealed class PooledOutputWriter : IBufferWriter<byte>, IDisposable
    {
        private const int MaximumPooledLength = 1024 * 1024;
        private byte[] buffer;
        private bool pooled;
        private int written;

        public PooledOutputWriter(int initialCapacity)
        {
            pooled = initialCapacity <= MaximumPooledLength;
            buffer = pooled
                ? ArrayPool<byte>.Shared.Rent(Math.Max(256, initialCapacity))
                : GC.AllocateUninitializedArray<byte>(initialCapacity);
        }

        public void Advance(int count) => written = checked(written + count);
        public Memory<byte> GetMemory(int sizeHint = 0) { Ensure(sizeHint); return buffer.AsMemory(written); }
        public Span<byte> GetSpan(int sizeHint = 0) { Ensure(sizeHint); return buffer.AsSpan(written); }

        public (ReadOnlyMemory<byte> Content, IDisposable Owner) Detach()
        {
            byte[] detached = buffer;
            int length = written;
            bool detachedPooled = pooled;
            buffer = Array.Empty<byte>();
            written = 0;
            pooled = false;
            return (detached.AsMemory(0, length), new OutputBufferOwner(detached, detachedPooled));
        }

        public void Dispose()
        {
            byte[] returned = Interlocked.Exchange(ref buffer, Array.Empty<byte>());
            if (pooled && returned.Length > 0) ArrayPool<byte>.Shared.Return(returned, clearArray: true);
            pooled = false;
            written = 0;
        }

        private void Ensure(int sizeHint)
        {
            int required = checked(written + Math.Max(1, sizeHint));
            if (required <= buffer.Length) return;
            int growth = Math.Max(required, buffer.Length <= int.MaxValue / 2 ? buffer.Length * 2 : int.MaxValue);
            bool nextPooled = growth <= MaximumPooledLength;
            byte[] replacement = nextPooled
                ? ArrayPool<byte>.Shared.Rent(growth)
                : GC.AllocateUninitializedArray<byte>(growth);
            buffer.AsSpan(0, written).CopyTo(replacement);
            if (pooled) ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
            buffer = replacement;
            pooled = nextPooled;
        }
    }

    private sealed class OutputBufferOwner(byte[] buffer, bool pooled) : IDisposable
    {
        private byte[] ownedBuffer = buffer;
        public void Dispose()
        {
            byte[] returned = Interlocked.Exchange(ref ownedBuffer, Array.Empty<byte>());
            if (pooled && returned.Length > 0) ArrayPool<byte>.Shared.Return(returned, clearArray: true);
        }
    }
}
