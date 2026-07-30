# ParityBench.ClientCustomerLookupPlugin.Tests

End-to-end tests for the reference plugin, exercised the way a real client plugin is: loaded from disk as a package, not referenced as a project.

## Covers

- **`ReferencePluginEndToEndTests`** — loading the built plugin package from its output folder, running the two-hop token exchange, mapping the SOAP request to the endpoint's JSON contract, projecting both responses onto the canonical type, and comparing them.

## Boundaries

- Must not add a `ProjectReference` to `ParityBench.ClientCustomerLookupPlugin`. The point of these tests is that the plugin is discovered and loaded exactly as a client's would be, so plugin ids and comparison ids appear here as string literals rather than as constants imported from the plugin.

## Run

Run from the physical `Tests` directory so `Tests/global.json` selects Microsoft Testing Platform:

```powershell
dotnet test --project ParityBench.ClientCustomerLookupPlugin.Tests\ParityBench.ClientCustomerLookupPlugin.Tests.csproj -v:minimal
```
