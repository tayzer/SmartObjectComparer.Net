using System.Text.Json;
using System.Text.Json.Serialization;

using ParityBench.NET.Domain.Comparison;

namespace ParityBench.NET.Infrastructure;

public static class ComparisonRuleDefaultsFileLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static ComparisonRuleDefaults Load(ComparisonRuleDefaultsFileOptions? options) =>
        Load(options, AppContext.BaseDirectory);

    public static ComparisonRuleDefaults Load(
        ComparisonRuleDefaultsFileOptions? options,
        string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        string? ignoreRulesFile = options?.IgnoreRulesFile;
        if (string.IsNullOrWhiteSpace(ignoreRulesFile))
        {
            return new ComparisonRuleDefaults(
                ignoreXmlNamespaces: options?.IgnoreXmlNamespacesOverride
                    ?? options?.IgnoreXmlNamespacesWhenFileMissing
                    ?? true);
        }

        string resolvedPath = ResolvePath(ignoreRulesFile, baseDirectory);
        if (!File.Exists(resolvedPath))
        {
            throw new InvalidOperationException($"Comparison ignore rules file '{resolvedPath}' was not found.");
        }

        try
        {
            string fileText = File.ReadAllText(resolvedPath);
            return Parse(fileText, options?.IgnoreXmlNamespacesOverride);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or JsonException)
        {
            throw new InvalidOperationException(
                $"Comparison ignore rules file '{resolvedPath}' could not be loaded.",
                ex);
        }
    }

    public static ComparisonRuleDefaults Parse(
        string fileText,
        bool? ignoreXmlNamespacesOverride = null)
    {
        ArgumentNullException.ThrowIfNull(fileText);

        return LooksLikeJson(fileText)
            ? ParseJsonConfiguration(fileText, ignoreXmlNamespacesOverride)
            : ParseTextIgnoreRules(fileText, ignoreXmlNamespacesOverride ?? true);
    }

    private static ComparisonRuleDefaults ParseTextIgnoreRules(
        string fileText,
        bool ignoreXmlNamespaces) =>
        new ComparisonRuleDefaults(
            ignoreXmlNamespaces: ignoreXmlNamespaces,
            ignoreRules: NormalizeIgnoreRules(fileText
                .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && !line.StartsWith("#", StringComparison.Ordinal))
                .Select(line => new IgnoreRuleDefinition(line))));

    private static ComparisonRuleDefaults ParseJsonConfiguration(
        string json,
        bool? ignoreXmlNamespacesOverride)
    {
        ComparisonRuleDefaultsConfigurationFile? configuration =
            JsonSerializer.Deserialize<ComparisonRuleDefaultsConfigurationFile>(json, JsonOptions);

        if (configuration is null)
        {
            throw new InvalidOperationException("File does not contain a supported comparison configuration.");
        }

        ComparisonRuleDefaultsGlobalSettings? globalSettings = configuration.GlobalSettings;
        return new ComparisonRuleDefaults(
            ignoreCollectionOrder: globalSettings?.IgnoreCollectionOrder ?? false,
            ignoreStringCase: globalSettings?.IgnoreStringCase ?? false,
            ignoreTrailingWhitespaceAtEnd: globalSettings?.IgnoreTrailingWhitespaceAtEnd ?? false,
            treatNullAndEmptyCollectionsAsEqual: globalSettings?.TreatNullAndEmptyCollectionsAsEqual ?? false,
            ignoreXmlNamespaces: ignoreXmlNamespacesOverride ?? globalSettings?.IgnoreXmlNamespaces ?? false,
            ignoreRules: NormalizeIgnoreRules(configuration.IgnoreRules ?? Array.Empty<IgnoreRuleDefinition>()));
    }

    private static IReadOnlyList<IgnoreRuleDefinition> NormalizeIgnoreRules(IEnumerable<IgnoreRuleDefinition> rules) =>
        rules
            .Where(rule => !string.IsNullOrWhiteSpace(rule.PropertyPath))
            .GroupBy(rule => rule.PropertyPath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(rule => rule.PropertyPath, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static bool LooksLikeJson(string text)
    {
        ReadOnlySpan<char> trimmed = text.AsSpan().TrimStart();
        return trimmed.Length > 0 && trimmed[0] == '{';
    }

    private static string ResolvePath(string path, string baseDirectory)
    {
        string trimmedPath = path.Trim();
        return Path.IsPathRooted(trimmedPath)
            ? Path.GetFullPath(trimmedPath)
            : Path.GetFullPath(Path.Combine(baseDirectory, trimmedPath));
    }

    private sealed record ComparisonRuleDefaultsConfigurationFile(
        int SchemaVersion,
        ComparisonRuleDefaultsGlobalSettings? GlobalSettings,
        int MaxDifferences,
        IReadOnlyList<IgnoreRuleDefinition>? IgnoreRules);

    private sealed record ComparisonRuleDefaultsGlobalSettings(
        bool IgnoreCollectionOrder,
        bool IgnoreStringCase,
        bool IgnoreTrailingWhitespaceAtEnd,
        bool TreatNullAndEmptyCollectionsAsEqual,
        bool IgnoreXmlNamespaces);
}
