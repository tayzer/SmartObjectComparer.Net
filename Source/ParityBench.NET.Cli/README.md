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
