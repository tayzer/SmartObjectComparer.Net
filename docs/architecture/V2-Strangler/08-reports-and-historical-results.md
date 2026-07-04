# Slice 7: Reports And Historical Results

## Goal

Make V2 results durable and inspectable.

This slice separates run summaries from detailed pair artifacts, supports lazy raw and focused content loading, and produces static report bundles from V2 result data.

## User-Visible Behavior

Users can inspect V2 results in the same practical ways they inspect V1 results today, with cleaner storage boundaries and better lazy loading.

Reports should remain useful as standalone artifacts.

## Architecture Areas

- Run summary storage.
- Pair detail storage.
- Workspace-backed historical result browsing.
- Raw-content artifact reading.
- Focused-content artifact reading.
- Report bundle generation.
- Historical result listing.
- Result metadata compatibility.

## V1 Parity Expectations

V2 should preserve:

- Static report generation.
- Lazy raw-content sidecars.
- Focused raw-content availability where applicable.
- Summary counts and metadata.
- Pair detail inspection.
- Report navigation expectations.

## Performance Considerations

Historical result browsing should load summaries first. Detail and raw content should be loaded only on demand.

Report generation should avoid embedding every large body in the main bootstrap payload.

## Completion Criteria

- V2 can list historical run summaries without loading all details.
- V2 can load pair details lazily.
- Static reports can be generated from V2 results.
- Large raw content remains sidecar-backed or otherwise lazy.
- V1 report behavior has parity coverage.

## Non-Goals

- Do not require final workspace UX beyond the storage/read behavior needed for result inspection.
- Do not redesign the report UI unless needed for V2 data shape.
- Do not remove V1 report generation until V2 is default.

