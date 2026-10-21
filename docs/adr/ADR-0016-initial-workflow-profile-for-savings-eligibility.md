---
id: ADR-0016
title: "Initial workflow profile for Savings eligibility publication"
status: Accepted
date: 2026-10-21
owners: [Product, Operations, Architecture, Security]
required_by: "Phase 1 - Workflow and Maker-Checker; Products and Accounts"
architecture_item: null
referenced_by:
  - docs/implementation/phase-1-banking-foundation/04-products-and-accounts.md
  - docs/implementation/phase-1-banking-foundation/06-workflow-and-maker-checker.md
supersedes: []
approved_by: [Repository Owner]
approved_on: 2026-10-21
---

# ADR-0016: Initial workflow profile for Savings eligibility publication

## Status

`Accepted` on 2026-10-21 by the repository owner for the narrow profile below.

## Context

CFG-007, ACC-010, IAM-005 through IAM-007, WFL-001, WFL-002, US-003, US-007 and US-048 require exact-version maker-checker approval before product configuration publication. The generic Workflow epic remains Draft because its general definition language, thresholds, quorum, SLA, delegation, case and override policies are unresolved. Savings eligibility cannot safely expose submission or publication without a concrete first approval profile.

## Decision

1. The first enabled profile is `product.savings.eligibility.publish.v1` with immutable workflow version `savings-eligibility-publication-v1`.
2. It is a single-stage, one-level maker-checker approval. The maker's stable actor ID is excluded regardless of role, session or channel.
3. The decision permission is `product.version.approve`. Role names, client-selected candidates and assignment do not authorise a decision.
4. The request is bound to institution, source service, subject type, subject ID, subject version and canonical SHA-256 content hash. Any source mutation requires a new linked request; an existing approval cannot be retargeted.
5. Identity and Access must revalidate the approver's current institution, permission, scope and conflicts at decision time. Workflow receives an entitlement version and a validity interval containing the decision instant and fails closed otherwise.
6. Approval and rejection are immutable. Rejection requires a non-empty reason. One valid final decision completes the profile.
7. Workflow emits the exact decision at least once with a stable decision ID. Core Ledger applies it idempotently only to the matching pending subject version/hash and records application separately. `Approved` never means `Applied`.
8. No amount threshold, SLA, escalation, delegation, reassignment, parallel stage, quorum, multi-level route or arbitrary expression language is enabled by this decision.

## Alternatives considered

| Option | Why not chosen |
| --- | --- |
| Wait for the complete generic Workflow epic | It would block a narrow, already-approved Savings configuration path on unrelated case, SLA and quorum decisions |
| Approve inside Core Ledger without Workflow | It would duplicate workflow authority and weaken reusable maker-checker evidence |
| Route by hard-coded role name | Roles are configurable permission bundles and are not an authorisation boundary |
| Accept entitlement evidence captured at submission | Access or conflicts may change before the decision; decision-time evidence is required |

## Consequences

- Workflow can implement a safe typed request/decision foundation and Core Ledger can later integrate exact decision application.
- The initial profile cannot express thresholds, escalations or multiple approvers.
- A future generic definition schema must preserve compatibility with running requests pinned to this profile/version.
- HTTP exposure remains disabled until authenticated source and human decision application services enforce this ADR end to end.

## Compliance and evidence

- Domain tests prove maker exclusion, exact permission, current entitlement evidence, immutable decisions and mandatory rejection reasons.
- Contract tests prove request/decision messages preserve exact subject version/hash and decision identity.
- Persistence tests prove tenant filters, immutable request/decision identity and unique final decision per request.
- Source application tests must prove stale hash, replay and cross-tenant decisions fail closed before routes are enabled.

## Open questions

- Generic workflow definition schema and additional approval patterns.
- Candidate discovery, caching, delegation and reassignment policy.
- SLA calendars, reminders and escalation.
- General workflow retention and operational capacity targets.

## Approval gate

This ADR permits only the typed `product.savings.eligibility.publish.v1` profile. Other workflows and advanced approval patterns remain inaccessible until separately approved.

## Related documents

- [ADR index](README.md)
- [Savings eligibility decision](ADR-0015-phase-1-savings-eligibility-and-freshness.md)
- [Workflow epic](../implementation/phase-1-banking-foundation/06-workflow-and-maker-checker.md)
- [Products and Accounts epic](../implementation/phase-1-banking-foundation/04-products-and-accounts.md)
