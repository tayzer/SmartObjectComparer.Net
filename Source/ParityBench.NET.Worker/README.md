# ParityBench.NET.Worker

Out-of-process run executor. Client plugin code runs here, never in the host.

## Owns

- The worker entry point: parses `--workspace --run --pipe --fixture-base-url`, builds the same workspace composition the host uses, loads the run the host already persisted, executes it in-process, and streams progress + the final summary back over the named pipe.
- Cooperative cancellation: watches for a host `cancel` frame and unwinds the executor.

## How it is used

The host launches this executable per run through `WorkerComparisonRunExecutor` (in Infrastructure) when `Worker:Enabled=true`. Artifacts and paged details are written to the same workspace, so only the summary crosses the pipe and large-run memory stays bounded. A crash, hang, or missing terminal frame fails the run with the worker's stderr captured — the host is unaffected.

The wire protocol (`WorkerProtocol`, `WorkerChannel`) lives in `Application/Runs/Worker`, shared by both sides.

## Boundaries

- References `Composition` (to build the full graph) and logging/config.
- Must not itself opt into worker execution — it registers the in-process `ComparisonRunExecutor`, so it never spawns a worker of its own.

## Tests

Covered by `Tests/ParityBench.NET.Worker.Tests` (protocol/channel units + end-to-end launch and containment).
