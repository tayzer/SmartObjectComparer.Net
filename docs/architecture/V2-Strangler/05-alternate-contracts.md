# Slice 4: Alternate Contracts

## Goal

Rebuild alternate-contract behavior in the V2 architecture.

This slice covers endpoint-B request transformation, endpoint A/B response normalization, canonical comparison models, default ignore rules, profile resolution, and existing alternate-contract scenarios.

## User-Visible Behavior

Users can compare endpoint A and endpoint B even when endpoint B uses a different request or response contract.

Results should appear equivalent to V1: users still see comparable canonical output and expected ignore behavior.

## Architecture Areas

- Alternate-contract profile model.
- Request transformation.
- Response normalization.
- Canonical model selection.
- Default ignore rules.
- Profile lookup and validation.
- Legacy profile bridge policy.

## V1 Parity Expectations

V2 should match existing alternate-contract scenarios:

- Supported source request formats.
- Endpoint-B request body generation.
- Profile-generated headers.
- Endpoint A response normalization.
- Endpoint B response normalization.
- Canonical response format.
- Profile default ignore rules.
- Error messages for unsupported or invalid profiles.

## Performance Considerations

Normalization should avoid unnecessary large-body retention. Where possible, normalization should read from and write to artifacts or streams.

Profile behavior should remain per-run and deterministic under concurrent execution.

## Completion Criteria

- Current alternate-contract profiles have V2 equivalents or explicit temporary legacy adapters.
- V2 produces equivalent pair outcomes and differences for current alternate-contract tests.
- Profile default ignore rules are applied through immutable run configuration.
- Temporary V1 profile dependencies are isolated to Infrastructure adapters.

## Non-Goals

- Do not add a plugin system unless required later.
- Do not broaden profile behavior beyond V1 parity.
- Do not expose infrastructure serialization details to Application or Domain.

