# ParityBench.NET.Cli

Opt-in command-line host for V2 request comparison.

## Owns

- CLI argument parsing.
- CLI composition root for V2 services.
- Request comparison command execution.
- Console output and process exit codes.

## Boundaries

- Host project only: map command-line input to Application workflow requests.
- May reference concrete V2 layers because it is a composition root.
- Must not reference V1 projects or reimplement Engine, Workspace, or report behavior.

## Run

```powershell
dotnet run --project Source\ParityBench.NET.Cli\ParityBench.NET.Cli.csproj -- request <request-directory> --endpoint-a <url> --endpoint-b <url>
```

### Using a preset

`--preset <preset-id>` resolves request directory, endpoints, model, contract profile, comparison rules, and default headers from a registered preset (`IRequestComparisonPresetRegistry`), same as the preset dropdown in Desktop's Run Workflow view. Any explicit `--endpoint-a`, `--endpoint-b`, request directory, `--model`, or `--profile` flag overrides the preset's value.

```powershell
dotnet run --project Source\ParityBench.NET.Cli\ParityBench.NET.Cli.csproj -- request --preset client-soap-json-token
```

`client-soap-json-token` is the ClientCustomerLookup example preset (SOAP endpoint A / JSON endpoint B, token auth, masked sensitive fields). Requires the test fixture host running at the configured `ParityBench:RequestDefaults:FixtureBaseUrl` (default `http://localhost:5056`, see `ParityBench.NET.TestEndpoints`).
