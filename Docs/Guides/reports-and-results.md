# Reports and Results

A run produces three layers of output: a **summary** (did it pass), **paged pair details** (what differed, pair by pair), and **analysis** (what differed *across* pairs). All three are readable in the app, and all three can be exported as a self-contained static report you can hand to someone who doesn't have the tool.

## Run summary

The top-level outcome. Pairs fall into one of these classes:

| Class | Meaning |
|---|---|
| **Equal** | No differences survived the run's rules |
| **Different** | At least one difference survived |
| **Execution failed** | The call itself failed — transport error, timeout, cancellation |
| **Status code mismatch** | Both sides responded, but with different status codes |
| **Both non-success** | Both sides returned a non-2xx status |

The last three are execution outcomes, not comparison outcomes, and are counted separately. A run where every pair failed to execute is not a run where everything matched — the summary makes that distinction explicit rather than folding failures into "not different".

## Pair details

Results are **paged on disk**, not held in memory: `runs/<run-id>/details/pages/` with a `manifest.json` describing the page layout. The UI and the static report both read pages lazily, which is why a run of thousands of pairs opens as fast as a run of ten.

Open a pair to get:

- **Differences** — each with its property path, the value on each side, and its category.
- **Side-by-side diff** — the two response bodies aligned, with the differing regions highlighted.
- **Raw artifacts** — the exact bytes each endpoint returned, plus the canonical model each was projected onto. Loaded on demand, not embedded up front.

If retention has trimmed a pair's artifacts, the detail view says so rather than failing. See [Retention and Workspace](retention-and-workspace.md).

## Analysis

`details/analysis.json` and `details/difference-index.json` answer the cross-pair questions:

- Which **property paths** differ most often, and in how many pairs.
- Which **categories** of difference dominate.
- Which **objects** are most affected.

This is what turns "418 pairs differ" into "418 pairs differ, all on one field" — usually a rule you're missing rather than 418 problems. Start here before opening individual pairs.

## Run history

The **Run History** tab lists every run in the workspace with its options, timing and summary. Results stay browsable after the fact, subject to retention.

Each run stores the exact `ComparisonOptions` it ran with, so a historical result can be read with the rules that produced it rather than with today's rules.

## Static reports

A static report is a self-contained Blazor WebAssembly bundle: the same result UI, plus the run's data, in a directory you can zip, attach, or serve. No ParityBench install, no workspace, no network.

From the CLI, on a completed run:

```bash
dotnet run --project Source/ParityBench.NET.Cli -- request ./requests --endpoint-a <url> --endpoint-b <url> --report-output ./out
```

| Flag | Meaning |
|---|---|
| `--report-output <dir>` | Where to write the bundle. Generation only happens if this is set and the run completed |
| `--report-assets <dir>` | Where to find the Blazor report assets. Defaults to the `BlazorReportAssets` folder published alongside the CLI |

The CLI's publish target builds `ParityBench.NET.Report` into `BlazorReportAssets/` automatically, so a published CLI can generate reports with no extra step. If you are running from a build tree rather than a publish output and report generation can't find its assets, publish the CLI or pass `--report-assets` explicitly.

### What's in the bundle

| Path | Contents |
|---|---|
| `report.data.json` | Bootstrap payload: run snapshot, options, summary, page manifest |
| `pages/` | Paged pair details, loaded on demand |
| `analysis.json`, `difference-index.json` | Cross-pair analysis |
| `raw/<pairId>.json` | Per-pair raw bodies for Full File View, loaded only when opened |
| Blazor assets | The report application itself |

Raw bodies for non-error pairs live in **sidecars** rather than in the bootstrap payload, so opening the report doesn't mean downloading every response body in the run. Error pairs stay embedded, because the error detail flow needs them immediately. The reasoning is in the [static report sidecar packaging ADR](../Architecture/ADRs/2026-04-16-static-report-sidecar-packaging.md).

Masked fields are masked in the bundle. A mask rule keeps the sensitive value out of the artifact you hand over, not just off the screen.

### Baseline replay reports

A report from a Baseline vs Live run is titled accordingly and carries a provenance banner: which package, when it was captured, from which endpoint and environment. The banner escalates to a warning if the plugin version or environment differs between capture and replay. See [Baseline vs Live](baseline-vs-live.md).

## See also

- [Comparison Rules](comparison-rules.md) — controlling what reaches the report in the first place
- [Retention and Workspace](retention-and-workspace.md) — why an old run's artifacts may be gone
- `Source/ParityBench.NET.Report/README.md` — the report host project
