# ParityBench.PluginSdk

The only assembly a client plugin compiles against.

## Owns

- Plugin contracts: `IParityBenchPlugin`, `IPluginBuilder`, `IComparisonDefinition<T>`, `IComparisonConfigurator<T>`, `PluginManifest`.
- The middleware pipeline surface: `IComparisonMiddleware` (endpoint- and pair-scoped), `PipelinePhase`, `IPipelineContext` and its endpoint/pair specializations, `PipelineTransportResponse`, `PairComparisonResult`.
- Configuration and profile metadata: `PluginConfigurationSchema`/`PluginConfigurationField`, `IPipelineConfiguration`/`IStepConfiguration`, `PluginEnvironment`, `PluginProfileTemplate`.
- The neutral value types shared with plugins (moved here from Domain, namespaces unchanged): payload formats, comparison rules, ignore/mask/smart-ignore definitions, request/endpoint/artifact models, `ContractPayload`, `IContractPayloadSerializer`.

## Boundaries

- No dependency on Application, Engine, Infrastructure, or any host — only `System.Text.Json` and `Microsoft.Extensions.DependencyInjection.Abstractions`.
- Loaded from the default context and shared into every plugin's load context, so its types unify across the host and all plugins.
- Namespaces of the moved value types are unchanged, so `Domain` project-references this and nothing else in the repo had to change.

## Guide

See [Building a Plugin](../../Docs/Guides/building-a-plugin.md).
