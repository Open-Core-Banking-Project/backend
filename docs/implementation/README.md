# Backend Implementation Instructions

## Purpose

This directory converts product requirements and architecture decisions into executable implementation instructions for engineers and coding agents.

Implementation documentation uses a hybrid structure:

- A phase directory defines release outcomes, sequence, dependencies, and acceptance gates.
- An epic document defines the complete implementation contract for one coherent area of work.
- Tasks and pull requests implement slices of an epic and link back to its requirement and story identifiers.

Do not implement an epic from its title alone. Read the phase overview, the epic instructions, applicable ADRs, service-level `AGENTS.md`, contracts, migrations, and existing tests.

## Directory structure

```text
docs/implementation/
├── README.md
├── EPIC_TEMPLATE.md
├── phase-0-engineering-foundation/
│   ├── 00-phase-overview.md
│   ├── 01-repository-and-build.md
│   ├── 02-local-infrastructure.md
│   ├── 03-identity-and-permission-foundation.md
│   ├── 04-messaging-and-outbox.md
│   └── 05-observability-and-ci.md
├── phase-1-banking-foundation/
│   ├── 00-phase-overview.md
│   └── 01..12 epic implementation documents
├── phase-2-regulated-operations/
│   ├── 00-phase-overview.md
│   └── 01..11 epic implementation documents
├── phase-3-growth-channels/
│   ├── 00-phase-overview.md
│   └── 01..08 epic implementation documents
├── phase-4-advanced-banking/
    ├── 00-phase-overview.md
    ├── 01-treasury-liquidity-and-fx-exposure.md
    └── 02-trade-finance-instruments-and-correspondent-messaging.md
└── phase-5-optimisation/
    ├── 00-phase-overview.md
    ├── 01-enterprise-data-governance-lineage-and-ownership.md
    ├── 02-profitability-and-management-analytics.md
    ├── 03-ai-assisted-service-and-model-governance.md
    └── 04-predictive-operational-analytics.md
```

Phase 5 completes the current roadmap. Any later phase requires approved requirements, allocation and architecture review before being added.

## Sources and precedence

Implementation instructions derive from:

1. Approved user request or work item.
2. Applicable `AGENTS.md` instructions.
3. Approved ADRs and versioned contracts.
4. `docs/architecture/BACKEND_ARCHITECTURE.md`.
5. Software Requirements Specification.
6. User stories and acceptance criteria.
7. Architecture blueprint.

An implementation document must not silently change an approved requirement or architectural decision. Record a conflict or unresolved decision and resolve it through the appropriate approval or ADR process.

## Document states

Use one of these states in front matter:

- `Draft` - incomplete or awaiting key decisions.
- `Ready for review` - complete enough for technical and product review.
- `Approved` - authorised for implementation.
- `In progress` - active implementation exists.
- `Implemented` - code and required evidence are complete.
- `Verified` - acceptance, security, operational, and traceability evidence is approved.
- `Superseded` - replaced by another document or decision.

Only an approved epic should be treated as a stable implementation contract. A draft may support exploration and scaffolding but not irreversible business or data decisions.

## Epic authoring rules

Every epic document must:

- Identify its phase, owning microservice, internal modules, requirements, user stories, dependencies, and status.
- State measurable outcomes rather than only listing components.
- Define included and excluded scope.
- Define roles, permissions, contextual scopes, and maker-checker requirements.
- Describe domain concepts, invariants, state transitions, and failure behaviour.
- Identify APIs, events, database ownership, migrations, idempotency, concurrency, and transaction boundaries.
- Define security, privacy, audit, observability, and recovery expectations.
- Provide ordered implementation slices small enough for review.
- Specify automated evidence and a clear Definition of Done.
- List unresolved decisions and prohibit agents from inventing them.

Use `EPIC_TEMPLATE.md` when creating an epic.

## Traceability

Each implementation slice preserves this chain:

```text
Requirement ID
  -> user story
  -> phase and epic
  -> ADR/design
  -> API/event/database contract
  -> code
  -> automated test
  -> operational or approval evidence
```

Pull requests should list all applicable requirement IDs, story IDs, epic document, migrations, contracts, tests, and verification commands.

## Agent workflow

Before implementation, a coding agent must:

1. Read the root and service-level `AGENTS.md` files.
2. Read the phase overview and epic document.
3. Inspect applicable ADRs, contracts, schemas, and existing patterns.
4. Confirm that the epic is approved or that the user explicitly authorised draft implementation.
5. Identify unresolved decisions that affect money, access, security, regulation, data ownership, or recovery.
6. Implement the smallest complete vertical slice.
7. Build, test, inspect the diff, and update traceability and documentation.

Agents must not infer accounting rules, provider semantics, permission grants, regulatory policy, or production targets from examples.

## Phase overview expectations

A phase overview defines:

- Business outcome and release boundary.
- Included epics and owning services.
- Dependency and implementation order.
- Cross-service contracts required by the phase.
- Entry and exit criteria.
- Security, data, operational, migration, and acceptance gates.
- Decisions that must be resolved before individual epics can become approved.

## Current implementation sequence

```text
Phase 0 - Engineering Foundation
  -> Phase 1 - Banking Foundation
  -> Phase 2 - Regulated Operations
  -> Phase 3 - Growth Channels
  -> Phase 4 - Advanced Banking
  -> Phase 5 - Optimisation
```

Phase 0 creates the safe delivery platform. It does not implement banking products or move money. Phase 1 introduces the first customer and financial capabilities on top of the verified foundation. Phase 2 extends that verified foundation with regulated operational controls, balancing, risk, lending, physical/agent banking and regulatory reporting.
Phase 3 expands governed channels and partner capabilities. Phase 4 adds advanced treasury and trade-finance planning. Phase 5 adds governed optimisation and intelligence, with analytical and model outputs remaining non-authoritative and human-controlled.

