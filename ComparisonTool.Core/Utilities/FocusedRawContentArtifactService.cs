using System.Text;
using ComparisonTool.Core.Comparison.Configuration;
using ComparisonTool.Core.Comparison.Results;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Core.Utilities;

/// <summary>
/// Creates focused side-by-side artifacts with ignore-complete fields removed.
/// </summary>
public sealed class FocusedRawContentArtifactService
{
    public const string MetadataIgnoreCompleteRulesKey = "FocusedRawContentIgnoreCompleteRules";
    public const string MetadataFocusedPairCountKey = "FocusedRawContentPairCount";

    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly StructuredContentPruningService pruningService;
    private readonly ILogger<FocusedRawContentArtifactService> logger;

    public FocusedRawContentArtifactService(
        StructuredContentPruningService pruningService,
        ILogger<FocusedRawContentArtifactService> logger)
    {
        this.pruningService = pruningService;
        this.logger = logger;
    }

    public async Task PopulateFocusedRawContentAsync(
        MultiFolderComparisonResult result,
        IEnumerable<IgnoreRule> ignoreRules,
        string artifactRootDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(ignoreRules);

        var ignoreCompletePaths = ignoreRules
            .Where(rule => rule.IgnoreCompletely && !string.IsNullOrWhiteSpace(rule.PropertyPath))
            .Select(rule => rule.PropertyPath.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        result.Metadata ??= new Dictionary<string, object>(StringComparer.Ordinal);
        result.Metadata[MetadataIgnoreCompleteRulesKey] = ignoreCompletePaths;

        if (ignoreCompletePaths.Count == 0 || result.FilePairResults.Count == 0)
        {
            result.Metadata[MetadataFocusedPairCountKey] = 0;
            return;
        }

        Directory.CreateDirectory(artifactRootDirectory);

        var focusedCount = 0;
        for (var index = 0; index < result.FilePairResults.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pair = result.FilePairResults[index];
            if (pair.HasError || string.IsNullOrWhiteSpace(pair.File1Path) || string.IsNullOrWhiteSpace(pair.File2Path))
            {
                continue;
            }

            try
            {
                if (await TryPopulatePairAsync(pair, index, ignoreCompletePaths, artifactRootDirectory, cancellationToken).ConfigureAwait(false))
                {
                    focusedCount++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                logger.LogWarning(ex, "Failed to build focused raw content for {Pair}.", pair.GetDisplayIdentifier());
            }
        }

        result.Metadata[MetadataFocusedPairCountKey] = focusedCount;
    }

    public async Task PopulateFocusedRawContentAsync(
        MultiFolderComparisonResult result,
        IEnumerable<string> ignoreCompletePaths,
        string artifactRootDirectory,
        CancellationToken cancellationToken = default)
    {
        var rules = ignoreCompletePaths.Select(path => new IgnoreRule
        {
            PropertyPath = path,
            IgnoreCompletely = true,
        });

        await PopulateFocusedRawContentAsync(result, rules, artifactRootDirectory, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> TryPopulatePairAsync(
        FilePairComparisonResult pair,
        int index,
        IReadOnlyCollection<string> ignoreCompletePaths,
        string artifactRootDirectory,
        CancellationToken cancellationToken)
    {
        var bytesA = await File.ReadAllBytesAsync(pair.File1Path!, cancellationToken).ConfigureAwait(false);
        var bytesB = await File.ReadAllBytesAsync(pair.File2Path!, cancellationToken).ConfigureAwait(false);

        var prunedA = pruningService.TryPrune(bytesA, pair.ContentTypeA, pair.File1Name, ignoreCompletePaths);
        var prunedB = pruningService.TryPrune(bytesB, pair.ContentTypeB, pair.File2Name, ignoreCompletePaths);

        if (!prunedA.IsSupported || !prunedB.IsSupported || (!prunedA.WasPruned && !prunedB.WasPruned))
        {
            return false;
        }

        var contentA = prunedA.WasPruned
            ? prunedA.Content
            : StructuredTextDisplayFormatter.FormatForDisplay(Encoding.UTF8.GetString(bytesA), pair.ContentTypeA, pair.File1Name);
        var contentB = prunedB.WasPruned
            ? prunedB.Content
            : StructuredTextDisplayFormatter.FormatForDisplay(Encoding.UTF8.GetString(bytesB), pair.ContentTypeB, pair.File2Name);

        var pairDirectory = Path.Combine(artifactRootDirectory, BuildPairDirectoryName(pair, index));
        Directory.CreateDirectory(pairDirectory);

        var focusedPathA = Path.Combine(pairDirectory, BuildFocusedFileName(pair.File1Name, "a"));
        var focusedPathB = Path.Combine(pairDirectory, BuildFocusedFileName(pair.File2Name, "b"));

        await File.WriteAllTextAsync(focusedPathA, contentA, Utf8WithoutBom, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(focusedPathB, contentB, Utf8WithoutBom, cancellationToken).ConfigureAwait(false);

        pair.FocusedFile1Path = focusedPathA;
        pair.FocusedFile2Path = focusedPathB;
        pair.FocusedRawContentRuleCount = ignoreCompletePaths.Count;
        return true;
    }

    private static string BuildPairDirectoryName(FilePairComparisonResult pair, int index)
    {
        var name = string.IsNullOrWhiteSpace(pair.RequestRelativePath)
            ? $"{pair.File1Name}-{pair.File2Name}"
            : pair.RequestRelativePath;

        var sanitized = string.Concat(name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) || ch is '/' or '\\' ? '_' : ch));
        return $"{index + 1:00000}-{sanitized}";
    }

    private static string BuildFocusedFileName(string fileName, string suffix)
    {
        var name = string.IsNullOrWhiteSpace(fileName) ? $"focused-{suffix}.txt" : Path.GetFileName(fileName);
        var extension = Path.GetExtension(name);
        var stem = Path.GetFileNameWithoutExtension(name);
        return string.IsNullOrWhiteSpace(extension)
            ? $"{stem}.{suffix}.focused.txt"
            : $"{stem}.{suffix}.focused{extension}";
    }
}