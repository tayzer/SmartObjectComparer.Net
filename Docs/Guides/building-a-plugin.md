# Building a Plugin

A **plugin** teaches ParityBench.NET how to compare one API pair — how to build each
endpoint's request, how to project each response onto a shared in-memory type, and
what to compare — and it does so **without rebuilding the product**. A client installs
the app once, drops a plugin package into a `plugins/` folder, and selects it from a
saved run profile.

Use this guide when Endpoint A and Endpoint B are **two deployments of the same API** —
different versions, builds, or environments — and accept and return the identical
request/response shape. This is the plugin shape with the least code: one model set,
no translation step.

> **If your two endpoints don't share one contract** — different formats, different
> field names, one side needs translating onto the other — see
> [Building a Plugin for Different Contracts](building-a-different-contract-plugin.md)
> instead.

> **Contrast with the old model.** Earlier, a client was a project inside the solution,
> referenced by every host and wired at compile time. A plugin is the opposite: it
> compiles only against `ParityBench.PluginSdk`, ships as a package, and is discovered
> at run time. See the
> [plugin-extensibility ADR](../Architecture/ADRs/2026-07-22-plugin-extensibility-and-worker-isolation.md)
> for why.

## When this applies

- Endpoint A and Endpoint B are the **same API**, at different versions, builds, or
  deployments.
- Both accept the same request payload and return the same response payload — same
  serialization format, same field names, same types.
- Any difference between "A" and "B" is operational (host, API key, auth token) rather
  than structural (field shapes, message format).

If the two sides have drifted even slightly — a renamed field, a new required
property, one side moving from XML to JSON — use
[Building a Plugin for Different Contracts](building-a-different-contract-plugin.md)
instead: give each side its own type and a small mapper onto one shared canonical type.

## 1. Create the project

A plugin is a class library that references **only** the SDK and whatever third-party
packages it needs. Mark the SDK reference non-runtime so the shared SDK assembly is
never copied into your package, and enable dynamic loading:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <EnableDynamicLoading>true</EnableDynamicLoading>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\ParityBench.PluginSdk\ParityBench.PluginSdk.csproj">
      <Private>false</Private>
      <ExcludeAssets>runtime</ExcludeAssets>
    </ProjectReference>
  </ItemGroup>

  <ItemGroup>
    <None Update="parity-plugin.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

A same-contract plugin usually needs no third-party mapping library — there's nothing
to map.

## 2. Write the manifest

Ship a `parity-plugin.json` next to your assembly. The host reads it **without loading
any plugin code**, so an incompatible or malformed package is rejected before its code
can run:

```json
{
  "id": "acme.order-lookup",
  "version": "1.0.0",
  "sdkVersion": 1,
  "entryAssembly": "Acme.OrderLookupPlugin.dll",
  "displayName": "Order Lookup",
  "publisher": "Acme"
}
```

## 3. One model set

A plugin carries its own model types rather than referencing the product. Because
both endpoints speak the same contract, you need exactly **one** type per message —
no separate canonical type to translate onto:

```csharp
namespace Acme.OrderLookupPlugin;

public sealed class OrderLookupRequest
{
    public string OrderId { get; init; } = string.Empty;
    public string CorrelationId { get; init; } = string.Empty;
}

public sealed class OrderLookupResponse
{
    public string OrderId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string TraceId { get; init; } = string.Empty;
    public decimal Total { get; init; }
    public OrderLine[] Lines { get; init; } = Array.Empty<OrderLine>();
}

public sealed class OrderLine
{
    public string Sku { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
}
```

`OrderLookupResponse` **is** the canonical type Compare-Net-Objects diffs. There is no
`Adapt<T>()` step and nothing to translate.

## 4. Write the pipeline steps

Behaviour lives in **middleware**. Each step declares a `StepId`, a `Phase`, and an
`Order` (which only sorts steps *within* a phase). Endpoint-scoped steps
(`Input`…`Mapping`) implement `IEndpointComparisonMiddleware` and run once per endpoint
slot; pair-scoped steps (`Comparison`, `ResultProcessing`) implement
`IPairComparisonMiddleware`. To fail a pair deliberately, a step calls
`context.Fail(reason)` and doesn't call `next`.

**Request middleware** builds the outbound request. If both endpoints take the same
request body, the simplest version does nothing to `context.RequestBody` at all — the
source request the operator supplied goes out unchanged to both slots. You only need a
request step if the two versions need different auth:

```csharp
using ParityBench.PluginSdk.Configuration;
using ParityBench.PluginSdk.Pipeline;
using ParityBench.PluginSdk.Requests;

namespace Acme.OrderLookupPlugin;

public sealed class OrderLookupRequestMiddleware : IEndpointComparisonMiddleware
{
    public const string Id = "acme.order-lookup.request";

    public string StepId => Id;
    public PipelinePhase Phase => PipelinePhase.Request;
    public int Order => 0;

    public async ValueTask InvokeAsync(
        IEndpointPipelineContext context,
        PipelineDelegate next,
        CancellationToken cancellationToken)
    {
        IStepConfiguration config = context.Configuration.ForStep(Id);
        string keyName = context.Endpoint == EndpointSlot.A ? "endpointAApiKey" : "endpointBApiKey";
        context.RequestHeaders["Authorization"] = $"Bearer {config.GetRequiredString(keyName)}";
        await next(cancellationToken).ConfigureAwait(false);
    }
}
```

