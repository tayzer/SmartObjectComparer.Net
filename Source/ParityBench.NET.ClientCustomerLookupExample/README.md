# ParityBench.NET.ClientCustomerLookupExample

> **Legacy.** This is the reference implementation of the *compile-time contract profile* model, which has been superseded by plugins. The equivalent under the current model is [`Source/ParityBench.ClientCustomerLookupPlugin`](../ParityBench.ClientCustomerLookupPlugin) — start there. This project is retained for reference and for the tests that pin the legacy path's behaviour.

A worked example of one API pair: a SOAP request to Endpoint A, a JSON request to Endpoint B, chained bearer-token auth, and both responses normalized onto one canonical response type.

## Owns

- The four model types the contract-profile model needs: Endpoint A request, Endpoint B request, Endpoint B response, canonical response (`ClientCustomerLookupModels.cs`).
- The Mapster mapping configuration between them (`ClientCustomerLookupMapsterConfig.cs`).
- The profile itself, including the request-preparation step that performs the two-hop token exchange (`ClientCustomerLookupProfileFactory.cs`, `ClientCustomerLookupTokenProvider.cs`).
- Endpoint and preset registration for the `client-soap-json-token` preset (`ClientCustomerLookupExampleDefaults.cs`).
- The variation catalog used to generate manual-run fixtures (`ClientCustomerLookupVariationCatalog.cs`).

## Boundaries

- Not wired into any host. The `AddClientCustomerLookupExample(...)` extension exists but no host calls it; the `ProjectReference` from Cli, Desktop, TestEndpoints and ManualRunFixtureGenerator is retained for the tests and fixture generator that build the profile directly.
- Must not be extended. New client work belongs in a plugin — see [Building a Plugin](../../Docs/Guides/building-a-plugin.md).

## Tests

`Tests/ParityBench.NET.ClientCustomerLookupExample.Tests`, plus the client-scenario fitness tests in `Tests/ParityBench.NET.Fitness.Tests`, which construct this profile directly rather than through host DI.
