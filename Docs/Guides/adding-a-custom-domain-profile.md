# Adding a Custom Domain Profile

A **contract profile** tells ParityBench.NET how to prepare requests for Endpoint A and Endpoint B, and how to normalize both responses into one canonical model for comparison. This is how you plug in your own API pair (SOAP vs JSON, v1 vs v2, whatever) without touching the comparison engine.

The reference implementation for everything below is [`Source/ParityBench.NET.ClientCustomerLookupExample`](../../Source/ParityBench.NET.ClientCustomerLookupExample) — a real, working profile (SOAP request to Endpoint A, JSON to Endpoint B, chained bearer-token auth). Read this guide alongside that project; it's the full version of every snippet here.

## 1. Define your models

You need four types: the Endpoint A request, the Endpoint B request, the Endpoint B response, and a canonical response type that both sides normalize into.

See [`ClientCustomerLookupModels.cs`](../../Source/ParityBench.NET.ClientCustomerLookupExample/ClientCustomerLookupModels.cs) for the shape: `ClientCustomerLookupSoapRequestEnvelope`, `ClientCustomerLookupJsonRequest`, `ClientCustomerLookupJsonResponse`, and the canonical `ClientCustomerLookupResponse`.

## 2. Write a mapping config

[Mapster](https://github.com/MapsterMapper/Mapster) does the type-to-type mapping. Create a static config method, following [`ClientCustomerLookupMapsterConfig.cs`](../../Source/ParityBench.NET.ClientCustomerLookupExample/ClientCustomerLookupMapsterConfig.cs):

```csharp
public static class MyProfileMapsterConfig
{
    public static TypeAdapterConfig CreateConfig()
    {
        TypeAdapterConfig config = new TypeAdapterConfig();
        config.NewConfig<MyEndpointARequest, MyEndpointBRequest>()
            .Map(dest => dest.SomeField, src => src.SomeOtherField);
        // ...
        return config;
    }
}
```

## 3. Build the profile

Use `ParityBench.NET.Infrastructure.ContractProfile<TEndpointARequest, TEndpointBRequest, TCanonicalResponse, TEndpointBResponse>` — a generic builder, not an interface you implement by hand:

```csharp
public static class MyProfileFactory
{
    public const string ResponseModelName = "MyResponse";
    public const string ProfileId = "my-org.my-profile.v1";

    public static IContractProfile Create(
        IContractPayloadSerializer serializer,
        TypeAdapterConfig mapsterConfig)
    {
        return new ContractProfile<
            MyEndpointARequest, MyEndpointBRequest,
            MyCanonicalResponse, MyEndpointBResponse>(
            serializer,
            ProfileId,
            ResponseModelName,
            request => request.Adapt<MyEndpointBRequest>(mapsterConfig),
            response => response.Adapt<MyCanonicalResponse>(mapsterConfig),
            supportedSourceRequestFormats: new[] { PayloadFormat.Xml },
            endpointBRequestFormat: PayloadFormat.Json,
            endpointBRequestContentType: "application/json",
            endpointBResponseFormat: PayloadFormat.Json,
            canonicalResponseFormat: PayloadFormat.Json,
            canonicalResponseContentType: "application/json");
    }
}
```

Constructor parameters worth knowing:

- `requestMapper` / `responseMapper` — the default A→B request and B→canonical response mappings.
- `requestPreparation` (optional) — a `Func<ContractRequestPreparationContext<TEndpointARequest>, CancellationToken, ValueTask<PreparedContractRequest>>` for anything beyond a plain field mapping (auth headers, token exchange, custom serialization). `ClientCustomerLookupProfileFactory.cs` uses this to inject a bearer token fetched via a chained token provider before building the Endpoint B request.
- `endpointAResponseNormalizer` (optional) — same idea, for Endpoint A's response → canonical, when it's not a straight deserialize-and-adapt.
- `defaultComparisonRules` / `defaultIgnoreRules` — a `ComparisonRuleDefaults` seeding default ignore rules (e.g. trace IDs, timestamps) so every run using this profile starts from a sane baseline.

## 4. Register endpoints and a preset

So your profile is reachable by name (`--preset <id>` on the CLI, or the equivalent picker in Web/Desktop), register an endpoint pair and a preset:

```csharp
public static class MyProfileDefaults
{
    public const string PresetId = "my-profile";

    public static void Register(
        IRequestComparisonEndpointRegistry endpointRegistry,
        IRequestComparisonPresetRegistry presetRegistry,
        IConfiguration configuration,
        string manualRunRoot)
    {
        Uri endpointA = new Uri(configuration["MyProfile:EndpointA"]!, UriKind.Absolute);
        Uri endpointB = new Uri(configuration["MyProfile:EndpointB"]!, UriKind.Absolute);

        endpointRegistry.Register(new EndpointOption("my-profile/a", "My Endpoint A", endpointA));
        endpointRegistry.Register(new EndpointOption("my-profile/b", "My Endpoint B", endpointB));

        presetRegistry.Register(new RequestComparisonPresetOption(
            PresetId,
            "My custom profile",
            Path.Combine(manualRunRoot, "my-profile"),
            endpointA,
            endpointB,
            MyProfileFactory.ResponseModelName,
            MyProfileFactory.ProfileId,
            new ComparisonOptions(),
            new RequestExecutionOptions()));
    }
}
```

## 5. Wire it into DI

Implement the two contributor interfaces (`Source/ParityBench.NET.Application/Requests/IResponseModelContributor.cs`, `Source/ParityBench.NET.Application/ContractProfiles/IContractProfileContributor.cs`) and expose one `AddXyz(...)` extension method, following [`ClientCustomerLookupExampleServiceCollectionExtensions.cs`](../../Source/ParityBench.NET.ClientCustomerLookupExample/ClientCustomerLookupExampleServiceCollectionExtensions.cs):

```csharp
public static class MyProfileServiceCollectionExtensions
{
    public static IServiceCollection AddMyProfile(
        this IServiceCollection services,
        IConfiguration configuration,
        IRequestComparisonEndpointRegistry endpointRegistry,
        IRequestComparisonPresetRegistry presetRegistry,
        string manualRunRoot)
    {
        services.AddSingleton(_ => MyProfileMapsterConfig.CreateConfig());
        MyProfileDefaults.Register(endpointRegistry, presetRegistry, configuration, manualRunRoot);

        services.AddSingleton<IResponseModelContributor, MyResponseModelContributor>();
        services.AddSingleton<IContractProfileContributor, MyProfileContributor>();
        return services;
    }

    private sealed class MyResponseModelContributor : IResponseModelContributor
    {
        public void Register(IResponseModelRegistry registry) =>
            registry.Register<MyCanonicalResponse>(MyProfileFactory.ResponseModelName);
    }

    private sealed class MyProfileContributor : IContractProfileContributor
    {
        public void Register(IContractProfileRegistry registry, IServiceProvider serviceProvider) =>
            registry.Register(MyProfileFactory.Create(
                serviceProvider.GetRequiredService<IContractPayloadSerializer>(),
                serviceProvider.GetRequiredService<TypeAdapterConfig>()));
    }
}
```

No assembly scanning happens — `IResponseModelContributor` and `IContractProfileContributor` are resolved via `serviceProvider.GetServices<T>()` when the registries are built (`Source/ParityBench.NET.Composition/WorkspaceServiceCollectionExtensions.cs`), so any contributor registered in the `IServiceCollection` *before* the provider is built gets picked up automatically. What does **not** happen automatically is calling your `AddMyProfile(...)` in the first place — see step 6.

## 6. Call it from the hosts you want it in

Each host explicitly opts in. For the CLI, that's a direct call in `Source/ParityBench.NET.Cli/CliApplication.cs` (search for `AddClientCustomerLookupExample` to see the existing call site and add a sibling call to `AddMyProfile(...)`). Do the same in `Source/ParityBench.NET.Web/Program.cs` and `Source/ParityBench.NET.Desktop/App.xaml.cs` if you want the profile available there too — there's no central registry file to edit instead.

## 7. Run it

```bash
dotnet run --project Source/ParityBench.NET.Cli/ParityBench.NET.Cli.csproj -- request --preset my-profile
```

If it resolves and runs, your profile is wired up correctly. From here, tune `defaultComparisonRules`/`defaultIgnoreRules` on the profile as you find noisy fields (timestamps, generated IDs) that shouldn't fail a comparison.
