# V2 Contract Profiles Design Specification

## Status

Foundation implemented.

The first contract-profile implementation slice has replaced active V2 alternate-contract run configuration with `ContractProfileSelection`, `RunOptions.ResponseModelName`, `IContractProfile`, and `IContractProfileRegistry`. The current executable implementation includes the built-in `same-contract` fallback profile, ports the existing SOAP-to-JSON examples as built-in contract profiles, persists native raw artifacts separately from canonical artifacts, and wires explicit profile selection through V2 Web, Desktop, CLI, Workspaces, Engine, and shared UI.

The broader design below still describes the target state. Profile JSON loading, declarative mappings, secret/auth stage chaining, external rule-set files, plugin loading, and richer profile selectors remain future slices.

This specification replaces the V2 alternate-contract concept with a general contract-profile pipeline. It is not backwards compatible with `AlternateContractOptions`, `IAlternateContractProfile`, or existing alternate-contract profile registration. Existing V2 alternate-contract examples should be rebuilt as contract profiles rather than adapted through compatibility shims.

## Purpose

Contract profiles let a V2 run compare two endpoints even when their request contracts, response contracts, authentication flows, headers, and payload formats differ.

The selected response model remains the canonical comparison shape. A contract profile describes how Endpoint A and Endpoint B are prepared, authenticated, sent, normalized, masked, and compared through that canonical model.

This removes the special user-facing idea of an "alternate contract". A same-contract comparison and a SOAP-to-JSON comparison are both contract-profile runs. The difference is the selected profile, not a separate execution mode.

## Goals

- Replace V2 alternate contracts with a profile-driven canonicalization pipeline.
- Make Endpoint A and Endpoint B symmetrical in the profile model.
- Support passthrough, declarative mapping, and registered class-based mapping.
- Let clients implement and unit test custom mappers, response normalizers, authentication stages, and header providers.
- Let profile JSON reference registered implementations by stable IDs rather than CLR type names.
- Keep profile JSON small by referencing external comparison rule-set files instead of embedding large ignore and masking rules.
- Keep secrets out of profile JSON and logs.
- Persist raw artifacts and canonical artifacts so historical result viewing does not require rerunning profile stages.
- Keep Domain pure and keep executable behavior in Application, Engine, and Infrastructure.
- Preserve normal same-contract comparison as the default profile path.

## Non-Goals

- Do not maintain backwards compatibility with V2 alternate-contract options or interfaces.
- Do not keep `AlternateContract` naming in new public V2 contracts.
- Do not expose Mapster, JsonPath, XPath, or any third-party library as a Domain or Application dependency.
- Do not make profile JSON a general-purpose scripting language.
- Do not embed large ignore, mask, smart-ignore, or collection-order rule collections in profile JSON.
- Do not store secret values in run options, profile files, reports, or result metadata.
- Do not require plugin loading for simple built-in profiles.
- Do not switch V1 hosts as part of this design by itself.

## Terminology

- **Response model**: The canonical model selected for comparison. Both endpoint responses are normalized into this shape before structured comparison.
- **Contract profile**: A named, versioned profile that describes request preparation, auth, headers, response normalization, and comparison rule-set references for both endpoints.
- **Stage**: A single executable profile step, such as a request mapper, auth stage, response normalizer, or header provider.
- **Registered stage**: A client or built-in implementation registered under a stable ID.
- **Declarative stage**: A built-in stage configured only by profile JSON.
- **Run variables**: Per-request values produced by extraction, mapping, auth, and profile stages. Variables are scoped to a single request pair.
- **Canonical artifact**: The normalized response body persisted after endpoint-specific response normalization and masking.
- **Rule set**: An external file or registered provider that supplies comparison rules such as ignore rules, mask rules, smart-ignore rules, and collection-order rules.

## User Scenarios

### Same Contract

Both endpoints receive the same request body and return the same response model.

The selected profile is `same-contract`. Request handling and response handling are passthrough. The engine still follows the contract-profile pipeline, but each stage is trivial.

### SOAP Endpoint A To JSON Endpoint B

Endpoint A receives a SOAP XML request and returns a SOAP XML response. Endpoint B receives a JSON request and returns a JSON response. Endpoint B also needs a bearer token produced through two token clients, each with a separate subscription key.

