# Building a Plugin for Different Contracts

A **plugin** teaches ParityBench.NET how to compare one API pair — how to build each
endpoint's request, how to project each response onto a shared in-memory type, and
what to compare — and it does so **without rebuilding the product**. A client installs
the app once, drops a plugin package into a `plugins/` folder, and selects it from a
saved run profile.

Use this guide when Endpoint A and Endpoint B **don't** share one contract — different
serialization formats, different field names or types, one side needing translation
onto the other. SOAP vs JSON is the case below; a v1-vs-v2 pair where a field was
renamed or retyped follows the same shape.

> **If both endpoints share one contract** — same format, same fields, same types,
> just different deployments — see [Building a Plugin](building-a-plugin.md) instead;
> it needs far less code.

> **Contrast with the old model.** Earlier, a client was a project inside the solution,
> referenced by every host and wired at compile time. A plugin is the opposite: it
> compiles only against `ParityBench.PluginSdk`, ships as a package, and is discovered
> at run time. See the
> [plugin-extensibility ADR](../Architecture/ADRs/2026-07-22-plugin-extensibility-and-worker-isolation.md)
> for why.

The reference implementation for everything below is
[`Source/ParityBench.ClientCustomerLookupPlugin`](../../Source/ParityBench.ClientCustomerLookupPlugin)
— a real, working plugin (SOAP request to Endpoint A, JSON to Endpoint B, chained
bearer-token auth). Read this guide alongside that project.

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
    <PackageReference Include="Mapster" />
  </ItemGroup>

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

Your own third-party dependencies (here, Mapster) are loaded in an **isolated
context**, so two plugins may depend on different versions of the same library
without conflict.

## 2. Write the manifest

Ship a `parity-plugin.json` next to your assembly. The host reads it **without loading
any plugin code**, so an incompatible or malformed package is rejected before its code
can run:

```json
{
  "id": "client.customer-lookup",
  "version": "1.0.0",
  "sdkVersion": 1,
  "entryAssembly": "ParityBench.ClientCustomerLookupPlugin.dll",
  "displayName": "Client Customer Lookup",
  "publisher": "Your Org"
}
```

## 3. Define your models

A plugin carries its own model types rather than referencing the product. You need the
source request type, the Endpoint B request/response types, and a **canonical** type
both endpoints are projected onto — that canonical type is what Compare-Net-Objects
diffs. See
[`ClientCustomerLookupModels.cs`](../../Source/ParityBench.ClientCustomerLookupPlugin/ClientCustomerLookupModels.cs).

## 4. Write the pipeline steps

Behaviour lives in **middleware**. Each step declares a `StepId`, a `Phase`, and an
`Order` (which only sorts steps *within* a phase). Endpoint-scoped steps
(`Input`…`Mapping`) implement `IEndpointComparisonMiddleware` and run once per endpoint
slot; pair-scoped steps (`Comparison`, `ResultProcessing`) implement
`IPairComparisonMiddleware`. To fail a pair deliberately, a step calls
`context.Fail(reason)` and doesn't call `next`.

A **Request-phase** step builds the outbound request. The reference does a two-hop
token exchange, maps the SOAP request to the endpoint's JSON contract, and attaches
auth headers — see
[`ClientCustomerLookupRequestMiddleware.cs`](../../Source/ParityBench.ClientCustomerLookupPlugin/ClientCustomerLookupRequestMiddleware.cs):

```csharp
public async ValueTask InvokeAsync(IEndpointPipelineContext context, PipelineDelegate next, CancellationToken ct)
{
    IStepConfiguration config = context.Configuration.ForStep(Id);
    if (context.Endpoint == EndpointSlot.B)
    {
        var token = await tokenExchange.GetFinalTokenAsync(/* ... */, ct);
        context.RequestBody = /* mapped + serialized JSON payload */;
        context.RequestHeaders["Authorization"] = $"Bearer {token.AccessToken}";
    }
    await next(ct);
}
```

A **Mapping-phase** step projects each persisted response onto the canonical type,
genuinely doing two different things per slot (unlike a same-contract plugin's mapping
step, which is one code path for both). It reads the response through
`context.OpenResponseArtifactAsync(...)` — a plugin never touches the artifact store
directly — and sets `context.ComparisonInstance`. The built-in mapping step then
persists that projection as the canonical artifact and Compare-Net-Objects diffs the
two. See
[`ClientCustomerLookupMappingMiddleware.cs`](../../Source/ParityBench.ClientCustomerLookupPlugin/ClientCustomerLookupMappingMiddleware.cs).

## 5. Write the entry point

Implement `IParityBenchPlugin`. Register run-scoped services on `builder.Services`
(they can inject the host services a plugin is allowed to see —
`IContractPayloadSerializer`, `HttpClient`), then register the comparison, middleware,
configuration schema, environments, and profile templates. See
[`ClientCustomerLookupPlugin.cs`](../../Source/ParityBench.ClientCustomerLookupPlugin/ClientCustomerLookupPlugin.cs):

