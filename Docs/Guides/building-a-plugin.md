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

## 4. Declare the comparison

One contract means one declaration. `SameContractComparison<TResponse>` takes it and
serves both endpoint slots from it, so there is no `EndpointA`/`EndpointB` pair to
keep in sync:

```csharp
using ParityBench.NET.Application.ContractProfiles;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.ContractProfiles;
using ParityBench.PluginSdk.Comparisons;

new SameContractComparison<OrderLookupResponse>(
    "acme.order-lookup.v1-vs-v2",
    "Order Lookup — v1 vs v2",
    new ContractEndpointProfile(
        PayloadFormat.Json,
        "application/json",
        PayloadFormat.Json,
        requestHeaders: new Dictionary<string, string> { ["Accept"] = "application/json" }),
    new ComparisonRuleDefaults(
        ignoreRules: new[]
        {
            new IgnoreRuleDefinition("traceId"),
            new IgnoreRuleDefinition("lines", ignoreCompletely: false, ignoreCollectionOrder: true),
        }));
```

**`requestContentType`** (the second argument) is what goes out as `Content-Type`. It
matters more than it looks: without it, the content type is inferred from the request
file's extension — `.xml` becomes `application/xml`, `.txt` becomes `text/plain` — and
an endpoint that wants `text/xml` answers a inferred `application/xml` with **415
Unsupported Media Type**. Declare what the endpoint actually accepts. Leave it `null`
to keep each request file's own, which is what a batch of mixed formats wants.

**`requestHeaders`** are the headers this contract always sends — a `SOAPAction`, an
`Accept`. They sit at the bottom of the header precedence, so environment, profile and
CLI headers still override them, as does any request middleware. Declare a
`Content-Type` here and the constructor throws: that is `requestContentType`'s job.

Put version-drift-prone fields — trace ids, timestamps, anything that legitimately
differs release to release — into `ComparisonRuleDefaults` so every run using this
comparison starts clean.

## 5. Write the entry point

Implement `IParityBenchPlugin`, register the comparison, and ship a `PluginEnvironment`
per version pair you test against. For a same-contract comparison **that is the entire
plugin** — no middleware, no step ids, no configuration schema:

```csharp
using ParityBench.NET.Application.ContractProfiles;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.ContractProfiles;
using ParityBench.PluginSdk.Comparisons;
using ParityBench.PluginSdk.Plugin;
using ParityBench.PluginSdk.Profiles;

namespace Acme.OrderLookupPlugin;

public sealed class OrderLookupPlugin : IParityBenchPlugin
{
    public const string Id = "acme.order-lookup";
    public const string ComparisonId = "acme.order-lookup.v1-vs-v2";

    public string PluginId => Id;

    public void Configure(IPluginBuilder builder) => builder
        .AddComparison(new SameContractComparison<OrderLookupResponse>(
            ComparisonId,
            "Order Lookup — v1 vs v2",
            new ContractEndpointProfile(
                PayloadFormat.Json,
                "application/json",
                PayloadFormat.Json,
                requestHeaders: new Dictionary<string, string> { ["Accept"] = "application/json" }),
            new ComparisonRuleDefaults(
                ignoreRules: new[] { new IgnoreRuleDefinition("traceId") })))
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
```

No mapping step is needed: when nothing has produced a comparison instance, the
built-in mapping phase deserializes each persisted response straight into
`OrderLookupResponse`. A translating plugin needs a mapping step because its two slots
genuinely produce different shapes; yours don't.

Because both endpoints share one contract, you typically ship **one environment per
comparison target** (QA, ST, pre-prod) rather than a request/response-format split.

### Per-environment headers

If the two deployments need different *static* headers — a gateway key that varies by
environment rather than by contract — put them on the environment, not in code:

```csharp
.AddEnvironment(new PluginEnvironment(
    "QA",
    new Uri("https://qa-v1.example.test/orders"),
    new Uri("https://qa-v2.example.test/orders"),
    endpointAHeaders: new Dictionary<string, string> { ["X-Api-Version"] = "1" },
    endpointBHeaders: new Dictionary<string, string> { ["X-Api-Version"] = "2" }))
```

These seed the materialised run profile's `endpointAHeaders`/`endpointBHeaders`, which
the operator can then edit — the plugin suggests, the saved profile decides.

### When you still need middleware

Reach for a request step only when a header's **value has to be computed at run time**
— a token exchange, a request signature — or when it comes from a secret:

```csharp
using ParityBench.PluginSdk.Configuration;
using ParityBench.PluginSdk.Pipeline;

namespace Acme.OrderLookupPlugin;

public sealed class OrderLookupAuthMiddleware : IEndpointComparisonMiddleware
{
    public const string Id = "acme.order-lookup.auth";

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

Register it with `.AddMiddleware<OrderLookupAuthMiddleware>()`, an
`.AddConfigurationSchema(...)` describing its fields, and pass its id in the
comparison's `defaultStepIds`. Request steps run after the declarative header merge, so
they override anything declared above.

**Secrets.** A `PluginFieldKind.Secret` field is captured masked and stored as a
`secret://` reference in the profile; the value is resolved from the secret store only
at run start and arrives already-resolved in `IStepConfiguration.GetRequiredString(...)`.
Your plugin never sees the reference and never handles the value at rest. This is the
main reason a same-contract plugin ends up with a request step at all.

## 6. Install and run

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

**Shipping the plugin with the app.** To hand testers a build that already has the
plugin installed, bundle it into the host's publish output instead of asking each of
them to copy a folder. The host imports `build/ParityBench.BundledPlugins.targets` and
lists the packages it ships (see `Source/ParityBench.NET.Desktop/ParityBench.NET.Desktop.csproj`):

```xml
<Import Project="..\..\build\ParityBench.BundledPlugins.targets" />

<ItemGroup>
  <PbBundledPlugin Include="..\..\Plugins\Acme.OrderLookup\Acme.OrderLookup.csproj"
                   PackageId="acme.order-lookup" />
</ItemGroup>
```

`dotnet publish` then publishes each listed plugin into `plugins\<PackageId>\` beneath
the app, which is one of the scanned directories, so the plugin is installed for anyone
who copies the published output. `PackageId` must match the manifest `id`. Opt out for
one publish with `-p:PbBundlePlugins=false`.

A bundled plugin and a workspace copy of the same plugin are both discovered, and the
**higher version wins** — so bump the plugin version when you ship a new build, or have
testers delete stale copies under `<workspace>\plugins\`.

Then run the seeded profile:

```bash
dotnet run --project Source/ParityBench.NET.Cli -- request --run-profile order-lookup-qa
```

A run profile is JSON under `<workspace>/config/profiles/`:

```jsonc
{
  "schemaVersion": 2,
  "id": "order-lookup-qa",
  // Omit "version" (the default) to run the highest installed version of the plugin,
  // so an upgraded package is picked up with no profile change. Set it only to pin
  // the profile to one build — a pinned version that is not installed fails the run.
  "plugin": { "id": "acme.order-lookup" },
  "comparisonId": "acme.order-lookup.v1-vs-v2",
  "environment": "QA",
  "endpoints": { "a": "https://qa-v1…/orders", "b": "https://qa-v2…/orders" },
  "endpointAHeaders": { "X-Api-Version": "1" },
  "endpointBHeaders": { "X-Api-Version": "2" },
  "stepConfiguration": {
    // Only present if you registered a middleware that reads configuration.
    "acme.order-lookup.auth": { "endpointBApiKey": "secret://acme/qa-v2-api-key" }
  },
  "comparison": { /* ignore/mask rules, maxDifferences */ },
  "input": { "requestDirectory": "…" },
  // Optional. Omit to follow ParityBench:Retention:Mode; set it to keep full raw
  // responses for this profile's runs only. See Retention and Workspace.
  "report": { "retentionMode": "TrimmedEquals" }
}
```

`endpointAHeaders`/`endpointBHeaders` override whatever the comparison declares in
`requestHeaders`, so an operator can adjust headers per profile without a plugin
rebuild.

Profiles reference **stable logical ids only** — plugin id, comparison id, step ids,
`secret://` names — never .NET type names, so a profile keeps working across plugin
rebuilds and versions.

Version pinning follows the same rule: a profile names no version by default and runs
the highest installed one. Pin a version (in the Plugin version picker on the Profiles
tab, or by adding `"version"` above) only when a profile must stay on one build; the
run fails with `version X is not installed` once that package is replaced. Profiles
written under `schemaVersion: 1` are read as unpinned, because that schema stamped the
installed version into every seeded profile.

**Running out of process (optional).** To execute runs in a separate worker process —
so a plugin crash, hang, or dependency conflict fails the run instead of the app — set
`Worker:Enabled=true` in the host's configuration. This is identical for either
contract shape; see
[Building a Plugin for Different Contracts §7](building-a-different-contract-plugin.md#7-run-out-of-process-optional)
for detail.

## 7. Pairing with Baseline vs Live

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
