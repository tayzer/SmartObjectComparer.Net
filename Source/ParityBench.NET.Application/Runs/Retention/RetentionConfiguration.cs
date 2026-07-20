using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using ParityBench.NET.Domain.Runs.Retention;

namespace ParityBench.NET.Application.Runs.Retention;

public sealed class RetentionConfiguration
{
    public const string SectionPath = "ParityBench:Retention";
    public const string PolicyVersionV1 = "v1";

    public RetentionMode Mode { get; init; } = RetentionMode.TrimmedEqualsAndIgnoredPaths;

    public NonSuccessRetentionOverride NonSuccessOverride { get; init; } = NonSuccessRetentionOverride.KeepBounded;

    public int NonSuccessDiagnosticRetentionWindowDays { get; init; } = 14;

    public long NonSuccessDiagnosticRetentionMaxBytesPerRun { get; init; } = 5368709120;

    public long NonSuccessDiagnosticRetentionMaxBytesWorkspace { get; init; } = 53687091200;

    public static RetentionConfiguration Default { get; } = new RetentionConfiguration();

    internal static void ValidateConfiguredValues(IConfigurationSection retentionSection)
    {
        ValidateEnumValue<RetentionMode>(retentionSection, nameof(Mode));
        ValidateEnumValue<NonSuccessRetentionOverride>(retentionSection, nameof(NonSuccessOverride));
    }

    private static void ValidateEnumValue<TEnum>(IConfigurationSection section, string key)
        where TEnum : struct, Enum
    {
        string? configured = section[key];
        if (string.IsNullOrWhiteSpace(configured))
        {
            return;
        }

        if (!Enum.TryParse(configured, ignoreCase: true, out TEnum parsed) || !Enum.IsDefined(parsed))
        {
            throw new InvalidOperationException(
                $"Configuration key '{SectionPath}:{key}' has invalid value '{configured}'.");
        }
    }
}

public sealed class RetentionConfigurationValidator : IValidateOptions<RetentionConfiguration>
{
    public ValidateOptionsResult Validate(string? name, RetentionConfiguration options)
    {
        List<string> failures = new List<string>();
        if (options.NonSuccessDiagnosticRetentionWindowDays <= 0)
        {
            failures.Add($"{RetentionConfiguration.SectionPath}:NonSuccessDiagnosticRetentionWindowDays must be greater than zero.");
        }

        if (options.NonSuccessDiagnosticRetentionMaxBytesPerRun <= 0)
        {
            failures.Add($"{RetentionConfiguration.SectionPath}:NonSuccessDiagnosticRetentionMaxBytesPerRun must be greater than zero.");
        }

        if (options.NonSuccessDiagnosticRetentionMaxBytesWorkspace <= 0)
        {
            failures.Add($"{RetentionConfiguration.SectionPath}:NonSuccessDiagnosticRetentionMaxBytesWorkspace must be greater than zero.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}

public static class RetentionConfigurationServiceCollectionExtensions
{
    public static IServiceCollection AddRetentionConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        IConfigurationSection retentionSection = configuration.GetSection(RetentionConfiguration.SectionPath);
        RetentionConfiguration.ValidateConfiguredValues(retentionSection);

        services.AddSingleton<IValidateOptions<RetentionConfiguration>, RetentionConfigurationValidator>();
        services
            .AddOptions<RetentionConfiguration>()
            .Bind(retentionSection)
            .ValidateOnStart();

        return services;
    }
}