```csharp
public void Configure(IPluginBuilder builder)
{
    builder.Services.AddSingleton(_ => MyMapsterConfig.CreateConfig());
    builder.Services.AddSingleton(p => new MyTokenExchange(p.GetRequiredService<HttpClient>()));

    builder
        .AddComparison(new MyComparisonDefinition())
        .AddMiddleware<MyRequestMiddleware>()
        .AddMiddleware<MyMappingMiddleware>()
        .AddConfigurationSchema(new PluginConfigurationSchema(
            MyRequestMiddleware.Id, "Token exchange", new[]
            {
                new PluginConfigurationField("primaryTokenUrl", "Primary token URL", PluginFieldKind.Uri, isRequired: true),
                new PluginConfigurationField("primaryTokenSubscriptionKey", "Primary key", PluginFieldKind.Secret, isRequired: true),
            }))
        .AddEnvironment(new PluginEnvironment("QA", endpointAUri, endpointBUri))
        .AddProfileTemplate(new PluginProfileTemplate("my-profile-qa", "My profile — QA", MyComparisonDefinition.Id, environmentName: "QA"));
}
```

`IComparisonDefinition<TComparison>` names the comparison, its canonical CLR type, the
endpoint contracts, the default/required step ids, and the `ComparisonRuleDefaults`
(known-noisy fields to ignore) every run using it should start from.

**Declare each side's contract rather than coding it.** Because the two slots differ,
each gets its own `ContractEndpointProfile` — and that profile is where the outbound
`Content-Type` and the contract's fixed headers belong:

```csharp
public ContractEndpointProfile EndpointA { get; } = new ContractEndpointProfile(
    PayloadFormat.Xml,
    "text/xml",
    PayloadFormat.Xml,
    supportedSourceRequestFormats: new[] { PayloadFormat.Xml },
    requestHeaders: new Dictionary<string, string> { ["SOAPAction"] = "urn:CustomerLookup" });

public ContractEndpointProfile EndpointB { get; } = new ContractEndpointProfile(
    PayloadFormat.Json,
    "application/json",
    PayloadFormat.Json);
```

Declaring `"text/xml"` matters: otherwise the content type is inferred from the request
file's extension (`.xml` → `application/xml`), and a SOAP endpoint that wants `text/xml`
answers that with **415 Unsupported Media Type**. Pass `null` to keep each request
file's own content type.

A request step is then only for what has to be computed at run time — the token
exchange and the body mapping above — not for static headers. Steps run after the
declarative merge, so they still override anything declared here. A `Content-Type` key
in `requestHeaders` throws; use `requestContentType`.

**Secrets.** A `PluginFieldKind.Secret` field is captured masked and stored as a
`secret://` reference in the profile; the value is resolved from the secret store only
at run start and arrives already-resolved in `IStepConfiguration.GetRequiredString(...)`.
Your plugin never sees the reference and never handles the value at rest.

## 6. Install and run

Build the package and drop its output folder into a `plugins/` directory (next to the
app, or under the workspace).

**During development**, automate that copy: set `<PbPluginPackageId>your.plugin.id</PbPluginPackageId>`
and import the shared packaging target so every Debug build republishes the package
into the shared workspace's `plugins/` folder — any host then picks up your latest
build with no manual copy:

```xml
<PropertyGroup>
  <PbPluginPackageId>client.customer-lookup</PbPluginPackageId>
</PropertyGroup>
<Import Project="..\..\build\ParityBench.Plugin.targets" />
```

It runs for Debug builds only (off for Release, CI, and non-Windows); opt out with
`-p:PbInstallPluginToWorkspace=false`, or retarget with `-p:PbWorkspacePluginsDir=<path>`.

**To ship the plugin inside a published app** — so testers get it installed by copying
the build — list it as a `PbBundledPlugin` in the host project; see
[Building a Plugin §6](building-a-plugin.md#6-install-and-run).

Then create a run profile that selects the plugin and run:

```bash
dotnet run --project Source/ParityBench.NET.Cli -- request --run-profile my-profile-qa
```

A run profile is JSON under `<workspace>/config/profiles/`:

```jsonc
{
  "schemaVersion": 2,
  "id": "my-profile-qa",
  // No "version" means the highest installed one; add it only to pin a build.
  "plugin": { "id": "client.customer-lookup" },
  "comparisonId": "client.customer-lookup.soap-vs-json",
  "environment": "QA",
  "endpoints": { "a": "https://qa…/soap", "b": "https://qa…/json" },
  "stepConfiguration": {
    "client.customer-lookup.request": { "primaryTokenSubscriptionKey": "secret://client/qa-primary-key" }
  },
  "comparison": { /* ignore/mask rules, maxDifferences */ },
  "input": { "requestDirectory": "…" }
}
```

Profiles reference **stable logical ids only** — plugin id, comparison id, step ids,
`secret://` names — never .NET type names, so a profile keeps working across plugin
rebuilds and versions.

## 7. Run out of process (optional)

To execute runs in a separate worker process — so a plugin crash, hang, or dependency
conflict fails the run instead of the app — set `Worker:Enabled=true` in the host's
configuration. The worker loads plugins in an isolated context, writes artifacts to
the same workspace, and streams progress and the result back over a named pipe. This
is identical regardless of contract shape.

## See also

- [Building a Plugin](building-a-plugin.md) — the leaner path when both endpoints share one contract
- [Baseline vs Live](baseline-vs-live.md) — capturing a baseline and replaying it against a later version
- [Comparison Rules](comparison-rules.md) — ignore rules, smart ignores, masking beyond `DefaultComparisonRules`
- [Plugin-extensibility ADR](../Architecture/ADRs/2026-07-22-plugin-extensibility-and-worker-isolation.md) — why the plugin model replaced compile-time contract profiles
