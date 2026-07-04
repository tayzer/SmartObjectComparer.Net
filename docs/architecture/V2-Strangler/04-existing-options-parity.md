# Slice 3: Existing Options Parity

## Goal

Bring across the core comparison options users already rely on.

This slice expands the basic V2 request flow so it can represent and execute the same main options as V1.

## User-Visible Behavior

Users can configure a V2 run with familiar request-comparison options and receive equivalent results.

The expected behavior should match V1 for headers, labels, content types, ignore behavior, masking, and comparison settings.

## Architecture Areas

- Immutable run options.
- Rule-set modeling.
- Host and shared UI input mapping.
- Comparison option application.
- Masking policy.
- Header and content-type policy.

## V1 Parity Expectations

V2 should match V1 behavior for:

- Endpoint labels.
- Common and endpoint-specific headers.
- Content-type override.
- SOAPAction behavior.
- Ignore rules.
- Smart ignores.
- String comparison options.
- XML namespace behavior.
- Collection-order settings.
- Null and empty collection handling.
- Response masking.

## Performance Considerations

Options must be per-run and immutable. V2 should not reintroduce shared mutable comparison configuration.

Masking should operate on artifacts or streams where practical rather than forcing large bodies into memory.

## Completion Criteria

- V2 can express all core V1 request-comparison options.
- Concurrent V2 runs with different options remain isolated.
- Parity tests cover representative combinations of options.
- No host-specific or UI-specific option rules leak into Domain or Engine.

## Non-Goals

- Do not migrate alternate-contract profiles in this slice.
- Do not redesign the user-facing option set.
- Do not add new options unless needed to preserve existing behavior.