The selected response model is a JSON canonical model. The selected contract profile:

- sends the original SOAP request to Endpoint A;
- extracts credentials and business keys from the SOAP request;
- maps the SOAP request into Endpoint B's JSON request model;
- runs token client 1;
- runs token client 2 using token client 1 output;
- adds the final bearer token to Endpoint B headers;
- normalizes Endpoint A's SOAP response into the canonical response model;
- normalizes Endpoint B's JSON response into the same canonical response model;
- applies profile-referenced comparison rule sets and per-run comparison rules;
- compares canonical artifacts.

### Client-Owned Mapping Logic

A client wants mapping logic in testable classes rather than JSON field maps.

The profile references registered IDs such as:

```json
{
  "kind": "registered",
  "id": "client-a.customer-lookup.soap-request-to-json-request.v1"
}
```

The client registers a class against that ID in the host or plugin composition root. The engine never reads the CLR type name from the profile JSON.

## High-Level Architecture

```text
Host
  loads profile documents
  registers built-in and client stages
  starts run

Application
  validates selected response model and profile
  owns profile contracts and stage contracts
  owns variable resolution contracts

Engine
  executes request pairs
  invokes profile pipeline stages
  sends endpoint requests
  persists raw and canonical artifacts
  compares canonical artifacts

Infrastructure
  implements JSON profile loading
  implements serializer adapters
  implements built-in declarative mappers
  implements built-in HTTP token stages
  implements secret/config value resolvers
```

Domain continues to own run options, endpoints, request references, comparison options, and immutable result models. Domain must not execute profile behavior or reference mapping/auth libraries.

## Replacement Scope

Remove the V2 alternate-contract surface:

```text
AlternateContractOptions
IAlternateContractProfile
IAlternateContractProfileRegistry
PreparedAlternateContractRequest
NormalizedAlternateContractResponse
AlternateContractRequestPreparationContext
AlternateContractResponseNormalizationContext
```

Replace it with contract-profile naming:

```text
ContractProfileSelection
IContractProfile
IContractProfileRegistry
PreparedContractRequest
NormalizedContractResponse
ContractRequestPreparationContext
ContractResponseNormalizationContext
```

The exact implementation names may vary, but new V2 public contracts should use contract-profile terminology, not alternate-contract terminology.

## Domain Model Changes

`RunOptions` should carry the selected response model and optional contract profile selection.

Conceptual shape:

```text
RunOptions
  RequestBatch
  EndpointA
  EndpointB
  Timeout
  MaxConcurrency
  ResponseModelName
  ContractProfile
  Comparison
  RequestExecution
```

`ResponseModelName` replaces ambiguous use of `ModelName` where practical. If the current `ModelName` property remains during V2 evolution, its semantics should be documented as the response model name.

`ContractProfileSelection` contains:

```text
ProfileId
ProfileVersion
Options
```

`ProfileVersion` is optional at run creation if the registry can resolve the latest compatible version, but persisted run snapshots must store the concrete profile version used.

If no profile is selected, Application should resolve the built-in `same-contract` profile for the selected response model.

## Application Contracts

Application owns the contracts that stage implementations target. These contracts should be stable and unit-test friendly.

### Contract Profile

An executable profile exposes:

```text
ProfileId
DisplayName
Version
ResponseModelName
EndpointA
EndpointB
ComparisonRuleSets
Validate()
```

Each endpoint profile exposes:

```text
RequestPreparation
AuthPipeline
HeaderRules
ResponseNormalization
```

Profiles may be loaded from JSON, built in code, or assembled by plugins. The engine should consume a resolved executable profile, not raw JSON.

### Stage Registry

The registry resolves configured stage references to executable stage implementations.

Stage IDs must be:

- stable;
- unique within a stage kind;
- versioned when behavior can change;
- independent of CLR type names;
- safe to expose in run metadata and reports.

Example ID:

```text
client-a.customer-lookup.soap-request-to-json-request.v1
```

### Stage Kinds

The first supported stage kinds should be:

