# Retention and Workspace

Every run persists the raw bytes both endpoints returned. That is what makes results reviewable after the fact and what keeps memory bounded during the run — but a few large runs will fill a disk. Retention is the policy that decides what survives once a run has finished.

## The workspace

All state lives under one workspace root:

| Host | Default root |
|---|---|
| Desktop, Web | `%LOCALAPPDATA%\ParityBench.NET\Workspace`, overridable via `ParityBench:WorkspaceRoot` |
| CLI | `%LOCALAPPDATA%\ParityBench.NET\Workspace`, **not** currently overridable — the CLI takes the root as a parameter its entry point never supplies, so configuration and environment variables do not redirect CLI runs |

Layout:

```
runs/<run-id>/
  run.json                     run options, status, summary
  artifacts/A/, artifacts/B/   raw responses as they came off the wire
  details/
    manifest.json              page layout
    pages/                     paged pair results
    analysis.json              cross-pair analysis
    difference-index.json      difference index
request-batches/               staged request files, per batch
baselines/<id>/v<n>/           captured baseline packages
config/
  profiles/*.json              saved run profiles
  accepted-differences.json    accepted-difference profiles
plugins/                       installed plugin packages
manual-requests/               requests staged from the app
data-protection-keys/          ASP.NET data protection keys
```

Plugins are also picked up from a `plugins` folder next to the application itself, so a client can install a package without write access to the app directory, or vice versa.

## Retention

Retention runs as the final stage of a run (`RetentionCleanupStage`), after every pair has been compared. It never touches a run in progress, and it never deletes results — only the response artifacts behind them. A trimmed pair keeps its differences, its summary and its place in history; what it loses is the ability to show you the original bytes.

Decisions are made per pair, from the pair's outcome class.

### Modes

Configured at `ParityBench:Retention:Mode`. Applies to pairs that executed successfully.

| Mode | Equal pairs | Different pairs |
|---|---|---|
| `TrimmedEqualsAndIgnoredPaths` **(default)** | Artifacts trimmed | Only the artifacts relevant to the surviving differences are kept |
| `TrimmedEquals` | Artifacts trimmed | Everything kept |
| `TrimmedIgnoredPaths` | Everything kept | Only the relevant artifacts kept |
| `None` | Everything kept | Everything kept |

The default is the aggressive one, and it is the right default: a pair that came out equal has nothing to investigate, and equal pairs are the overwhelming majority of a healthy run.

Use `None` when you are debugging the comparison itself and need the original bytes regardless of outcome.

### Non-success pairs

Pairs that failed to execute, mismatched on status code, or returned non-2xx on both sides are handled separately, because those are exactly the ones you need the raw response for. Configured at `ParityBench:Retention:NonSuccessOverride`:

| Override | Effect |
|---|---|
| `KeepBounded` **(default)** | Keep diagnostics, but within an age and size budget |
| `KeepAll` | Keep every non-success artifact regardless of budget |
| `TrimAll` | Trim them like any other pair |

`KeepBounded` bounds by three limits:

| Key | Default | Meaning |
|---|---|---|
| `NonSuccessDiagnosticRetentionWindowDays` | `14` | Age beyond which non-success diagnostics are eligible for trimming |
| `NonSuccessDiagnosticRetentionMaxBytesPerRun` | 5 GiB | Per-run budget |
| `NonSuccessDiagnosticRetentionMaxBytesWorkspace` | 50 GiB | Workspace-wide budget |

All three must be greater than zero; the configuration is validated at startup, so an invalid value fails fast rather than silently reverting to a default.

### Configuration

```json
{
  "ParityBench": {
    "WorkspaceRoot": "D:\\ParityBench\\Workspace",
    "Retention": {
      "Mode": "TrimmedEqualsAndIgnoredPaths",
      "NonSuccessOverride": "KeepBounded",
      "NonSuccessDiagnosticRetentionWindowDays": 14,
      "NonSuccessDiagnosticRetentionMaxBytesPerRun": 5368709120,
      "NonSuccessDiagnosticRetentionMaxBytesWorkspace": 53687091200
    }
  }
}
```

An unrecognized `Mode` or `NonSuccessOverride` value throws at startup naming the offending key, rather than being ignored.

## Baselines are exempt

Baseline packages under `baselines/` are not subject to retention. A baseline is a deliberate, named artifact with its own lifecycle — versioned, never overwritten, deleted only when you delete it. That is the whole point: the captured version may no longer exist to re-capture from. See [Baseline vs Live](baseline-vs-live.md).

## Reclaiming space

- **Delete a run directory.** Runs are self-contained under `runs/<run-id>/`; removing one removes it from history and costs nothing else.
- **Tighten the mode.** `TrimmedEqualsAndIgnoredPaths` is already the tightest that preserves investigability.
- **Check non-success first.** On a run with widespread failures, `KeepBounded` diagnostics are usually the bulk of what's on disk.
- **Export what you need to keep.** A [static report](reports-and-results.md) is self-contained and unaffected by later retention, so it's the right way to preserve a result long-term.

## CI

`scripts/Generate-RetentionMatrixReport.ps1` produces a matrix report of retention decisions across outcome classes and modes; CI runs it on every build and uploads the result as an artifact. It's the fastest way to see what a policy change actually does before you ship it.

## See also

- [Reports and Results](reports-and-results.md) — what survives trimming
- [Baseline vs Live](baseline-vs-live.md) — the retention-exempt package format
- [High-Level Design](../Architecture/high-level-design.md) — why artifacts are persisted rather than held in memory
