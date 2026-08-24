# ParityBench.ClientCustomerLookupPlugin.Tests

End-to-end tests for the reference plugin, exercised the way a real client plugin is: loaded from disk as a package, not referenced as a project.

## Covers

- `ClientPluginPerformanceBenchmarkTests` is an opt-in, fresh-process fitness run through the real plugin package and full filesystem-backed production pipeline. It calibrates 8/12/16/20 compare workers at 1k, selects by throughput and memory, then verifies 2.5k/8k scaling and ordered-difference stability.

- **`ReferencePluginEndToEndTests`** — loading the built plugin package from its output folder, running the two-hop token exchange, mapping the SOAP request to the endpoint's JSON contract, projecting both responses onto the canonical type, and comparing them.

## Boundaries

- Must not add a `ProjectReference` to `ParityBench.ClientCustomerLookupPlugin`. The point of these tests is that the plugin is discovered and loaded exactly as a client's would be, so plugin ids and comparison ids appear here as string literals rather than as constants imported from the plugin.

## Run

Run from the physical `Tests` directory so `Tests/global.json` selects Microsoft Testing Platform:

```powershell
dotnet test --project ParityBench.ClientCustomerLookupPlugin.Tests\ParityBench.ClientCustomerLookupPlugin.Tests.csproj -v:minimal
```

The performance fitness test is excluded unless explicitly enabled. Its JSON report is written outside the repository under `%LOCALAPPDATA%\ParityBench.NET\Performance` by default:

```powershell
$env:PB_RUN_CLIENT_PLUGIN_FITNESS = '1'
dotnet test --project ParityBench.ClientCustomerLookupPlugin.Tests\ParityBench.ClientCustomerLookupPlugin.Tests.csproj --filter "FullyQualifiedName~ClientPluginPerformanceBenchmarkTests.ExecuteAsync_RealClientPlugin_Fitness"
```

Useful overrides: `PB_CLIENT_PLUGIN_FITNESS_COUNT`, `PB_CLIENT_PLUGIN_FITNESS_COUNTS`, `PB_CLIENT_PLUGIN_FITNESS_CONCURRENCIES`, `PB_CLIENT_PLUGIN_FITNESS_ITERATIONS`, and `PB_PERFORMANCE_OUTPUT`.