- `passthrough-request`
- `declarative-request-mapper`
- `registered-request-mapper`
- `http-token-auth`
- `registered-auth-stage`
- `static-header-provider`
- `registered-header-provider`
- `passthrough-response-normalizer`
- `declarative-response-normalizer`
- `registered-response-normalizer`

Internally these can be modeled by narrower interfaces, but profile validation should reason about these stage kinds.

### Mapping Contracts

Registered mapping should be strongly typed where possible. The profile registry can capture source and target types during registration, while the engine invokes through non-generic Application contracts.

Request mapping contract semantics:

```text
Input:
  source request payload or source request model
  request metadata
  per-request variable bag
  cancellation token

Output:
  target request payload or target request model
  content type
  payload format
  optional variables
```

Response normalization contract semantics:

```text
Input:
  raw endpoint response payload
  endpoint metadata
  per-request variable bag
  cancellation token

Output:
  canonical response payload
  canonical content type
  canonical payload format
  optional variables
```

Mapping classes should be pure by convention. HTTP calls and token orchestration belong in auth stages or explicit external-service stages, not ordinary mappers.

## Profile Document Format

Profile documents are configuration, not executable code. They select and configure stages.

Example:

```json
{
  "schemaVersion": 1,
  "id": "client-a.customer-lookup.soap-json.v1",
  "displayName": "Client A Customer Lookup SOAP to JSON",
  "version": "1.0.0",
  "responseModel": "CustomerLookupResponse",
  "endpointA": {
    "request": {
      "kind": "passthrough"
    },
    "response": {
      "kind": "registered",
      "id": "client-a.customer-lookup.soap-response-to-canonical.v1"
    }
  },
  "endpointB": {
    "request": {
      "kind": "registered",
      "id": "client-a.customer-lookup.soap-request-to-json-request.v1"
    },
    "auth": [
      {
        "kind": "registered",
        "id": "client-a.customer-lookup.primary-token.v1",
        "output": "primaryToken"
      },
      {
        "kind": "registered",
        "id": "client-a.customer-lookup.final-token.v1",
        "input": "${auth.primaryToken}",
        "output": "finalToken"
      }
    ],
    "headers": {
      "Authorization": "Bearer ${auth.finalToken.access_token}",
      "Content-Type": "application/json"
    },
    "response": {
      "kind": "registered",
      "id": "client-a.customer-lookup.json-response-to-canonical.v1"
    }
  },
  "comparisonRuleSets": {
    "merge": "profileThenRun",
    "sources": [
      {
        "kind": "file",
        "id": "client-a.customer-lookup.profile-rules.v1",
        "path": "profiles/client-a/customer-lookup/rules/profile-rules.json",
        "required": true
      },
      {
        "kind": "file",
        "id": "client-a.customer-lookup.masking.v1",
        "path": "profiles/client-a/customer-lookup/rules/masking.json",
        "required": true
      }
    ]
  }
}
```

The profile owns the rule-set references, not the full rule bodies. This keeps profile documents readable even when a client has thousands of masking or ignore rules.

### Declarative Request Mapping Example

```json
{
  "kind": "declarative",
  "sourceFormat": "xml",
  "targetFormat": "json",
  "contentType": "application/json",
  "rules": {
    "lookupId": "/Envelope/Body/LookupRequest/CustomerId",
    "postcode": "/Envelope/Body/LookupRequest/Postcode"
  }
}
```

### Declarative Response Normalization Example

```json
{
  "kind": "declarative",
  "sourceFormat": "xml",
  "targetFormat": "json",
  "contentType": "application/json",
  "rules": {
    "resultCode": "/Envelope/Body/LookupResponse/StatusCode",
    "customerName": "/Envelope/Body/LookupResponse/CustomerName",
    "traceId": "/Envelope/Body/LookupResponse/TraceId"
  }
}
```

Declarative rules should support a small, explicit expression set:

- XPath for XML sources.
- JSON Pointer or JsonPath for JSON sources.
- Constant values.
- Variable references.
- Basic null/default handling.

Do not add loops, conditionals, arbitrary method calls, or script execution to profile JSON.

## Runtime Pipeline

For each request pair:

