using System.Globalization;

using ParityBench.PluginSdk.Configuration;

namespace ParityBench.NET.Engine.Pipeline;

/// <summary>
/// Profile-supplied step configuration, with secret references already resolved
/// to values by the host before the pipeline is built.
/// </summary>
public sealed class PipelineConfiguration : IPipelineConfiguration
{
    private static readonly IStepConfiguration Empty = new StepConfiguration(
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    private readonly IReadOnlyDictionary<string, IStepConfiguration> stepConfigurations;

    public PipelineConfiguration(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? stepConfiguration = null,
        string? environmentName = null)
    {
        EnvironmentName = string.IsNullOrWhiteSpace(environmentName) ? null : environmentName.Trim();
        stepConfigurations = (stepConfiguration ?? new Dictionary<string, IReadOnlyDictionary<string, string>>())
            .ToDictionary(
                entry => entry.Key,
                entry => (IStepConfiguration)new StepConfiguration(entry.Value),
                StringComparer.OrdinalIgnoreCase);
    }

    public string? EnvironmentName { get; }

    public IStepConfiguration ForStep(string stepId) =>
        !string.IsNullOrWhiteSpace(stepId) && stepConfigurations.TryGetValue(stepId, out IStepConfiguration? configuration)
            ? configuration
            : Empty;

    private sealed class StepConfiguration : IStepConfiguration
    {
        private readonly IReadOnlyDictionary<string, string> values;

        public StepConfiguration(IReadOnlyDictionary<string, string> values) =>
            this.values = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);

        public bool TryGetValue(string key, out string? value)
        {
            if (!string.IsNullOrWhiteSpace(key) && values.TryGetValue(key, out string? found))
            {
                value = found;
                return true;
            }

            value = null;
            return false;
        }

        public string? GetString(string key, string? defaultValue = null) =>
            TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value) ? value : defaultValue;

        public string GetRequiredString(string key) =>
            GetString(key) ?? throw new InvalidOperationException($"Required configuration value '{key}' is missing.");

        public bool GetBoolean(string key, bool defaultValue = false) =>
            GetString(key) is string value && bool.TryParse(value, out bool parsed) ? parsed : defaultValue;

        public int GetInt32(string key, int defaultValue = 0) =>
            GetString(key) is string value && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                ? parsed
                : defaultValue;

        public Uri GetRequiredUri(string key)
        {
            string value = GetRequiredString(key);
            return Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
                ? uri
                : throw new InvalidOperationException($"Configuration value '{key}' is not an absolute URI.");
        }
    }
}
