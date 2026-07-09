using System.Text.Json;
using System.Text.Json.Serialization;

using ParityBench.NET.Domain.Comparison;

namespace ParityBench.NET.ClientCustomerLookupExample;

public static class ClientCustomerLookupComparisonRuleDefaultsLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static ComparisonRuleDefaults Load(ClientCustomerLookupComparisonOptions? options) =>
        Load(options, AppContext.BaseDirectory);

    public static ComparisonRuleDefaults Load(
        ClientCustomerLookupComparisonOptions? options,
        string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        string? ignoreRulesFile = options?.IgnoreRulesFile;
        if (string.IsNullOrWhiteSpace(ignoreRulesFile))
        {
            return new ComparisonRuleDefaults(ignoreXmlNamespaces: true);
        }

        string resolvedPath = ResolvePath(ignoreRulesFile, baseDirectory);
        if (!File.Exists(resolvedPath))
        {
            throw new InvalidOperationException($"Client customer lookup ignore rules file '{resolvedPath}' was not found.");
        }

        try
        {
            string fileText = File.ReadAllText(resolvedPath);
            return LooksLikeJson(fileText)
                ? LoadJsonConfiguration(fileText)
                : LoadTextIgnoreRules(fileText);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or JsonException)
        {
            throw new InvalidOperationException(
                $"Client customer lookup ignore rules file '{resolvedPath}' could not be loaded.",
                ex);
        }
    }

    private static ComparisonRuleDefaults LoadTextIgnoreRules(string fileText) =>
        new ComparisonRuleDefaults(
            ignoreXmlNamespaces: true,
            ignoreRules: NormalizeIgnoreRules(fileText
                .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && !line.StartsWith("#", StringComparison.Ordinal))
                .Select(line => new IgnoreRuleDefinition(line))));

    private static ComparisonRuleDefaults LoadJsonConfiguration(string json)
    {
        ClientCustomerLookupComparisonConfigurationFile? configuration =
            JsonSerializer.Deserialize<ClientCustomerLookupComparisonConfigurationFile>(json, JsonOptions);

        if (configuration is null)
        {
            throw new InvalidOperationException("File does not contain a supported comparison configuration.");
        }

        ClientCustomerLookupComparisonGlobalSettings? globalSettings = configuration.GlobalSettings;
        return new ComparisonRuleDefaults(
            ignoreCollectionOrder: globalSettings?.IgnoreCollectionOrder ?? false,
            ignoreStringCase: globalSettings?.IgnoreStringCase ?? false,
            ignoreTrailingWhitespaceAtEnd: globalSettings?.IgnoreTrailingWhitespaceAtEnd ?? false,
            treatNullAndEmptyCollectionsAsEqual: globalSettings?.TreatNullAndEmptyCollectionsAsEqual ?? false,
            ignoreXmlNamespaces: true,
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

    private sealed record ClientCustomerLookupComparisonConfigurationFile(
        int SchemaVersion,
        ClientCustomerLookupComparisonGlobalSettings? GlobalSettings,
        IReadOnlyList<IgnoreRuleDefinition>? IgnoreRules);

    private sealed record ClientCustomerLookupComparisonGlobalSettings(
        bool IgnoreCollectionOrder,
        bool IgnoreStringCase,
        bool IgnoreTrailingWhitespaceAtEnd,
        bool TreatNullAndEmptyCollectionsAsEqual,
        bool IgnoreXmlNamespaces);
}
