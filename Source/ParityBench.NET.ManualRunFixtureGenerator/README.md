# ParityBench.NET.ManualRunFixtureGenerator

Developer tool that generates volume request fixtures for manual and large-run testing. Not part of the shipped product.

## Owns

- Deterministic generation of `client-soap-json-token` SOAP request fixtures across the variation catalog (equal, value-changed, missing, extra, non-success, and so on).
- Writing the generated set to disk and printing the resulting category distribution, so a large run has a known expected outcome.

## Boundaries

- A console tool only. It must not be referenced by the product, by hosts, or by the engine.
- References `ParityBench.NET.ClientCustomerLookupExample` for the variation catalog. That reference is the reason the legacy example project is still built.

## Run

Defaults to 1000 fixtures starting at customer id 10000, written to `Examples/ParityBench.NET.ManualRuns/client-soap-json-token/volume`:

```powershell
dotnet run --project Source\ParityBench.NET.ManualRunFixtureGenerator
```

```powershell
dotnet run --project Source\ParityBench.NET.ManualRunFixtureGenerator -- --count 5000 --start-id 20000 --output C:\temp\fixtures
```

| Option | Default | Meaning |
|---|---|---|
| `--count <n>` | `1000` | Number of request fixtures to generate |
| `--start-id <n>` | `10000` | First customer id; ids increment from here |
| `--output <dir>` | repo `Examples/…/volume` | Target directory |

## Tests

`ManualRunFixtureGeneratorTests` in `Tests/ParityBench.NET.Fitness.Tests` pins the generated distribution.
