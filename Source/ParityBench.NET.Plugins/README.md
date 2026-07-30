# ParityBench.NET.Plugins

Host-side loading of client plugin packages.

## Owns

- `PluginCatalog` — discovers installed packages by reading `parity-plugin.json` manifests, without loading any plugin code; rejects incompatible/malformed packages with a reason.
- `PluginLoadContext` — a collectible `AssemblyLoadContext` per package, resolving via the package's `.deps.json`; the SDK (and Domain/DI abstractions) are shared from the default context so contract types unify.
- `PluginLoader` / `PluginBuilder` — instantiates the plugin entry point and collects its registrations (comparisons, middleware, services, config schemas, environments, profile templates).
- `PluginComparisonPlanFactory` — implements `IComparisonPlanFactory`: turns a run's `PluginComparisonSelection` into an executable plan (comparison definition, enabled steps, a DI scope holding plugin services plus the host services plugins may see).

## Boundaries

- References `Application`, `Domain`, `Engine`, and the SDK.
- Intended to run inside the worker process; the desktop app holds only manifest-derived metadata.

## Tests

Covered by `Tests/ParityBench.NET.Plugins.Tests` (discovery, isolation, plan building).
