# Slice 3: Existing Options Parity

## Goal

Implement V2 parity for the core options already used by V1 request comparison, without switching Web, Desktop, or CLI hosts to V2.

Slice 3 changes the successful 2xx comparison path from raw hash-only classification to model-aware comparison behind a V2 Engine adapter. Slice 2's basic classifications remain for non-success and execution-failure cases.

## Implemented Shape

### Domain

V2 now owns storage-neutral option and result contracts:

- `ComparisonOptions`
  - `IgnoreCollectionOrder`
  - `IgnoreStringCase`
  - `IgnoreTrailingWhitespaceAtEnd`
  - `TreatNullAndEmptyCollectionsAsEqual`
  - `IgnoreXmlNamespaces`
  - `MaxDifferences`
  - `IgnoreRules`
  - `SmartIgnoreRules`
  - `MaskRules`
- `IgnoreRuleDefinition`
- `SmartIgnoreRuleDefinition`
- `SmartIgnoreRuleKind`
- `MaskRuleDefinition`
- `RequestExecutionOptions`
- `ComparisonDifference`

`RunOptions` carries immutable per-run comparison and request-execution options while keeping Slice 2 constructor defaults compatible.

`RequestPairResult` now stores lightweight comparison metadata:

- equality flag
- difference count
- V2-owned difference records

Raw response bodies remain artifact references, not detail payloads.

### Application

The Application layer exposes ports for the new behavior:

- `IRunArtifactStore.OpenReadAsync` reopens persisted response artifacts by logical reference.
- `IResponseBodyDeserializer` converts persisted response bodies into registered model objects.
- `IResponseModelRegistry` resolves stable model names to concrete response model types.

These ports keep Engine orchestration independent from file-system and serializer details.

### Engine

`BasicComparisonRunExecutor` still owns the Slice 2 batch execution flow, bounded concurrency, progress reporting, artifact persistence, detail persistence, and summary completion.

For each pair:

1. Endpoint A and B are executed with merged headers.
2. `RequestExecutionOptions.ContentTypeOverride` is applied to outbound request content type when present.
3. JSON/XML mask rules are applied before artifact persistence.
4. Persisted artifacts are classified through an `IResponseComparer`.
5. Detail metadata is saved without raw response bodies.

The default constructor preserves Slice 2 hash-only behavior via `HashOnlyResponseComparer`.

The model-aware path uses `CompareNetObjectsResponseComparer`, which creates an isolated `CompareLogic` per comparison. Per-run options are mapped into that instance, so concurrent runs do not share mutable comparer configuration.

Successful 2xx pairs are classified as:

- `Equal` when model comparison has no remaining differences after configured filters.
- `Different` when differences remain.
- `ExecutionFailed` when artifact readback, deserialization, or comparison fails.

The hash equality fast path is used only when successful artifacts match and no comparison-affecting options are present.

### Infrastructure

Infrastructure now contains:

- `ResponseModelRegistry`
- `JsonXmlResponseBodyDeserializer`

JSON uses `System.Text.Json` with case-insensitive property names.

XML uses `XmlSerializer`. When `IgnoreXmlNamespaces` is enabled, namespaces are stripped before deserialization for representative V1-style payloads.

### Workspaces

File-system Workspaces now persist and reload:

- comparison options
- request execution options
- ignore rules
- smart ignore rules
- mask rules
- comparison difference metadata

Artifacts can also be reopened by logical artifact reference.

## Behavioral Notes

- Endpoint headers still merge in the Slice 2 order: endpoint headers, request common headers, request endpoint-specific headers.
- `SOAPAction` is represented as an ordinary endpoint/request header in this slice.
- Masking currently buffers the response body when mask rules are present. Streaming/large-body masking optimization remains a later improvement.
- Property-specific collection-order rules are represented in Domain. Slice 3 maps collection-order options to practical representative behavior; deeper V1 custom comparer parity can be expanded in later hardening if needed.
- V2 does not reference V1 source projects.

## Verified Coverage

Representative tests cover:

- option defaulting and validation
- run snapshot option roundtrip
- detail difference metadata roundtrip
- artifact reopen
- model registry duplicate/unknown behavior
- JSON deserialization
- XML namespace ignoring
- object equality with different raw hashes
- object differences with metadata
- ignore-complete rules
- string-case ignoring
- trailing-whitespace ignoring
- null/empty collection equivalence
- collection-order ignoring
- smart ignore by property name and name pattern
- response masking before persistence/comparison
- content-type override
- concurrent comparer isolation
- vertical Application + Workspaces + Engine + Infrastructure flow with fake endpoints

## Non-Goals

- Host/Web/Desktop/CLI mapping to V2 options.
- Alternate-contract profiles.
- SOAPAction suppression for alternate contracts.
- Final report generation.
- Raw non-success response diff parity.
- Full large-run memory optimization.
- UI changes.
