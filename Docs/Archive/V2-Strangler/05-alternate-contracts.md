# Slice 4: Alternate Contracts And Canonical Comparison Flow

## Goal

Rebuild V1 alternate-contract behavior inside the V2 architecture without switching any Web, Desktop, or CLI host to V2.

This slice lets a run compare endpoint A and endpoint B when endpoint B uses a different request or response contract. The Engine transforms endpoint B requests, normalizes both responses into the canonical model contract, then uses the existing V2 comparison pipeline so masking, default ignore rules, and per-run comparison options remain deterministic.

## Implemented Project Shape

- `ParityBench.NET.Domain`
  - Adds `AlternateContractOptions` to `RunOptions` so a run can select a profile by logical profile id.
  - Adds `PayloadFormat` for JSON/XML alternate-contract routing.
- `ParityBench.NET.Application`
  - Adds alternate-contract ports and contracts: profile registry, profile interface, payload serializer, request preparation context, prepared endpoint-B request, response normalization context, and normalized response payload.
- `ParityBench.NET.Engine`
  - Extends `BasicComparisonRunExecutor` with an optional `IAlternateContractProfileRegistry`.
  - Executes normal runs through the existing streamed artifact path.
  - Executes alternate-contract runs through endpoint-B transformation, response normalization, canonical artifact persistence, and the existing response comparer.
- `ParityBench.NET.Infrastructure`
  - Adds a JSON/XML contract payload serializer.
  - Adds an in-memory alternate-contract profile registry with deterministic duplicate, missing, mismatch, and ambiguous-profile errors.
  - Adds generic profile wiring plus built-in profile definitions for the current representative SOAP-to-JSON scenarios.
- `ParityBench.NET.Workspaces`
  - Persists and reloads `AlternateContractOptions` in run snapshots.

## Runtime Flow

1. Application starts a run using immutable `RunOptions`.
2. Engine resolves the configured alternate-contract profile from `ModelName` and `AlternateContract.ProfileId`.
3. Profile default ignore rules are prepended to the run comparison rules for this execution only.
4. Endpoint A receives the original staged request.
5. Endpoint B request preparation reads the staged source body, detects JSON/XML, calls the profile, removes `SOAPAction`, applies profile-generated headers, and sends the transformed request body/content type.
6. Both endpoint responses are captured for normalization when the alternate-contract path is active.
7. Non-success or sender-failure pairs fall back to the existing basic result classification and raw artifact persistence.
8. Successful pairs are normalized into the canonical response format, masked, persisted as canonical artifacts, and compared by the V2 response comparer.
9. Detail indexes continue to store lightweight pair metadata and artifact references, not raw bodies.

## Profile Contract

An alternate-contract profile owns only contract-shape behavior:

- profile id and canonical model name;
- canonical and alternate request/response CLR types;
- supported source request formats;
- endpoint-B request format and content type;
- alternate response format;
- canonical response format and content type;
- optional suggested endpoint ids;
- default ignore rules;
- optional canonical-to-alternate mask path map;
- request preparation and response normalization methods.

Profiles are resolved per run and must not mutate shared comparer or serializer state. Profile defaults are copied into an execution-specific `RunOptions` instance before comparison.

## Behavioral Rules

- Alternate-contract selection is storage-neutral and uses logical profile ids, not file paths or host-specific settings.
- Unsupported source request formats produce an `ExecutionFailed` pair rather than crashing the whole batch.
- Missing registries, unknown profiles, duplicate profile ids, and model/profile mismatches are deterministic configuration errors.
- `SOAPAction` is suppressed only for transformed endpoint-B requests.
- Profile-generated headers override endpoint headers and request headers for endpoint B.
- Canonical artifacts are persisted after normalization and masking, so later result loading does not need profile execution.
- Existing V2 comparison options still apply after canonical normalization.

## Built-In Profile Coverage

This slice includes V2-owned profile implementations for representative current alternate-contract behavior:

- `sample-soap-to-json` for a SOAP request/response compared against a JSON endpoint through a canonical SOAP response model.
- `expected-json-customer-lookup` for a JSON canonical response profile with endpoint-B authorization header generation, source-system default ignore behavior, and suggested endpoint ids.

These profiles are Infrastructure implementations. V2 source projects do not reference V1 projects.

## Tests

Coverage added or extended in this slice:

- Domain option validation and `RunOptions` storage.
- Infrastructure profile registry resolution, duplicate/missing/mismatch errors, JSON/XML serialization, profile request transformation, profile response normalization, and expected-profile authorization/default metadata.
- Engine endpoint-B transformation, `SOAPAction` suppression, unsupported request format failure, normalization failure handling, built-in profile canonical comparison, and canonical artifact persistence.
- Workspaces run snapshot round-trip for alternate-contract options.

## Non-Goals

- No host integration yet.
- No plugin system.
- No V1 project references from V2 projects.
- No broad alternate-contract behavior beyond current parity scenarios.
- No final report or UI flow changes.
