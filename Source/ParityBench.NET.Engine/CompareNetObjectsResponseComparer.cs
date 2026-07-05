using System.Collections;
using System.Text.RegularExpressions;

using KellermanSoftware.CompareNetObjects;
using ParityBench.NET.Application.Requests;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Engine;

public sealed class CompareNetObjectsResponseComparer : IResponseComparer
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private readonly IRunArtifactStore artifactStore;
    private readonly IResponseBodyDeserializer deserializer;

    public CompareNetObjectsResponseComparer(
        IRunArtifactStore artifactStore,
        IResponseBodyDeserializer deserializer)
    {
        this.artifactStore = artifactStore;
        this.deserializer = deserializer;
    }

    public async Task<RequestPairResult> CompareAsync(
        RequestItem request,
        RunOptions options,
        ResponseArtifactMetadata? responseA,
        ResponseArtifactMetadata? responseB,
        string? errorMessage,
        CancellationToken cancellationToken = default)
    {
        if (!CanModelCompare(responseA, responseB, errorMessage))
        {
            return RequestPairResult.Classify(request, responseA, responseB, errorMessage);
        }

        if (CanUseHashFastPath(responseA!, responseB!, options.Comparison))
        {
            return RequestPairResult.Classify(request, responseA, responseB);
        }

        try
        {
            await using Stream bodyA = await artifactStore
                .OpenReadAsync(responseA!.Artifact, cancellationToken)
                .ConfigureAwait(false);
            await using Stream bodyB = await artifactStore
                .OpenReadAsync(responseB!.Artifact, cancellationToken)
                .ConfigureAwait(false);

            object modelA = await deserializer
                .DeserializeAsync(options.ResponseModelName, bodyA, responseA.ContentType, options.Comparison, cancellationToken)
                .ConfigureAwait(false);
            object modelB = await deserializer
                .DeserializeAsync(options.ResponseModelName, bodyB, responseB.ContentType, options.Comparison, cancellationToken)
                .ConfigureAwait(false);

            CompareLogic compareLogic = CreateCompareLogic(options.Comparison);
            ComparisonResult comparisonResult = compareLogic.Compare(modelA, modelB);
            List<ComparisonDifference> differences = comparisonResult
                .Differences
                .Where(difference => !ShouldFilterDifference(difference, options.Comparison))
                .Take(options.Comparison.MaxDifferences)
                .Select(ToDomainDifference)
                .ToList();

            return RequestPairResult.FromComparison(
                request,
                responseA,
                responseB,
                differences,
                differences.Count == 0 ? "Responses are equal after configured comparison rules." : null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new RequestPairResult(
                request.RelativePath,
                RequestPairOutcome.ExecutionFailed,
                responseA,
                responseB,
                ex.Message);
        }
    }

    private static CompareLogic CreateCompareLogic(ComparisonOptions options)
    {
        CompareLogic compareLogic = new CompareLogic();
        compareLogic.Config.MaxDifferences = Math.Min(options.MaxDifferences, 1000);
        compareLogic.Config.IgnoreObjectTypes = false;
        compareLogic.Config.ComparePrivateFields = false;
        compareLogic.Config.ComparePrivateProperties = true;
        compareLogic.Config.CompareReadOnly = true;
        compareLogic.Config.IgnoreCollectionOrder = ShouldIgnoreCollectionOrder(options);
        compareLogic.Config.CaseSensitive = !options.IgnoreStringCase;
        compareLogic.Config.Caching = true;
        compareLogic.Config.SkipInvalidIndexers = true;
        compareLogic.Config.MembersToIgnore = BuildMembersToIgnore(options);

        return compareLogic;
    }

    private static List<string> BuildMembersToIgnore(ComparisonOptions options)
    {
        List<string> membersToIgnore = new List<string>
        {
            "Length",
            "LongLength",
            "NativeLength",
        };

        foreach (IgnoreRuleDefinition rule in options.IgnoreRules.Where(rule => rule.IgnoreCompletely))
        {
            foreach (string pattern in ExpandIgnorePath(rule.PropertyPath))
            {
                if (!membersToIgnore.Contains(pattern, StringComparer.Ordinal))
                {
                    membersToIgnore.Add(pattern);
                }
            }
        }

        return membersToIgnore;
    }

    private static IEnumerable<string> ExpandIgnorePath(string propertyPath)
    {
        yield return propertyPath;

        if (propertyPath.Contains("[*]", StringComparison.Ordinal))
        {
            for (int index = 0; index < 10; index++)
            {
                yield return propertyPath.Replace("[*]", $"[{index}]", StringComparison.Ordinal);
            }
        }
    }

    private static bool CanModelCompare(
        ResponseArtifactMetadata? responseA,
        ResponseArtifactMetadata? responseB,
        string? errorMessage) =>
        string.IsNullOrWhiteSpace(errorMessage)
        && responseA is not null
        && responseB is not null
        && IsSuccessStatusCode(responseA.StatusCode)
        && IsSuccessStatusCode(responseB.StatusCode);

    private static bool CanUseHashFastPath(
        ResponseArtifactMetadata responseA,
        ResponseArtifactMetadata responseB,
        ComparisonOptions options) =>
        !options.HasComparisonAffectingOptions
        && responseA.ContentLength == responseB.ContentLength
        && string.Equals(responseA.Sha256, responseB.Sha256, StringComparison.OrdinalIgnoreCase);

    private static bool ShouldIgnoreCollectionOrder(ComparisonOptions options) =>
        options.IgnoreCollectionOrder
        || options.IgnoreRules.Any(rule => rule.IgnoreCollectionOrder)
        || options.SmartIgnoreRules.Any(rule => rule.IsEnabled && rule.Kind == SmartIgnoreRuleKind.CollectionOrdering);

    private static bool ShouldFilterDifference(Difference difference, ComparisonOptions options) =>
        ShouldIgnoreByRule(difference.PropertyName, options)
        || ShouldIgnoreBySmartRule(difference, options)
        || ShouldIgnoreByTrailingWhitespace(difference, options)
        || ShouldIgnoreByNullEmptyCollectionRule(difference, options);

    private static bool ShouldIgnoreByRule(string propertyPath, ComparisonOptions options) =>
        options.IgnoreRules
            .Where(rule => rule.IgnoreCompletely)
            .Any(rule => PathMatches(rule.PropertyPath, propertyPath));

    private static bool ShouldIgnoreBySmartRule(Difference difference, ComparisonOptions options)
    {
        string propertyPath = difference.PropertyName ?? string.Empty;
        string leafName = GetLeafPropertyName(propertyPath);

        foreach (SmartIgnoreRuleDefinition rule in options.SmartIgnoreRules.Where(rule => rule.IsEnabled))
        {
            if (rule.Kind == SmartIgnoreRuleKind.PropertyName
                && string.Equals(leafName, rule.Value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (rule.Kind == SmartIgnoreRuleKind.NamePattern && MatchesPattern(propertyPath, rule.Value))
            {
                return true;
            }

            if (rule.Kind == SmartIgnoreRuleKind.PropertyType && MatchesPropertyType(difference, rule.Value))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ShouldIgnoreByTrailingWhitespace(Difference difference, ComparisonOptions options)
    {
        if (!options.IgnoreTrailingWhitespaceAtEnd)
        {
            return false;
        }

        string? valueA = difference.Object1Value?.ToString();
        string? valueB = difference.Object2Value?.ToString();

        return valueA is not null
            && valueB is not null
            && string.Equals(valueA.TrimEnd(), valueB.TrimEnd(), StringComparison.Ordinal);
    }

    private static bool ShouldIgnoreByNullEmptyCollectionRule(Difference difference, ComparisonOptions options)
    {
        bool appliesGlobally = options.TreatNullAndEmptyCollectionsAsEqual;
        bool appliesByRule = options.IgnoreRules.Any(rule =>
            rule.TreatNullAndEmptyCollectionsAsEqual && PathMatches(rule.PropertyPath, difference.PropertyName));

        if (!appliesGlobally && !appliesByRule)
        {
            return false;
        }

        return (IsMissingOrNullValue(difference.Object1Value) && IsEmptyCollectionValue(difference.Object2Value))
            || (IsMissingOrNullValue(difference.Object2Value) && IsEmptyCollectionValue(difference.Object1Value));
    }

    private static bool IsMissingOrNullValue(object? value)
    {
        if (value is null)
        {
            return true;
        }

        string text = value.ToString() ?? string.Empty;
        return string.Equals(text, "(null)", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "null", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsEmptyCollectionValue(object? value)
    {
        if (value is null)
        {
            return false;
        }

        if (value is ICollection collection)
        {
            return collection.Count == 0;
        }

        if (value is IEnumerable enumerable && value is not string)
        {
            return !enumerable.Cast<object>().Any();
        }

        string text = value.ToString() ?? string.Empty;
        return string.Equals(text, "0", StringComparison.Ordinal)
            || string.Equals(text, string.Empty, StringComparison.Ordinal)
            || string.Equals(text, "[]", StringComparison.Ordinal)
            || string.Equals(text, "Count = 0", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("System.Collections.", StringComparison.Ordinal);
    }

    private static bool PathMatches(string rulePath, string? propertyPath)
    {
        if (string.IsNullOrWhiteSpace(propertyPath))
        {
            return false;
        }

        if (string.Equals(rulePath, propertyPath, StringComparison.OrdinalIgnoreCase)
            || propertyPath.StartsWith(rulePath + ".", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string pattern = "^" + Regex.Escape(rulePath)
            .Replace("\\[\\*\\]", "\\[\\d+\\]", StringComparison.Ordinal)
            .Replace("\\*", ".*", StringComparison.Ordinal) + "(\\..*)?$";

        return Regex.IsMatch(propertyPath, pattern, RegexOptions.IgnoreCase, RegexTimeout);
    }

    private static bool MatchesPattern(string propertyPath, string pattern)
    {
        try
        {
            return Regex.IsMatch(propertyPath, pattern, RegexOptions.IgnoreCase, RegexTimeout);
        }
        catch (ArgumentException)
        {
            string wildcardPattern = "^" + Regex.Escape(pattern)
                .Replace("\\*", ".*", StringComparison.Ordinal)
                .Replace("\\?", ".", StringComparison.Ordinal) + "$";

            return Regex.IsMatch(propertyPath, wildcardPattern, RegexOptions.IgnoreCase, RegexTimeout);
        }
    }

    private static bool MatchesPropertyType(Difference difference, string typeName)
    {
        object? value = difference.Object1Value ?? difference.Object2Value;
        Type? type = value?.GetType();

        return type is not null
            && (string.Equals(type.Name, typeName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(type.FullName, typeName, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetLeafPropertyName(string propertyPath)
    {
        string cleanedPath = Regex.Replace(propertyPath, "\\[\\d+\\]", string.Empty, RegexOptions.None, RegexTimeout);
        int separatorIndex = cleanedPath.LastIndexOf('.');
        return separatorIndex < 0 ? cleanedPath : cleanedPath[(separatorIndex + 1)..];
    }

    private static ComparisonDifference ToDomainDifference(Difference difference) =>
        new ComparisonDifference(
            string.IsNullOrWhiteSpace(difference.PropertyName) ? "Response" : difference.PropertyName,
            difference.Object1Value?.ToString(),
            difference.Object2Value?.ToString(),
            difference.ToString());

    private static bool IsSuccessStatusCode(int statusCode) => statusCode is >= 200 and <= 299;
}



