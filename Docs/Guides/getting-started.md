# Getting Started

This walks a first comparison end to end against the fixture endpoints that ship with the repository, so you need nothing external. About ten minutes.

## 1. Build

Prerequisites: **.NET 10 SDK**. Windows if you want the Desktop host; Web, CLI and tests are cross-platform.

```bash
dotnet restore ComparisonTool.sln
dotnet build ComparisonTool.sln -c Release
```

## 2. Start the fixture endpoints

`ParityBench.NET.TestEndpoints` serves deterministic SOAP/XML/JSON endpoints designed to differ from each other in known, interesting ways. Leave this running in its own terminal:

```bash
dotnet run --project Source/ParityBench.NET.TestEndpoints --no-launch-profile --urls http://localhost:5056
```

## 3. Run a comparison

### From the CLI

```bash
dotnet run --project Source/ParityBench.NET.Cli -- request Examples/ParityBench.NET.ManualRuns/xml-xml --endpoint-a http://localhost:5056/consumer-report/soap/a --endpoint-b http://localhost:5056/consumer-report/soap/b
```

Every `.xml` file in the request directory becomes one **pair**: the same request body sent to Endpoint A and Endpoint B. The CLI prints progress and a final summary of equal, different, and failed pairs.

Add `--report-output ./out` to also write a self-contained static report you can open in a browser. See [Reports and Results](reports-and-results.md).

### From the app

```bash
dotnet run --project Source/ParityBench.NET.Desktop
```

```bash
dotnet run --project Source/ParityBench.NET.Web
```

Both open the same UI, with four tabs:

| Tab | What it's for |
|---|---|
| **Compare Requests** | Set up and start a run; the rules studios live here |
| **Run History** | Every past run, with its summary and results |
| **Baselines** | Captured baseline packages — see [Baseline vs Live](baseline-vs-live.md) |
| **Plugins & Profiles** | Installed plugins and saved run profiles — see [Building a Plugin](building-a-plugin.md) |

In **Compare Requests**: add the request files, set Endpoint A and Endpoint B to the two fixture URLs above, pick the response model (`ConsumerReportSoapResponseEnvelope` for this fixture), and start the run.

## 4. Read the result

You'll get more differences than you expect. That's the point of the first run.

The fixture responses deliberately contain generated trace IDs, timestamps, and reordered collections — noise that is *different* but not *wrong*. Real API pairs behave the same way. A first run is meant to show you what your noise looks like, not to pass.

The summary splits pairs into:

- **Equal** — no differences survived your rules.
- **Different** — at least one difference survived. Open the pair to see each one with its property path and both values.
- **Failed** — the call itself failed, or a status code didn't match. These are execution problems, not comparison results, and are kept separately from the diff outcome.

## 5. Suppress the noise

Now turn the noise off and re-run. For this fixture, in **Ignore/Mask Rules**:

Comparison flags:
- Ignore collection order
- Ignore string case
- Ignore trailing whitespace
- Ignore XML namespaces

Smart ignores (one per line):

```text
PropertyName=ReportId
PropertyName=GeneratedAt
PropertyName=ProviderTraceId
PropertyName=ProcessingMilliseconds
PropertyName=SourceSystem
```

Mask rule:

```text
Body.ConsumerReportResponse.Subject.NationalIdentifier|preserveLast=4
```

Re-run. You should now get three equal pairs and two genuinely different ones — including a nested contact-preference case, which is the sort of difference the tool exists to find.

Full rule syntax and when to use which kind: [Comparison Rules](comparison-rules.md).

## Where your data went

Runs land in a workspace directory, by default `%LOCALAPPDATA%\ParityBench.NET\Workspace`. Layout, retention, and how to move it: [Retention and Workspace](retention-and-workspace.md).

## Next

- Other fixture scenarios, with suggested rules and expected outcomes: [`Examples/ParityBench.NET.ManualRuns/README.md`](../../Examples/ParityBench.NET.ManualRuns/README.md)
- Compare **your** API pair: [Building a Plugin](building-a-plugin.md)
- Compare against a version that is already switched off: [Baseline vs Live](baseline-vs-live.md)
- How the pieces fit together: [High-Level Design](../Architecture/high-level-design.md)
