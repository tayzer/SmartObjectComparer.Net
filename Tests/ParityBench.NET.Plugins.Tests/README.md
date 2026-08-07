# ParityBench.NET.Plugins.Tests

Tests for host-side plugin loading, metadata, and profile bootstrapping.

## Covers

- **`PluginLoadingTests`** — manifest discovery and validation before any plugin code runs, entry-assembly resolution, load into a collectible `AssemblyLoadContext`, and rejection of malformed or SDK-incompatible packages.
- **`PluginMetadataProviderTests`** — the catalog view the UI reads: installed plugins, their comparisons, configuration schemas, and comparison definition lookup (`ResolveComparisonDefinitionAsync`), all by reflection without instantiating plugin code.
- **`PluginProfileBootstrapperTests`** — seeding run profiles from installed plugins' profile templates, including not overwriting a profile the user has edited.

## Boundaries

- Loads plugins from disk the way the product does, using the `Tests/Fixtures/ParityBench.TestPlugin` package. It must not reference a plugin project directly — doing so would defeat the isolation the tests exist to prove.

## Run

Run from the physical `Tests` directory so `Tests/global.json` selects Microsoft Testing Platform:

```powershell
dotnet test --project ParityBench.NET.Plugins.Tests\ParityBench.NET.Plugins.Tests.csproj -v:minimal
```