```text
1. Create per-request profile execution context.
2. Load original staged request body.
3. Prepare Endpoint A request through Endpoint A request stage.
4. Prepare Endpoint B request through Endpoint B request stage.
5. Run Endpoint A auth stages and header providers.
6. Run Endpoint B auth stages and header providers.
7. Send Endpoint A and Endpoint B requests.
8. Persist raw Endpoint A and Endpoint B response artifacts.
9. If both responses are success responses, normalize Endpoint A response.
10. Normalize Endpoint B response.
11. Apply mask rules from resolved rule sets to canonical responses.
12. Persist canonical Endpoint A and Endpoint B artifacts.
13. Compare canonical artifacts with resolved profile rule sets plus run comparison options.
14. Persist pair result metadata.
```

Non-success responses should still persist raw artifacts. Canonical normalization should run only when the result classification path requires structured comparison.

The engine should treat request preparation failures, auth failures, send failures, normalization failures, and comparison failures as deterministic per-pair failures unless the run is cancelled.

## Endpoint Symmetry

Endpoint A and Endpoint B use the same profile model. Either endpoint may:

- receive the original request;
- receive a transformed request;
- require auth;
- require custom headers;
- return the canonical response model directly;
- require response normalization.

This symmetry avoids encoding "Endpoint B is special" into the architecture.

## Header Precedence

Headers should be merged in deterministic order:

```text
1. endpoint configured headers
2. request manifest headers
3. endpoint-specific request manifest headers
4. profile static headers
5. profile auth/header provider outputs
```

Later sources override earlier sources using case-insensitive header names.

Profile stages may remove headers explicitly. For example, a SOAP-to-JSON profile can remove `SOAPAction` from Endpoint B without making `SOAPAction` removal a global engine rule.

## Variables And Expressions

Each request pair gets an isolated variable bag.

Suggested namespaces:

```text
request.*
endpointA.*
endpointB.*
auth.*
profile.*
secret.*
env.*
config.*
```

Variables produced by stages should be serializable when they are included in diagnostic metadata, but secret-bearing values must be redacted.

Expression examples:

```text
${auth.finalToken.access_token}
${secret:clientA.primarySubscriptionKey}
${env:CLIENT_A_TOKEN_URL}
${config:profiles.clientA.timeoutSeconds}
```

Secret and config resolution belongs to Infrastructure. Application contracts should depend on abstractions such as `IProfileValueResolver`.

## Secrets And Redaction

Profile JSON must not contain literal secrets.

Secret references must be resolved at execution time. The resolver returns both the value and metadata indicating whether the value is sensitive.

Sensitive values must be redacted from:

- logs;
- exception messages;
- run metadata;
- result detail files;
- static report manifests;
- profile validation output;
- diagnostic stage traces.

If a stage fails because a secret is missing, the error should identify the missing reference, not the secret value.

## Persistence

Runs should persist enough metadata to explain which profile behavior was used without requiring live plugin execution.

Run snapshot fields:

```text
ResponseModelName
ContractProfileId
ContractProfileVersion
ContractProfileConfigHash
ContractProfileDisplayName
ComparisonRuleSetReferences
ComparisonRuleSetHashes
```

Artifact layout concept:

```text
raw/
  a/{requestArtifactId}.body
  b/{requestArtifactId}.body
canonical/
  a/{requestArtifactId}.body
  b/{requestArtifactId}.body
```

Raw artifacts are endpoint-native responses. Canonical artifacts are normalized and masked comparison inputs.

Rule-set references and content hashes should be persisted with the run snapshot so historical results can be traced back to the exact comparison rule files used for the run. Large rule files should not be copied into the profile metadata. If the workspace needs immutable historical replay, rule files can be copied into a run-owned configuration artifact and referenced by artifact ID.

Reports should read persisted artifacts only. Report viewing must not invoke contract profiles, plugins, auth stages, or normalizers.

## Comparison Rule Sets

Profiles may reference comparison rule sets:

- ignore rules;
- mask rules;
- smart ignore rules;
- collection order rules;
- profile-owned response path mappings where masking must target raw response shape before canonicalization.

Rule sets should be external files or registered providers. Inline rule arrays should be reserved only for tiny examples, tests, or temporary local experimentation, and should not be the recommended production shape.

Rule-set file references should include:

```text
Id
Kind
Path or ProviderId
Required
ExpectedHash
Description
```

