namespace ParityBench.PluginSdk.Configuration;

/// <summary>
/// How a configuration field should be captured. The desktop app renders forms
/// from this metadata alone, which is what keeps client-specific UI code out of
/// the product.
/// </summary>
public enum PluginFieldKind
{
    String = 0,
    MultilineString = 1,
    Boolean = 2,
    Integer = 3,
    Uri = 4,
    Enumeration = 5,

    /// <summary>
    /// Captured masked and written to the secret store; the profile keeps only a
    /// <c>secret://</c> reference.
    /// </summary>
    Secret = 6,
}

/// <summary>
/// One configuration field a plugin needs from the operator.
/// </summary>
public sealed record PluginConfigurationField
{
    public PluginConfigurationField(
        string key,
        string label,
        PluginFieldKind kind = PluginFieldKind.String,
        bool isRequired = false,
        string? defaultValue = null,
        string? description = null,
        IEnumerable<string>? allowedValues = null)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Configuration field key must not be empty.", nameof(key));
        }

        if (string.IsNullOrWhiteSpace(label))
        {
            throw new ArgumentException("Configuration field label must not be empty.", nameof(label));
        }

        string[] copiedAllowedValues = (allowedValues ?? Array.Empty<string>()).ToArray();
        if (kind == PluginFieldKind.Enumeration && copiedAllowedValues.Length == 0)
        {
            throw new ArgumentException("Enumeration fields must declare allowed values.", nameof(allowedValues));
        }

        Key = key.Trim();
        Label = label.Trim();
        Kind = kind;
        IsRequired = isRequired;
        DefaultValue = string.IsNullOrWhiteSpace(defaultValue) ? null : defaultValue;
        Description = string.IsNullOrWhiteSpace(description) ? null : description;
        AllowedValues = copiedAllowedValues;
    }

    public string Key { get; }

    public string Label { get; }

    public PluginFieldKind Kind { get; }

    public bool IsRequired { get; }

    public string? DefaultValue { get; }

    public string? Description { get; }

    public IReadOnlyList<string> AllowedValues { get; }
}

/// <summary>
/// The configuration a step (or the plugin as a whole) exposes to the operator.
/// </summary>
public sealed record PluginConfigurationSchema
{
    public PluginConfigurationSchema(
        string ownerId,
        string displayName,
        IEnumerable<PluginConfigurationField> fields)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            throw new ArgumentException("Configuration schema owner id must not be empty.", nameof(ownerId));
        }

        OwnerId = ownerId.Trim();
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? OwnerId : displayName.Trim();
        Fields = (fields ?? Array.Empty<PluginConfigurationField>()).ToArray();
    }

    /// <summary>
    /// Gets the id this schema configures — a step id, or the plugin id for
    /// plugin-wide settings.
    /// </summary>
    public string OwnerId { get; }

    public string DisplayName { get; }

    public IReadOnlyList<PluginConfigurationField> Fields { get; }
}
