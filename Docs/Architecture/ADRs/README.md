# Architecture Decision Records

Use this directory for approved architecture and systems-design decisions that need durable rationale.

## When To Create An ADR

- A change alters subsystem boundaries or ownership.
- A new integration pattern or public contract is approved.
- A migration plan changes the project's long-term technical direction.
- A performance or hosting constraint leads to a reusable architectural rule.

## Required Sections

- Title
- Date
- Status
- Context
- Decision
- Rationale
- Trade-offs
- Alternatives considered
- Impacted projects or files
- Verification approach
- Supersedes or superseded by

## Rules

- Only record approved decisions as final ADRs.
- Link to the relevant implementation docs, plans, or feature specs.
- If a newer decision replaces an older ADR, mark the older record as superseded.
- After adding or updating an ADR, update the matching summary in `memories/repo/`.