The first supported rule-set kind should be `file`. Later versions may add registered rule providers for dynamic or tenant-specific rule discovery.

Effective comparison rules are:

```text
profile rule sets, in declared order
then run options
```

Run options should be able to add rules without mutating the registered profile. If override semantics are required later, they should be explicit in the comparison option model.

Very large rule files should be parsed through streaming or bounded-memory readers where practical. The profile loader should validate references without materializing all rules into the profile document model.

## Validation

Application should validate profile selection before execution starts.

Validation should check:

- selected profile exists;
- selected profile response model matches the run response model;
- profile schema version is supported;
- profile stage IDs resolve;
- registered stage types are compatible with configured source and target formats;
- required secrets/config refs are present when validation is allowed to resolve them;
- auth outputs have unique names;
- variable references target known prior stages or allowed late-bound namespaces;
- endpoint request content types are known;
- canonical response content types are supported by the configured comparer;
- referenced rule-set files exist when required;
- referenced rule-set files are valid comparison rule files;
- referenced rule-set hashes match when an expected hash is supplied;
- profile document hash can be computed for persistence.

Per-request validation should check:

- source request format can be detected;
- request body can be opened;
- mapping input can be deserialized or parsed;
- required extracted fields are present;
- auth stage outputs satisfy downstream header expressions.

Profile loading errors are configuration errors. Per-request data problems are pair execution failures unless they indicate a broken global configuration.

## Error Handling

Errors should be classified by stage:

```text
ProfileResolutionFailed
ProfileValidationFailed
RequestPreparationFailed
AuthenticationFailed
HeaderResolutionFailed
EndpointSendFailed
ResponseNormalizationFailed
ComparisonFailed
Cancelled
```

Pair-level errors should include:

- request relative path;
- endpoint slot if relevant;
- profile ID;
- stage kind;
- stage ID if registered;
- redacted message.

Cancellation should remain distinct from failure and must not be swallowed into pair execution errors.

## Built-In Stages

Initial built-in stages:

- `passthrough` request stage;
- `passthrough` response normalizer;
- XML-to-JSON declarative mapper;
- JSON-to-JSON declarative mapper;
- XML-to-canonical declarative response normalizer;
- JSON-to-canonical declarative response normalizer;
- static headers;
- expression-based headers;
- HTTP token client auth stage;
- same-contract profile factory.

Mapster can be the default registered mapping implementation, but it should sit behind V2-owned mapping contracts. The core Application contracts should not require consumers to reference Mapster.

## Plugin And Client Extension Model

Clients extend the system by registering stage implementations under stable IDs.

Registration should happen in host composition or plugin composition:

```text
RegisterRequestMapper(id, implementationType, sourceType, targetType)
RegisterResponseNormalizer(id, implementationType, sourceType, canonicalType)
RegisterAuthStage(id, implementationType)
RegisterHeaderProvider(id, implementationType)
```

The profile JSON references the ID. The registry resolves the implementation. The engine executes through Application-owned interfaces.

This gives clients clean test boundaries:

- mapper unit tests;
- normalizer unit tests;
- auth stage unit tests with fake HTTP handlers;
- profile validation tests;
- whole-profile integration tests.

## Host Behavior

Hosts should expose "Comparison Profile" selection, not "Alternate Contract".

Host responsibilities:

- discover profiles for the selected response model;
- show profile display names and descriptions;
- preselect `same-contract` when no profile is needed;
- show profile validation errors before starting a run;
- allow profile-specific options where safe;
- avoid displaying secret values;
- pass only profile selection and run options into Application.

CLI concept:

```text
request compare
  --response-model CustomerLookupResponse
  --profile client-a.customer-lookup.soap-json.v1
```

If `--profile` is omitted, Application resolves the default `same-contract` profile.

## Testing Strategy

Domain tests:

- `ContractProfileSelection_WhenProfileIdIsEmpty_Throws`
- `RunOptions_WhenProfileIsProvided_StoresProfileSelection`
- `RunOptions_WhenProfileIsOmitted_AllowsDefaultProfileResolution`

Application tests:

