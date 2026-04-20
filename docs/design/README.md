# Design Memory

Use this directory for durable UI/UX guidance that should be reused across ComparisonTool surfaces.

## What Belongs Here

- Shared interaction patterns
- Layout and navigation decisions
- Reusable component guidance
- Design-token or styling rules that multiple surfaces should follow
- Host-specific UI constraints when they affect shared design behavior

## Rules

- Prefer `ComparisonTool.UI` as the reuse boundary when the workflow is shared.
- Respect the established visual language unless a proposed change is explicitly approved.
- Record why a reusable design rule exists, not just what it looks like.
- Link related implementation surfaces and memory files.
- After an approved reusable design change, update the matching summary in `memories/repo/`.