# Slice 2: Basic Request A/B Flow

## Goal

Rebuild the simplest useful request-comparison path in V2.

This slice covers request input staging, endpoint A/B execution, response artifact persistence, basic outcome classification, and run summary generation.

## User-Visible Behavior

A user can run a small A/B comparison against two endpoints and receive a basic result.

At this stage, advanced options may be incomplete. The purpose is to prove the vertical path from input to execution to summary.

## Architecture Areas

- Application start-run use case.
- Request work-item planning.
- Endpoint pair execution.
- Request and response body persistence through `ParityBench.NET.Workspaces` artifact contracts.
- Basic response classification.
- Summary assembly.

## V1 Parity Expectations

V2 should match V1 for a simple happy-path request batch:

- Same logical request identity.
- Endpoint A and endpoint B are both called.
- Responses are persisted.
- Successful pairs are classified as comparable.
- A basic run summary is produced.

## Performance Considerations

Response persistence should be designed around streaming from this slice, even if the first implementation uses a simple adapter. Avoid making full-body buffering part of the architecture contract.

## Completion Criteria

- V2 can execute a small request batch end to end.
- Response artifacts are stored through the `ParityBench.NET.Workspaces` artifact abstraction.
- The run summary can be loaded without reading raw response bodies.
- V1 parity is checked for a simple happy-path run.

## Non-Goals

- Do not migrate all request options.
- Do not migrate alternate contracts.
- Do not build final report output.
- Do not switch Web, Desktop, or CLI to V2 by default.