- profile registry duplicate ID handling;
- missing profile handling;
- response model mismatch handling;
- unresolved stage ID handling;
- profile schema validation;
- external rule-set reference validation;
- variable expression validation;
- secret reference redaction.

Infrastructure tests:

- JSON profile loading;
- external rule-set file loading;
- external rule-set hash stability;
- profile config hash stability;
- built-in declarative XML mapping;
- built-in declarative JSON mapping;
- HTTP token client stage with fake HTTP handler;
- secret/config resolver behavior;
- Mapster adapter behavior if Mapster is introduced.

Engine tests:

- same-contract profile preserves current basic request A/B behavior;
- SOAP-to-JSON profile transforms Endpoint B request;
- Endpoint A response normalization writes canonical artifact;
- Endpoint B response normalization writes canonical artifact;
- auth stage outputs override headers correctly;
- per-request mapping failure produces pair failure;
- normalization failure produces pair failure;
- cancellation remains cancellation;
- profile rule sets combine with run comparison options.

Workspace/report tests:

- run snapshot round-trips profile ID, version, and hash;
- raw and canonical artifacts are persisted with stable references;
- run snapshots persist rule-set references and hashes;
- static report loads persisted profile metadata without invoking profile code.

## Implementation Plan

### Phase 1: Domain And Application Contract Replacement

- Add contract-profile selection model.
- Add Application profile and stage contracts.
- Add profile registry and stage registry contracts.
- Remove alternate-contract Domain and Application contracts from V2.

### Phase 2: Infrastructure Profile Loading

- Add JSON profile document model.
- Add external comparison rule-set reference model.
- Add profile loader and validator.
- Add rule-set loader and validator.
- Add built-in same-contract profile.
- Add built-in passthrough stages.
- Add profile config hash generation.

### Phase 3: Engine Pipeline

- Replace optional alternate-contract branch with always-on contract-profile resolution.
- Execute same-contract through the new pipeline.
- Persist raw and canonical artifacts.
- Preserve current comparison behavior for same-contract runs.

### Phase 4: Mapping And Normalization Stages

- Add declarative XML/JSON mapping stages.
- Add registered mapper and normalizer adapters.
- Add Mapster adapter behind V2 mapping contracts if selected.
- Rebuild current SOAP-to-JSON examples as contract profiles.

### Phase 5: Auth And Header Stages

- Add static and expression-based header stages.
- Add registered auth stage contract.
- Add built-in HTTP token client stage.
- Support chained auth outputs.
- Add redaction guarantees.

### Phase 6: Host Integration

- Replace alternate-contract UI/CLI naming with comparison profile naming.
- Add profile discovery by response model.
- Add pre-run validation display.
- Persist selected profile metadata in workspaces and reports.

## Open Decisions

- Whether `ResponseModelName` should replace `ModelName` immediately or be introduced as a semantic alias first.
- Whether profile version should use semantic version strings or monotonic integer schema versions.
- Whether declarative JSON mapping should use JSON Pointer only, JsonPath only, or support both.
- Whether profile files should live in workspace configuration, application configuration, plugin packages, or all three.
- Whether rule-set files should share the existing comparison configuration schema or use a smaller V2-owned rule-bundle schema.
- Whether run snapshots should copy rule files into run-owned artifacts by default or only persist references and hashes.
- Whether registered class stages should be singleton, scoped per run, or created per request.
- Whether profile validation should resolve secret existence eagerly or defer secret resolution to execution.

## Acceptance Criteria

The replacement is complete when:

- V2 no longer exposes alternate-contract terminology in new Domain, Application, Engine, Workspace, or Host contracts.
- Same-contract comparison runs through the contract-profile pipeline.
- At least one SOAP-to-JSON profile is implemented with registered mapping/normalization stages.
- At least one profile uses a chained auth pipeline with two token stages and generated bearer headers.
- Profile JSON references external comparison rule-set files instead of embedding large ignore and masking collections.
- Profile JSON can use declarative mapping for simple cases.
- Profile JSON can reference registered client implementations by ID for custom cases.
- Raw and canonical artifacts are persisted and visible to reports without rerunning profile code.
- Validation errors are deterministic and redacted.
- Unit and integration tests cover same-contract, declarative mapping, registered mapping, auth chaining, response normalization, and failure classification.
