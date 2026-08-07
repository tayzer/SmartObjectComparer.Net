# ParityBench.NET.Worker.Tests

Tests for out-of-process run execution and the host↔worker protocol.

## Covers

- **`WorkerProtocolTests`** — message framing and round-tripping of the request, progress, and result messages exchanged over the named pipe.
- **`WorkerChannelTests`** — named-pipe transport behaviour: buffering, partial reads, peer disconnect, and disposal without flushing a possibly-dead pipe.
- **`WorkerComparisonRunExecutorTests`** — the host-side executor: launching the worker, relaying progress to the caller, surfacing worker faults as run failures rather than host crashes, and cancellation.

## Boundaries

- Marked `[assembly: DoNotParallelize]` (`AssemblyInfo.cs`). Pipe names and process launches are process-global; running these in parallel produces flaky cross-talk. Do not remove it.

## Run

Run from the physical `Tests` directory so `Tests/global.json` selects Microsoft Testing Platform:

```powershell
dotnet test --project ParityBench.NET.Worker.Tests\ParityBench.NET.Worker.Tests.csproj -v:minimal
```
