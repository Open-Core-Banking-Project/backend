---
phase: "Phase 5 - Optimisation"
epic: "Profitability and Management Analytics"
owning_service: "Risk and Reporting, with Finance definitions and Core Banking and Ledger source authority"
modules: [Profitability, Allocations, Management Analytics, Reconciliation, Restatement]
status: Draft
requirements: [ARC-001..ARC-006, RPT-003..RPT-006, REP-001..REP-003, DAT-001..DAT-008, SEC-001..SEC-018, AUD-001..AUD-004]
stories: []
dependencies: [Epic 01 Data Governance, Approved Finance Methodology, Ledger and Product Data Contracts, Reporting]
adrs: [Profitability methodology, Analytical calculation ownership, Currency and allocation policy]
last_updated: 2026-10-16
---

# Epic 02: Profitability and Management Analytics

## 1. Objective

Provide authorised management and Finance users with reproducible profitability analysis by approved institution, product, customer segment, branch and other governed dimensions, reconciled to approved finance sources and explicitly non-authoritative for accounting and balance state.

## 2. Success measures

- Every published result identifies source watermark, calculation/allocation version, period, currency treatment and reconciliation state.
- Revenue, expense and allocation totals reconcile to approved inputs within approved tolerances; residuals remain visible.
- Restatements create new versions and preserve prior publication/evidence.
- Access to customer/account-level profitability is purpose-limited, masked and audited.
- Approved reports meet measured freshness, accuracy, performance and recovery targets.

## 3. Scope

Included: governed profitability components, dimensions, allocation drivers/rules, period snapshots, currency conversion, reconciliation, variance, restatement, secure management views/exports and evidence. Excluded: inventing accounting or funds-transfer-pricing policy, direct pricing/fee/balance changes, replacing statutory P&L, uncontrolled spreadsheet adjustments, and profitability-driven automated customer treatment.

## 4. Actors, roles and permissions

| Actor | Permission | Scope | Approval |
| --- | --- | --- | --- |
| Finance analyst | `analytics.profitability.calculate` | Institution/period/dimension | Run only |
| Finance approver | `analytics.profitability.publish` | Period/methodology | Maker-checker |
| Management viewer | `analytics.profitability.view` | Approved aggregate scope | None |
| Data steward | `analytics.profitability.source-review` | Registered assets/quality | Independent |
| Auditor | `analytics.profitability.evidence-view` | Publication/run | Read-only |

Customer/account detail, export and restatement have separate permissions. The calculation maker cannot solely approve publication or their own manual adjustment.

## 5. Domain model

| Concept | Responsibility | Important rules |
| --- | --- | --- |
| `ProfitabilityMethodology` | Definitions, dimensions and component policy | Approved, effective-dated version |
| `AllocationRule` | Driver, source pool and target dimensions | Totals conserved; residual explicit |
| `ProfitabilityRun` | Fixed period/watermarks/methodology | Reproducible and idempotent |
| `ProfitabilityComponent` | Revenue/cost/funding/capital/tax item | Source and currency trace retained |
| `ReconciliationResult` | Input/output/control comparison | Exceptions cannot be hidden |
| `Publication/Restatement` | Approved output version | Prior publications immutable |

Invariants: analytical results never post; money uses `decimal` and explicit currency; conversion retains original/reporting amounts and approved rate; allocations conserve approved source totals; no double counting across source components; published result requires reconciled or explicitly approved exception state.

## 6. Use cases

### UC-01 - Configure methodology

Define approved components, dimensions, hierarchies, allocation drivers, currency/rate rules, tolerance and effective period; review and publish.

### UC-02 - Calculate and reconcile profitability

Freeze source watermarks, validate quality, calculate components/allocations, reconcile totals and record exceptions.

### UC-03 - Review and publish

Compare variances and control totals, approve exact run digest, publish governed aggregates/details and notify consumers.

### UC-04 - Restate a period

Create successor run with changed source or policy, explain differences, reapprove and preserve all earlier versions.

## 7. API contracts

| Method | Route | Permission | Idempotent |
| --- | --- | --- | --- |
| POST | `/api/v1/analytics/profitability/methodologies` | `analytics.profitability.configure` | Version key |
| POST | `/api/v1/analytics/profitability/runs` | `analytics.profitability.calculate` | Period/methodology/watermark |
| GET | `/api/v1/analytics/profitability/runs/{id}` | `analytics.profitability.view` | Yes |
| POST | `/api/v1/analytics/profitability/runs/{id}/decisions` | `analytics.profitability.publish` | Decision key |
| GET | `/api/v1/analytics/profitability/results` | `analytics.profitability.view` | Yes |
| POST | `/api/v1/analytics/profitability/restatements` | `analytics.profitability.restate` | Original/reason key |

Responses expose source/as-of, policy, currency, reconciliation and publication status. Stable errors cover source quality, unmapped dimension, currency/rate gap, allocation imbalance, tolerance breach, stale version, authority and self-approval.

## 8. Event contracts

