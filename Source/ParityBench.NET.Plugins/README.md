# ParityBench.NET.Plugins

Host-side loading of client plugin packages.

## Owns

- `PluginCatalog` — discovers installed packages by reading `parity-plugin.json` manifests, without loading any plugin code; rejects incompatible/malformed packages with a reason. `Rescan()` repeats discovery and swaps in a new `PluginCatalogSnapshot`.
- `PluginLoadContext` — a collectible `AssemblyLoadContext` per package, resolving via the package's `.deps.json`; the SDK (and Domain/DI abstractions) are shared from the default context so contract types unify.
- `PluginLoader` / `PluginBuilder` — instantiates the plugin entry point and collects its registrations (comparisons, middleware, services, config schemas, environments, profile templates).
- `PluginShadowCopy` / `PluginPackageStamp` / `PluginVersionOrder` — the reload mechanics: a private copy per load, a metadata fingerprint that spots a rebuild, and the ordering that decides which installed version is active.
- `PluginComparisonPlanFactory` — implements `IComparisonPlanFactory`: turns a run's `PluginComparisonSelection` into an executable plan (comparison definition, enabled steps, a DI scope holding plugin services plus the host services plugins may see).

## Reload

A client can rebuild a plugin with the app running: assemblies are loaded from a private copy under the temp directory, so the installed package is never locked. Clicking **Refresh** in the plugin catalog rescans the plugin directories; a package whose files changed is evicted and reloaded on the next request, and a package with a higher version supersedes a lower one.

Two consequences worth knowing:

- Eviction marks the old load context for collection but does not force it. Plugin types reach process-wide caches (notably the static serializer cache in `Infrastructure`), so a superseded context usually stays in memory until the app exits. The cost is one context per distinct build loaded in a session — refreshing with nothing changed loads nothing.
- The Compare Requests tab keeps the comparison type it resolved when a profile was selected. After rebuilding a plugin, re-select the run profile there so it picks up the new type.

Copying a package can fail transiently if a scanner has the freshly written files open; the copy retries briefly and, if it still fails, the package is reported as an installation failure that a further Refresh retries.

## Boundaries

- References `Application`, `Domain`, `Engine`, and the SDK.
- Loading executes plugin code wherever this assembly is used — including the desktop process, which reads plugin metadata for the catalog UI. Enabling worker-process execution swaps the run executor only; it does not move metadata reads out of the host.

## Tests

Covered by `Tests/ParityBench.NET.Plugins.Tests` (discovery, isolation, plan building, reload).
