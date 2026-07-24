namespace ParityBench.PluginSdk.Profiles;

/// <summary>
/// A named environment a plugin ships (ST, QA, ...). Endpoint hosts change per
/// environment; everything else about the comparison stays the same.
/// </summary>
public sealed record PluginEnvironment
{
    public PluginEnvironment(
        string name,
        Uri endpointA,
        Uri endpointB,
        IReadOnlyDictionary<string, string>? endpointAHeaders = null,
        IReadOnlyDictionary<string, string>? endpointBHeaders = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Environment name must not be empty.", nameof(name));
        }

        ArgumentNullException.ThrowIfNull(endpointA);
        ArgumentNullException.ThrowIfNull(endpointB);

        Name = name.Trim();
        EndpointA = endpointA;
        EndpointB = endpointB;
        EndpointAHeaders = endpointAHeaders;
        EndpointBHeaders = endpointBHeaders;
    }

    public string Name { get; }

    public Uri EndpointA { get; }

    public Uri EndpointB { get; }

    public IReadOnlyDictionary<string, string>? EndpointAHeaders { get; }

    public IReadOnlyDictionary<string, string>? EndpointBHeaders { get; }
}

/// <summary>
/// A starting point for a run profile. The catalog materialises templates into
/// real, editable profile files when a plugin is installed — the plugin suggests,
/// the saved profile decides.
/// </summary>
public sealed record PluginProfileTemplate
{
    public PluginProfileTemplate(
        string templateId,
        string displayName,
        string comparisonId,
        string? environmentName = null,
        string? requestDirectory = null,
        IEnumerable<string>? enabledStepIds = null,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? stepConfiguration = null)
    {
        if (string.IsNullOrWhiteSpace(templateId))
        {
            throw new ArgumentException("Template id must not be empty.", nameof(templateId));
        }

        if (string.IsNullOrWhiteSpace(comparisonId))
        {
            throw new ArgumentException("Template comparison id must not be empty.", nameof(comparisonId));
        }

        TemplateId = templateId.Trim();
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? TemplateId : displayName.Trim();
        ComparisonId = comparisonId.Trim();
        EnvironmentName = string.IsNullOrWhiteSpace(environmentName) ? null : environmentName.Trim();
        RequestDirectory = string.IsNullOrWhiteSpace(requestDirectory) ? null : requestDirectory;
        EnabledStepIds = (enabledStepIds ?? Array.Empty<string>()).ToArray();
        StepConfiguration = stepConfiguration;
    }

    public string TemplateId { get; }

    public string DisplayName { get; }

    public string ComparisonId { get; }

    public string? EnvironmentName { get; }

    public string? RequestDirectory { get; }

    public IReadOnlyList<string> EnabledStepIds { get; }

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? StepConfiguration { get; }
}
