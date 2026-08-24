# Dedicated-machine performance calibration

Desktop runs use the bundled `ParityBench.NET.Worker` process by default. Each saved profile can independently set mapping, comparison, and focused-content workers plus the worker GC mode under **Plugins & Profiles > Performance calibration**. Blank worker fields use safe Auto (`min(8, logical processors, request count)`). Explicit settings are never silently replaced; a hardware-fingerprint mismatch produces a warning.

## Privacy-safe client evidence

Enable one run at a time in Desktop `appsettings.json`:

```json
"EnableStructuralFingerprintExport": true,
"StructuralFingerprintOutputDirectory": "C:\\ParityBenchCalibration"
```

The resulting JSON contains aggregate topology distributions, salted hashes of type/path identifiers, normalization work, and per-pair allocation/timing evidence. It contains no bodies, scalar values, URLs, headers, request filenames, differences, or artifact content. The salt is discarded. Disable the toggle after capture.

## Private retained-artifact replay

Raw artifacts never leave the client machine. On that machine, run:

```powershell
$env:PB_RUN_CLIENT_PLUGIN_FITNESS = '1'
$env:PB_CLIENT_REPLAY_WORKSPACE = 'C:\path\to\workspace'
$env:PB_CLIENT_REPLAY_RUN_ID = 'run-id'
dotnet test --project Tests\ParityBench.ClientCustomerLookupPlugin.Tests\ParityBench.ClientCustomerLookupPlugin.Tests.csproj -c Release -- --filter "FullyQualifiedName~ExecuteAsync_RetainedClientRunReplay"
```

Replay deterministically selects the first 1,000 pairs whose two artifacts still exist, calls no comparison endpoints, writes into a temporary workspace, verifies the ordered output hash, and deletes the temporary workspace. If fewer than 1,000 pairs survive, the test reports that the next run must retain a private calibration sample.

To capture that sample, temporarily set these Desktop observability settings, run once, then immediately turn the toggle off:

```json
"CaptureNextRunForCalibration": true,
"CalibrationCaptureOutputDirectory": null
```

The worker copies only the first 1,000 raw response pairs before retention. The default private location is `%LOCALAPPDATA%\ParityBench.NET\CalibrationSamples\<run-id>`. It is not an export: it contains response bodies and must stay on the client machine. Replay automatically uses this sample when retained artifacts are missing and deletes the owned run-specific sample after calibration. Set `PB_CLIENT_REPLAY_CAPTURE` only when a non-default capture directory was configured.

## Fresh-process calibration matrix

Set `PB_CLIENT_STRUCTURAL_FINGERPRINT` to the exported fingerprint, then run the opt-in fitness test:

```powershell
$env:PB_RUN_CLIENT_PLUGIN_FITNESS = '1'
$env:PB_CLIENT_STRUCTURAL_FINGERPRINT = 'C:\ParityBenchCalibration\structural-fingerprint-run-id.json'
$env:PB_CLIENT_PLUGIN_FITNESS_TRACE = '1' # optional; requires dotnet-trace on PATH
dotnet test --project Tests\ParityBench.ClientCustomerLookupPlugin.Tests\ParityBench.ClientCustomerLookupPlugin.Tests.csproj -c Release -- --filter "FullyQualifiedName~ExecuteAsync_RealClientPlugin_Fitness"
```

Each candidate runs in a fresh process. The first pass covers Workstation, Server Adaptive/DATAS, and Server Fixed with 4/8/12 heaps at comparison concurrency 4/8/12. Subsequent coordinate-search passes test mapping and focused workers at 4/8/12/16. Reports and optional `.nettrace` files go to `%LOCALAPPDATA%\ParityBench.NET\Performance` unless `PB_PERFORMANCE_OUTPUT` is set.

The harness rejects output/retention changes, unrepresentative structure or offline evidence, memory-budget violations, poor post-run release, normalization/allocation gate failures, an 8k median over ten minutes, or 8k throughput below 85% of 2.5k. Save the reported winning tuple explicitly into the validation/client profile before deployment.
