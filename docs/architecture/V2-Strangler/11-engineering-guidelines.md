# V2 Engineering Guidelines

## Purpose

These guidelines apply to all new V2 code for ParityBench.NET.

They are part of the V2 migration contract. A slice is not complete if it moves behavior into V2 while ignoring these standards.

## Test Framework

- Use MSTest for V2 automated tests.
- Use Microsoft Testing Platform for new V2 test projects.
- Do not introduce xUnit, NUnit, VSTest-only projects, or another test framework for V2 without an explicit architecture decision.
- Prefer focused unit tests for Domain, Application, Engine, Workspaces, and Infrastructure behavior.
- Use characterization or parity tests when validating V2 behavior against V1.

## Test Naming

Unit test method names must use:

```text
Action_Scenario_ExpectedBehaviour
```

Examples:

```csharp
StartRun_WhenOptionsAreValid_CreatesPendingRun()
CompareResponses_WhenBodiesMatch_ReturnsEquivalent()
CancelRun_WhenRunIsExecuting_PublishesCancelledState()
```

Use behavior language rather than implementation language. The name should explain what the test proves without needing to read the body first.

## Naming Style

- Do not prefix private fields or variables with underscores.
- Use clear camelCase names for private fields, local variables, and parameters.
- Use PascalCase for public types, public members, records, and enum values.
- Prefer names that describe domain intent over names that describe technical mechanics.

Example:

```csharp
private readonly IRunStore runStore;
```

Avoid:

```csharp
private readonly IRunStore _runStore;
```

## Code Style

- Prefer clean, self-documenting code over explanatory comments.
- Keep methods small enough that their purpose is obvious from the name and structure.
- Make invalid states hard to represent with focused types, records, enums, and value objects.
- Prefer immutable inputs and per-run state over shared mutable services.
- Keep dependencies explicit through constructors.
- Avoid service locator patterns outside host composition roots.
- Keep public behavior easy to test without requiring Web, Desktop, or CLI hosts.

## Third-Party Comparison Dependency

`CompareNETObjects` is the current V1 object comparison engine and should be treated as a Slice 3 parity dependency, not as a Domain or Application contract.

- V2 may use `CompareNETObjects` inside Engine adapters to preserve current difference detection, ignore-rule, smart-ignore, collection-order, string-option, and null/empty collection behavior.
- Domain and Application contracts must use V2-owned comparison option, rule-set, and difference models rather than exposing Kellerman `CompareLogic`, `ComparisonConfig`, `ComparisonResult`, or `Difference` types.
- A future comparer replacement is allowed only after parity tests prove equivalent user-visible behavior for existing comparison options and reports.

## Comments And Documentation

- Comment public interfaces and externally consumed contracts where the caller needs semantic guidance.
- Document non-obvious domain rules, lifecycle guarantees, persistence contracts, and cancellation behavior.
- Avoid comments that restate the code.
- Prefer better names or smaller methods before adding a comment.

## Slice Review Checklist

Before marking a V2 slice complete, check:

- New tests use MSTest and the V2 naming convention.
- Test names follow `Action_Scenario_ExpectedBehaviour`.
- Private fields do not use underscore prefixes.
- Public interfaces and externally consumed contracts have useful comments.
- Code is readable without implementation-comment noise.
- V2 boundaries are preserved.
- V1 parity tests or checks exist where behavior has moved.