If both versions sit behind the same gateway and take identical auth too, you don't
need a request middleware at all — omit it, and don't list its id in `DefaultStepIds`.

**Mapping middleware** projects the persisted response onto the comparison type. Both
slots produce the same CLR type, so this is a straight deserialize with no
per-endpoint branch — the same code path runs for `EndpointSlot.A` and
`EndpointSlot.B`, unlike a translating plugin's mapping step, which genuinely does two
different things per slot. It reads the response through
`context.OpenResponseArtifactAsync(...)` — a plugin never touches the artifact store
directly — and sets `context.ComparisonInstance`:

```csharp
using ParityBench.NET.Application.ContractProfiles;
using ParityBench.NET.Domain.ContractProfiles;
using ParityBench.PluginSdk.Pipeline;

namespace Acme.OrderLookupPlugin;

public sealed class OrderLookupMappingMiddleware : IEndpointComparisonMiddleware
{
    public const string Id = "acme.order-lookup.mapping";

    private readonly IContractPayloadSerializer serializer;

    public OrderLookupMappingMiddleware(IContractPayloadSerializer serializer) => this.serializer = serializer;

    public string StepId => Id;
    public PipelinePhase Phase => PipelinePhase.Mapping;
    public int Order => 0;

    public async ValueTask InvokeAsync(
        IEndpointPipelineContext context,
        PipelineDelegate next,
        CancellationToken cancellationToken)
    {
        await using Stream body = await context.OpenResponseArtifactAsync(cancellationToken).ConfigureAwait(false);
        context.ComparisonInstance = await serializer
            .DeserializeAsync(typeof(OrderLookupResponse), body, PayloadFormat.Json, ignoreXmlNamespaces: false, cancellationToken)
            .ConfigureAwait(false);
        await next(cancellationToken).ConfigureAwait(false);
    }
}
```

## 5. Comparison definition

`EndpointA` and `EndpointB` use the same `ContractEndpointProfile` shape (same format
both sides). Put your version-drift-prone fields — trace ids, timestamps, anything
that legitimately differs release to release — into `DefaultComparisonRules` so every
run using this comparison starts clean:

```csharp
using ParityBench.NET.Application.ContractProfiles;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.ContractProfiles;
using ParityBench.PluginSdk.Comparisons;

namespace Acme.OrderLookupPlugin;

public sealed class OrderLookupComparisonDefinition : IComparisonDefinition<OrderLookupResponse>
{
    private static readonly ComparisonRuleDefaults Defaults = new ComparisonRuleDefaults(
        ignoreRules: new[]
        {
            new IgnoreRuleDefinition("traceId"),
            new IgnoreRuleDefinition("lines", ignoreCompletely: false, ignoreCollectionOrder: true),
        });

    public string ComparisonId => "acme.order-lookup.v1-vs-v2";
    public string DisplayName => "Order Lookup — v1 vs v2";
    public Type ComparisonType => typeof(OrderLookupResponse);

    public ContractEndpointProfile EndpointA { get; } = new ContractEndpointProfile(
        PayloadFormat.Json, "application/json", PayloadFormat.Json);

    public ContractEndpointProfile EndpointB { get; } = new ContractEndpointProfile(
        PayloadFormat.Json, "application/json", PayloadFormat.Json);

    public IReadOnlyList<string> DefaultStepIds { get; } = new[]
    {
        OrderLookupRequestMiddleware.Id,
        OrderLookupMappingMiddleware.Id,
    };

    public IReadOnlyList<string> RequiredStepIds { get; } = new[] { OrderLookupMappingMiddleware.Id };

    public ComparisonRuleDefaults DefaultComparisonRules => Defaults;
}
```

`IComparisonDefinition<TComparison>` names the comparison, its canonical CLR type, the
endpoint contracts, the default/required step ids, and the `ComparisonRuleDefaults`
every run using it should start from. `RequiredStepIds` lists only the mapping step
here — a profile is free to disable the request middleware (e.g. if a given
environment pair needs no per-slot auth), but it can never disable the step that
produces `ComparisonInstance`.

## 6. Write the entry point

Implement `IParityBenchPlugin`. Register run-scoped services on `builder.Services`
(they can inject the host services a plugin is allowed to see —
`IContractPayloadSerializer`, `HttpClient`), then register the comparison, middleware,
configuration schema, environments, and profile templates — one `PluginEnvironment`
per version pair you test against:

```csharp
using Microsoft.Extensions.DependencyInjection;

using ParityBench.PluginSdk.Configuration;
using ParityBench.PluginSdk.Plugin;
using ParityBench.PluginSdk.Profiles;

namespace Acme.OrderLookupPlugin;

public sealed class OrderLookupPlugin : IParityBenchPlugin
{
    public const string Id = "acme.order-lookup";
    public const string ComparisonId = "acme.order-lookup.v1-vs-v2";

    public string PluginId => Id;

    public void Configure(IPluginBuilder builder)
    {
        builder
            .AddComparison(new OrderLookupComparisonDefinition())
            .AddMiddleware<OrderLookupRequestMiddleware>()
            .AddMiddleware<OrderLookupMappingMiddleware>()
            .AddConfigurationSchema(new PluginConfigurationSchema(
                OrderLookupRequestMiddleware.Id,
                "API keys",
                new[]
                {
                    new PluginConfigurationField("endpointAApiKey", "Endpoint A API key", PluginFieldKind.Secret, isRequired: true),
                    new PluginConfigurationField("endpointBApiKey", "Endpoint B API key", PluginFieldKind.Secret, isRequired: true),
                }))
            .AddEnvironment(new PluginEnvironment(
                "QA",
                new Uri("https://qa-v1.example.test/orders"),
                new Uri("https://qa-v2.example.test/orders")))
            .AddProfileTemplate(new PluginProfileTemplate(
                "order-lookup-qa",
                "Order Lookup — QA (v1 vs v2)",
                ComparisonId,
                environmentName: "QA"));
    }
}
```

Because both endpoints share one contract, you typically ship **one environment per
comparison target** (QA, ST, pre-prod) rather than a request/response-format split.

**Secrets.** A `PluginFieldKind.Secret` field, like the two API keys above, is captured
masked and stored as a `secret://` reference in the profile; the value is resolved
from the secret store only at run start and arrives already-resolved in
`IStepConfiguration.GetRequiredString(...)`. Your plugin never sees the reference and
never handles the value at rest.

## 7. Install and run

Build the package and drop its output folder into a `plugins/` directory (next to the
app, or under the workspace).

**During development**, automate that copy: set `<PbPluginPackageId>your.plugin.id</PbPluginPackageId>`
and import the shared packaging target so every Debug build republishes the package
into the shared workspace's `plugins/` folder — any host then picks up your latest
build with no manual copy:

```xml
<PropertyGroup>
  <PbPluginPackageId>acme.order-lookup</PbPluginPackageId>
</PropertyGroup>
<Import Project="..\..\build\ParityBench.Plugin.targets" />
```

It runs for Debug builds only (off for Release, CI, and non-Windows); opt out with
`-p:PbInstallPluginToWorkspace=false`, or retarget with `-p:PbWorkspacePluginsDir=<path>`.

Then run the seeded profile:

```bash
dotnet run --project Source/ParityBench.NET.Cli -- request --run-profile order-lookup-qa
```

A run profile is JSON under `<workspace>/config/profiles/`:

```jsonc
{
  "schemaVersion": 1,
  "id": "order-lookup-qa",
  "plugin": { "id": "acme.order-lookup", "version": "1.0.0" },
  "comparisonId": "acme.order-lookup.v1-vs-v2",
  "environment": "QA",
  "endpoints": { "a": "https://qa-v1…/orders", "b": "https://qa-v2…/orders" },
  "stepConfiguration": {
    "acme.order-lookup.request": { "endpointBApiKey": "secret://acme/qa-v2-api-key" }
  },
  "comparison": { /* ignore/mask rules, maxDifferences */ },
  "input": { "requestDirectory": "…" }
}
```

Profiles reference **stable logical ids only** — plugin id, comparison id, step ids,
`secret://` names — never .NET type names, so a profile keeps working across plugin
rebuilds and versions.

**Running out of process (optional).** To execute runs in a separate worker process —
so a plugin crash, hang, or dependency conflict fails the run instead of the app — set
`Worker:Enabled=true` in the host's configuration. This is identical for either
contract shape; see
[Building a Plugin for Different Contracts §7](building-a-different-contract-plugin.md#7-run-out-of-process-optional)
for detail.

## 8. Pairing with Baseline vs Live

Same-contract comparisons are the common case for
[Baseline vs Live](baseline-vs-live.md): because both sides already produce the same
canonical type, a baseline captured against v1 replays cleanly against v2's live
endpoint with no plugin changes. Capture once before v1 is decommissioned:

```bash
ParityBench.NET.Cli request ./requests --run-profile order-lookup-qa --capture-baseline "order-lookup-v1"
```

Then replay against v2 once it ships, with no request to v1 at all:

```bash
ParityBench.NET.Cli request --run-profile order-lookup-qa --baseline order-lookup-v1@1 --endpoint-b https://v2.example.test/orders
```

Reach for this whenever v1 and v2 are never both running at the same time. If both
versions are live together (blue/green, canary), use a normal live-vs-live run instead
— no baseline needed.

## See also

- [Building a Plugin for Different Contracts](building-a-different-contract-plugin.md) — when Endpoint A and Endpoint B don't share one contract
- [Baseline vs Live](baseline-vs-live.md) — capturing a baseline and replaying it against a later version
- [Comparison Rules](comparison-rules.md) — ignore rules, smart ignores, masking beyond `DefaultComparisonRules`
