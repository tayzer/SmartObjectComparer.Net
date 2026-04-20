# Copilot Agent System For ComparisonTool

## Purpose

This document defines the ComparisonTool GitHub Copilot agent system. The system is coordinator-first: one visible generic coordinator agent routes work to focused specialists for implementation, architecture, UI/UX, documentation, code quality, performance, and testing.

The current implementation target is a personal-profile agent suite in the VS Code user prompts folder. The durable project rules and memory live in this repository.

## Goals

- Keep agent responsibilities focused and loosely coupled.
- Route work based on the active tech stack and the owning repository surface.
- Respect existing project standards and documentation.
- Maintain a single source of truth for durable decisions.
- Require approval before architecture, design, standards, or process changes are implemented.

## Agent Roster

| Agent | Responsibility | Typical Inputs | Typical Outputs |
|------|----------------|----------------|-----------------|
| `Coding-Coordinator` | Orchestration, routing, approval gates, memory discipline | User task, constraints, approval state | Delegation decision, unified plan, final response |
| `ComparisonTool-Implementation` | Feature work, bug fixes, debugging, local code changes | Files, symbols, behavior goals | Focused code change with verification |
| `ComparisonTool-Architecture` | System design, boundaries, contracts, migration planning | Cross-project problem or design question | Options, trade-offs, recommended decision |
| `ComparisonTool-UIUX` | Shared component strategy, layout consistency, interaction design | UI flow, screen, component issue | Proposed or implemented UX change |
| `ComparisonTool-Docs` | Canonical docs, ADRs, design notes, project-memory maintenance | Approved change or documentation request | Updated docs and memory notes |
| `ComparisonTool-Quality` | Maintainability, refactoring, standards enforcement | Quality issue or refactoring target | Behavior-safe refactor and validation |
| `ComparisonTool-Performance` | Hotspot analysis, profiling strategy, optimization plans | Slow path, throughput issue, measurements | Evidence-based optimization guidance |
| `ComparisonTool-Testing` | Test strategy, MSTest alignment, regression coverage | Behavior to validate, failures, touched area | Focused validation plan or test updates |

## Interaction Patterns

### Coordinator-first routing

- The coordinator is the main entry point.
- Specialist agents are hidden by default and are intended to be invoked through the coordinator.
- Cross-agent collaboration is coordinator-mediated to avoid role overlap and circular delegation.

### Handoff packet

Each delegated task should include:

- Goal and success criteria
- Active repository and detected stack
- Relevant files, symbols, and docs
- Scope boundaries and constraints
- Applicable standards and prior decisions
- Approval state
- Required validation
- Expected output shape

### Approval gates

The coordinator must stop for approval before any implementation that changes:

- Architecture or dependency boundaries
- Public contracts or integration patterns
- UI design conventions or shared layout rules
- Coding standards or process rules
- Durable documentation that declares a new final decision

## Project Memory Model

### Canonical durable memory

The authoritative project memory lives in repository-owned files:

- `README.md`
- `CODING_STANDARD.md`
- `docs/`
- `docs/architecture/adr/`
- `docs/design/`

### Distilled working memory

The concise project-memory index lives in `memories/repo/`.

These files summarize durable facts, routing notes, implementation caveats, and links back to the owning source-of-truth documents.

## Update Rules

1. Query existing docs and `memories/repo/` before writing new knowledge.
2. Update the canonical durable document first.
3. Then update the matching `memories/repo/` summary with concise facts.
4. Mark superseded decisions explicitly instead of silently replacing them.
5. Keep tentative ideas in plans or proposals until they are approved.
6. Avoid duplicate ownership. One topic should have one canonical home.

## Supported Query Types

- Code implementation and debugging
- Architecture and system-design analysis
- UI/UX consistency and flow improvements
- Documentation and knowledge updates
- Performance investigation and optimization
- Testing strategy and regression validation

## Example Workflows

### Bug fix workflow

1. Coordinator identifies the owning code path and routes the task to `ComparisonTool-Implementation`.
2. Implementation applies the smallest root-cause fix.
3. Coordinator or Implementation requests `ComparisonTool-Testing` when a focused validation plan or new coverage is needed.
4. If the fix changes durable behavior or troubleshooting knowledge, `ComparisonTool-Docs` updates the relevant doc and `memories/repo/` summary.

### Architecture change workflow

1. Coordinator routes the design question to `ComparisonTool-Architecture`.
2. Architecture returns options, trade-offs, affected surfaces, and a recommended path.
3. Coordinator surfaces the proposal and waits for approval.
4. After approval, Implementation executes the code changes, Testing verifies the change, and Docs records the decision in `docs/architecture/adr/` plus `memories/repo/`.

### UI consistency workflow

1. Coordinator routes the UI issue to `ComparisonTool-UIUX`.
2. UI/UX evaluates whether the change belongs in `ComparisonTool.UI` or a host-specific surface.
3. If the change affects shared design rules, Coordinator requests approval.
4. After approval, UI/UX or Implementation applies the change, and Docs records any reusable pattern in `docs/design/` and `memories/repo/`.

### Performance investigation workflow

1. Coordinator routes the hotspot to `ComparisonTool-Performance`.
2. Performance establishes a baseline and the likely bottleneck.
3. Coordinator involves `ComparisonTool-Testing` when benchmark, regression, or workload validation is needed.
4. If an approved optimization changes durable guidance, Docs records it in the owning doc and memory file.

### Documentation refresh workflow

1. Coordinator routes the request to `ComparisonTool-Docs`.
2. Docs updates the canonical source first.
3. Docs then updates the matching `memories/repo/` summary.
4. Coordinator closes the loop with the updated source of truth and any follow-up actions.