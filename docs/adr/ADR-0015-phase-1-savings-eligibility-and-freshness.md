---
id: ADR-0015
title: "Phase 1 Savings eligibility and final-approval freshness"
status: Accepted
date: 2026-10-20
owners: [Product, Architecture, Compliance]
required_by: "Phase 1 - Products and Accounts"
architecture_item: null
referenced_by:
  - docs/implementation/phase-1-banking-foundation/04-products-and-accounts.md
supersedes: []
approved_by: [Repository Owner]
approved_on: 2026-10-20
---

# ADR-0015: Phase 1 Savings eligibility and final-approval freshness

## Status

`Accepted` on 2026-10-20 by the repository owner for the narrow Phase 1 scope defined below.

## Context

CFG-005, CFG-007, ACC-001 through ACC-003, US-003 and US-016 require controlled product configuration and server-side account eligibility. The Products and Accounts epic requires product, customer status, KYC tier, age, segment, currency and branch eligibility to use authoritative versioned inputs. It prohibits opening from stale facts and leaves both the first live product family and final eligibility recheck point unresolved.

The configuration-lifecycle foundation already binds simulation evidence to an exact canonical content hash and prevents failed or stale simulation evidence from reaching approval. A narrow live family and freshness rule are now required before persistence and account-application contracts can be enabled.

## Decision

1. `Savings` is the only product family enabled for live Phase 1 eligibility. Other modelled families remain unavailable until separately approved.
2. Savings eligibility uses the typed `product_eligibility.v1` schema. It contains explicit criteria for customer type, KYC tier, segment, risk rating, age in completed years supplied by the authoritative customer source, branch, residency country and currency.
3. Every code criterion explicitly declares either `any` or a non-empty allow-list. An empty allow-list never means unrestricted.
4. Configuration simulation uses versioned synthetic or approved inputs, records an exact content hash, stable input/output hashes and a per-rule trace, and must pass before submission.
5. Final account approval re-fetches and re-evaluates current authoritative customer, branch and currency facts. Each source supplies its version and validity. Missing or expired facts fail closed with `eligibility_facts_stale`; a current but ineligible result fails with `product_eligibility_failed`.
6. A prior eligible result is evidence only. It cannot authorise final approval after its authoritative facts expire or change.
7. Eligibility evaluation does not open an account, allocate an account number, initialise a ledger account, mutate a balance or create a posting.
8. Product codes are defined by each institution. They are stable, case-normalised identifiers of at most 64 ASCII letters, digits, periods, underscores or hyphens; the same code may exist in different institutions, but a code is unique within an institution's Product area.
9. A published product configuration version has a half-open effective period `[effective_from, effective_to)`. `effective_to` may be absent. Adjacent periods are allowed; overlapping periods for the same institution and product code are prohibited.
10. As-of selection uses the requested instant against `effective_from` and `effective_to` and must resolve exactly one published version. Missing or multiple matches fail closed.
11. Publication reads existing periods and advances the configuration-set optimistic-concurrency token in one local transaction. A concurrency conflict reloads and rechecks the schedule; it never blindly retries publication.

## Alternatives considered

| Option | Why not chosen |
| --- | --- |
| Enable every product family represented in the requirements | Their lifecycle and monetary semantics remain unapproved and would create accidental Phase 2 capability |
| Reuse the eligibility result captured when an application was created | Mutable customer, branch or currency facts could become stale before approval |
| Treat empty criteria as unrestricted | A missing configuration could silently become permissive |
| Compute age from date of birth inside the product engine | Leap-day and jurisdictional age semantics are not approved; the authoritative customer source supplies completed age and its fact version |
| Use one platform-global product-code catalogue | Institutions require independent catalogues; tenant-scoped uniqueness preserves isolation without coupling their naming |
| Treat `effective_to` as inclusive | Inclusive endpoints make adjacent versions ambiguous at the boundary; half-open periods select exactly one version |
| Permit intentionally coexisting product versions | No approved discriminator exists to choose between overlapping versions, so coexistence would make account-opening behaviour ambiguous |

## Consequences

- Savings product configuration and simulations can progress behind typed contracts.
- Account final approval requires fresh source reads and must remain pending or fail safely during source unavailability.
- Current, term-deposit, wallet, notice-deposit and target-savings families remain inaccessible.
- Consumers can deterministically select the Savings configuration effective at an instant, including exact boundary instants.
- Publication command persistence must use the configuration-set row as its optimistic-concurrency boundary and re-evaluate overlaps after any conflict.
- This decision does not approve rates, fees, taxes, limits, interest, account-number formats or financial posting.

## Compliance and evidence

- Domain tests prove unsupported families are rejected.
- Simulation tests cover explicit criteria, inclusive configured age boundaries, missing inputs, stable hashes and expected eligible/ineligible outcomes.
- Final-approval tests prove expired facts fail closed and current facts are re-evaluated with exact source versions.
- Effective-period tests prove half-open boundary selection, adjacent periods, overlap rejection and fail-closed detection of ambiguous persisted data.
- Persistence-model tests prove tenant-scoped product-area uniqueness and the configuration-set concurrency token used to serialize publication.
- Architecture tests continue to prevent product eligibility code from depending on infrastructure or posting boundaries.

## Open questions

- Authoritative source contracts and validity windows for customer, branch and currency facts.
- Account application, approval and activation workflow sequencing.
- Multiple-account-per-product/customer policy.

## Approval gate

This ADR permits typed Savings eligibility configuration, simulation, persistence and non-financial evaluation. Do not enable another product family or implement rates, fees, taxes, interest, limits, account numbering, account activation or posting based on this decision.

## Related documents

- [ADR index](README.md)
- [Products and Accounts epic](../implementation/phase-1-banking-foundation/04-products-and-accounts.md)
- [Institution and Tenancy epic](../implementation/phase-1-banking-foundation/01-institution-and-tenancy.md)
- [Backend architecture](../architecture/BACKEND_ARCHITECTURE.md)
