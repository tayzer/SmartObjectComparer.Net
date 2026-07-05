# ParityBench.NET.Workspaces

Filesystem workspace implementation for V2.

## Owns

- Staging request batches from directories.
- Persisting run snapshots, summaries, detail indexes, and response artifacts.
- Reading paged historical detail data and bounded artifact previews.
- Keeping physical paths hidden behind storage-neutral Domain references.

## Boundaries

- References `Application` and `Domain`.
- Must not execute HTTP requests, compare response bodies, render UI, or own host configuration.
- Public behavior should preserve safe-path handling and avoid loading raw response bodies unless explicitly requested.

## Tests

Covered by `Tests/ParityBench.NET.Workspaces.Tests`.