Consume approved ledger/reporting facts, products/accounts/customer-segment references, rates and governance/quality state. Produce `ProfitabilityRunCompleted/Failed`, `ProfitabilityExceptionOpened/Resolved`, `ProfitabilityPublicationApproved`, `ProfitabilityPeriodRestated`. Order by institution/period/methodology; use outbox/inbox and minimum aggregated data.

## 9. Database design

Risk and Reporting owns methodologies, versions, dimensions/hierarchies, components, allocation rules/drivers, run metadata/checkpoints, source watermarks, result partitions, control totals, reconciliation exceptions, decisions, publications and restatement links. Constraints enforce tenant/period/version/run uniqueness, explicit numeric precision/currency, immutable approved policy/publications, balanced allocation controls and no cross-service foreign keys. High-volume results use approved partition/retention strategy.

## 10. Consistency, concurrency and idempotency

Runs bind immutable policy and source watermarks. Run metadata/checkpoint/outbox commit locally; partition output is published atomically through a manifest only after validation. Concurrent identical starts return the existing run. Publication uses optimistic version/digest. Source changes require a new run/restatement; no distributed transaction and no live cross-database joins.

## 11. Security and privacy

Enforce institution, legal entity, branch, dimension, granularity, classification, purpose and export scopes. Apply aggregation/minimum cohort and masking policies where approved. Prevent inference through repeated filters, formula injection in exports and unbounded extraction. Test cross-tenant/dimension access, parameter tampering, suppressed-cell reconstruction, unauthorised restatement and sensitive logs.

## 12. Audit and observability

Audit methodology/rule changes, manual inputs, source selections, run/retry, reconciliation, exception, decision, publication, export and restatement. Metrics: source freshness, run duration/failure, unmapped items, allocation residual, reconciliation variance, publication age, query latency and export volume. Traces link result to run/policy/source manifests without customer data.

## 13. Failure, retry and recovery

Missing/stale/untrusted source blocks run or publication according to approved policy. Item/partition failures resume from deterministic checkpoints. Partial output remains inaccessible until manifest publication. Restore verifies policies, manifests, control totals, publications, restatement chain and audit evidence. Recalculation never overwrites a published version.

## 14. Configuration

Version/approve dimensions, component/source mappings, signs, allocation pools/drivers/order, currency/rate hierarchy, rounding, tolerance, close/calendar, publication detail, cohort/masking, schedules, retention and capacity. Finance policy is data, not hard-coded defaults.

## 15. Implementation slices

1. Baseline detailed requirements, Finance methodology and ownership ADRs.
2. Governed dimensions/components/source manifests and validation.
3. Versioned allocation engine with golden conservation tests.
4. Run/checkpoint/result storage and reconciliation controls.
5. Review/publication/restatement and protected query/export APIs.
6. Performance, audit, dashboards, restore and Finance acceptance evidence.

## 16. Test plan

| ID | Scenario | Expected evidence |
| --- | --- | --- |
| PFA-01 | Approved component calculation | Reproducible source/policy result |
| PFA-02 | Allocation across dimensions | Totals conserved; rounding/residual explicit |
| PFA-03 | Multi-currency result | Original/reporting amounts and rate retained |
| PFA-04 | Missing/untrusted source | Run/publication blocked |
| PFA-05 | Duplicate/concurrent run | One logical run/version |
| PFA-06 | Reconciliation variance | Exception remains open |
| PFA-07 | Restatement | Prior publication preserved and difference explained |
| PFA-08 | Detail/export access | Granularity/purpose/masking enforced |
| PFA-09 | Cross-tenant query | Non-disclosing denial |
| PFA-10 | Restore/recompute | Manifest/control totals reproduce |

## 17. Acceptance criteria

- [ ] Detailed profitability requirements and measurable outcomes are approved.
- [ ] Results use approved definitions, dimensions, sources, allocations and currency treatment.
- [ ] Calculations reconcile and retain visible residuals/exceptions.
- [ ] Publications/restatements are versioned, approved, reproducible and audited.
- [ ] Results remain analytical and cannot directly change financial or customer state.

## 18. Definition of Done

Repository DoD plus approved Finance and architecture ADRs, golden calculations, migrations, contracts, reconciliation and security tests, representative performance evidence, restore/recompute rehearsal, dashboards/runbooks and Finance/Data Governance approval.

## 19. Open decisions

Use cases/stories, profit definition, component/source hierarchy, dimensions, allocation/funds-transfer-pricing/capital/tax methods, currency/rates/rounding, close/restatement, tolerances, detail/privacy, target freshness/volume/performance and publication consumers remain open.

## 20. Prohibited assumptions

Do not invent profit or accounting rules, use `double`, allocate unreconciled totals, overwrite publications, hide residuals, join source databases, treat result as statutory ledger truth, or automate pricing/customer treatment from profitability output.

## 21. Agent implementation instructions

Read Phase 5 overview, Epic 01, ledger/reporting/accounting-period epics and approved Finance decisions. Use C#/.NET in Risk and Reporting; Ledger remains read-only through contracts. Begin only after detailed requirements approval. Run build, migration, calculation golden, contract, concurrency, reconciliation, tenant/privacy, performance and recovery tests. Stop if any methodology, source authority or currency rule is unresolved.

