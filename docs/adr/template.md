---
id: ADR-NNNN
title: "Decision title"
status: Proposed
date: YYYY-MM-DD
owners: []
required_by: ""
architecture_item: null
referenced_by: []
supersedes: []
approved_by: []
approved_on: null
---

# ADR-NNNN: Decision title

## Status

`Proposed` - replace with `Accepted`, `Rejected` or `Superseded by ADR-NNNN` when decided. Keep this line in sync with the index in [README.md](README.md).

## Context

State the forces that require a decision: the governing requirements, the current repository or architectural state, the constraints that cannot be changed, and the epic documents that are blocked until the decision exists. Cite requirement, story and epic identifiers.

## Decision

State the decision precisely enough that two engineers would implement the same thing. Where the decision fixes a value or a mechanism, give it: versions, protocols, limits, ownership, and where it is enforced.

If the decision is not yet taken, list exactly what it must settle. Do not leave the section empty and do not present an option as though it were decided.

## Alternatives considered

| Option | Why not chosen |
| --- | --- |

## Consequences

- What becomes easier, and what becomes harder or impossible.
- Constraints this places on epics, contracts, migrations, operations and tests.
- Cost, licensing, operational or vendor implications.
- What is explicitly out of scope for this decision.

## Compliance and evidence

State how compliance with the decision is verified: architecture tests, analyzer rules, contract tests, migration checks, review checklists or operational evidence.

## Open questions

- List unresolved points that must be settled before, or shortly after, approval.

## Approval gate

State what implementation must stop until this ADR is `Accepted`, and what work may continue safely.

## Related documents

- [ADR index](README.md)
- Link the architecture document, epics, requirement identifiers and any external standard that constrains the decision.
