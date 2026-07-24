# ParityBench.ClientCustomerLookupPlugin

Reference plugin package. Compares a SOAP endpoint (A) against a JSON endpoint (B) that needs a chained bearer token, normalizing both onto one canonical response.

## Owns

- `ClientCustomerLookupPlugin` — the `IParityBenchPlugin` entry point wiring the comparison, middleware, config schema (with `Secret` fields), a `Local` environment, and a profile template.
- `ClientCustomerLookupRequestMiddleware` (Request phase) — SOAPAction header for A; two-hop token exchange + SOAP→JSON mapping + auth headers for B.
- `ClientCustomerLookupMappingMiddleware` (Mapping phase) — projects each endpoint's response onto the canonical type.
- `ClientCustomerLookupTokenExchange`, the models, and the Mapster config it carries as its own dependency.

## The point

This project is **not referenced by any host**. It builds to a package folder and is loaded like any third-party plugin — the proof that a client can extend ParityBench without rebuilding the product. It compiles only against `ParityBench.PluginSdk` (marked non-runtime) and carries Mapster, which the plugin load context resolves in isolation.

## Guide and tests

See [Building a Plugin](../../Docs/Guides/building-a-plugin.md). End-to-end coverage in `Tests/ParityBench.ClientCustomerLookupPlugin.Tests` and `Tests/ParityBench.NET.Cli.Tests`.
