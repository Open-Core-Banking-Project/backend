---
phase: "Phase 4 - Advanced Banking"
epic: "Treasury Liquidity and FX Exposure"
owning_service: "Requires treasury-boundary ADR; coordinated by Risk and Reporting with Core Banking and Ledger"
modules: [Cash Positions, Maturity Gaps, FX Positions, Exposure Limits, Revaluation]
status: Draft
requirements: [LED-001..LED-013, RSK-001..RSK-008, REC-005..REC-010, DAT-003..DAT-007, AUD-001..AUD-004]
stories: [US-077]
dependencies: [Verified Ledger EOD Reconciliation Risk Reporting and Provider Foundations]
adrs: [Treasury ownership, Position and maturity methodology, Market data and FX valuation, Revaluation accounting]
last_updated: 2026-10-16
---

# Epic 01: Treasury Liquidity and FX Exposure

## 1. Objective

Give authorised treasury staff reconciled cash positions, maturity gaps, FX positions and exposure-limit monitoring, with configurable breach alerts and approved end-of-day revaluation postings.

## 2. Success measures

- Positions reconcile to approved ledger accounts, contractual cash flows and versioned market data.
- Maturity/FX aggregation is reproducible by institution, legal entity, book, currency and approved dimensions.
- Limit versions, utilization, breaches, acknowledgement, escalation and overrides are auditable.
- EOD revaluation posts balanced approved accounting entries once and reconciles to the position snapshot.

## 3. Scope

Included: treasury books/scopes, cash-position read models, maturity buckets, FX positions, rate/market-data versions, exposure limits/breaches, EOD revaluation orchestration, reports and reconciliation. Excluded: trade execution/dealing, securities portfolio management, liquidity transfer pricing, hedge accounting and stress models unless separately baselined.

## 4. Actors, roles and permissions

| Actor | Permission | Contextual scope | Step-up | Approval |
| --- | --- | --- | --- | --- |
| Treasury officer | `treasury.position.view` | Institution/entity/book/currency | Sensitive export | None |
| Limit manager | `treasury.limit.manage` | Risk type/book/currency | Publish/override | Maker-checker |
| Market-data operator | `treasury.market_data.manage` | Source/instrument/currency pair | Required | Maker-checker |
| Revaluation operator | `treasury.revaluation.execute` | Business date/book | Required | Approved run |
| Finance approver | `treasury.revaluation.approve` | Run/accounting scope | Required | Cannot self-approve |

Identity and Access owns grants; each owner service enforces entity/book/currency/amount scope. Limit override and manual rate selection require reason, expiry and independent approval.

## 5. Domain model

| Concept | Responsibility | Key rules |
| --- | --- | --- |
| `TreasuryBook` | Approved position/limit scope | One institution/legal entity; versioned ownership |
| `PositionSnapshot` | Reconciled as-of cash/FX view | Immutable source/version/digest |
| `MaturityBucketPolicy` | Maps cash-flow dates to bands | Approved version, no silent rebucketing |
| `MarketDataObservation` | Rate/curve and quality evidence | Source, pair, time, precision, staleness |
| `ExposureLimitVersion` | Thresholds and aggregation scope | Effective-dated and approved |
| `LimitUtilization/Breach` | Evaluated position against limit | Explainable inputs; open until resolved/accepted |
| `RevaluationRun/Item` | Rate-based EOD adjustment | One run item/posting outcome per scope/date |

State: snapshot `Building -> Reconciling -> Approved/Rejected`; breach `Open -> Acknowledged -> Escalated -> Resolved/AcceptedWithAuthority`; revaluation `Planned -> Validating -> ApprovalPending -> Posting -> Completed/Failed/Uncertain -> Reconciled`.

Invariants: position view is non-authoritative; original/base currency and rate evidence are explicit; stale/missing rates follow approved fail policy; revaluation entries balance and never rewrite source transactions; same book/date/rule version cannot post twice; limit breach cannot be hidden by reprocessing.

## 6. Use cases

### UC-01 - Build and reconcile positions

Consume approved ledger/cash-flow facts, market-data references and book mappings; build snapshot; reconcile totals and expose breaks before approval.

### UC-02 - Evaluate limits and breaches

Apply exact limit/maturity policy version, create/deduplicate breach, alert/escalate and record authorised acknowledgement/override.

### UC-03 - Execute EOD revaluation

Freeze approved snapshot/rates/mappings, simulate entries, obtain approval, submit idempotent ledger postings and reconcile results.

### UC-04 - View/export treasury exposure

Apply permissions and masking; display source/as-of/quality/limit status; audit sensitive export.

## 7. API contracts

| Method | Route | Permission | Idempotent |
| --- | --- | --- | --- |
| GET | `/api/v1/treasury/positions` | `treasury.position.view` | Yes |
| GET | `/api/v1/treasury/maturity-gaps` | `treasury.position.view` | Yes |
| POST | `/api/v1/treasury/limits` | `treasury.limit.manage` | Request key |
| POST | `/api/v1/treasury/breaches/{id}/actions` | `treasury.breach.manage` | Action key |
| POST | `/api/v1/treasury/revaluations` | `treasury.revaluation.execute` | Book/date/policy key |
| POST | `/api/v1/treasury/revaluations/{id}/decisions` | `treasury.revaluation.approve` | Decision key |

Responses state as-of time, business date, source/quality, original/base currency and reconciliation status. Stable errors cover stale data, missing rate, unmapped account, limit conflict, period lock, approval and uncertain posting.

## 8. Event contracts

Consume approved ledger/account/cash-flow, period/business-date and market-data facts. Produce `TreasuryPositionSnapshotApproved`, `ExposureLimitPublished`, `ExposureLimitBreached`, `RevaluationRequested/Completed/Failed`, `TreasuryPositionBreakOpened`. Use event version, source snapshot/rate/policy IDs, outbox/inbox, minimal data and ordering by book/run.

## 9. Database design

No migration before treasury-owner ADR. Candidate owner stores books/mappings, snapshot metadata/items, maturity policies, market-data references/quality, limit versions/utilization/breaches, revaluation runs/items and posting refs. Raw/provider market data belongs to approved Integration Hub storage; ledger data remains authoritative in `core_ledger_db`.

Constraints: tenant/entity/book scope, explicit decimals/currencies/rate precision, unique source observation and book/date/snapshot/run keys, immutable approved snapshots/rates/limit versions, optimistic breach/run transitions, approved retention/partitioning. No cross-service foreign keys.

## 10. Consistency, concurrency and idempotency

Snapshot/run metadata and outbox commit locally. Source watermarks freeze reproducible inputs. Concurrent builders converge on unique book/as-of/policy key. Revaluation uses book/date/item/accounting-component idempotency and authoritative ledger outcome. No distributed transaction; missing/uncertain postings remain visible reconciliation items.

## 11. Security and privacy

Protect sensitive liquidity, counterparty, position and limit data. Enforce entity/book/currency scope, step-up, export control and maker-checker. Verify market-data source/signature where supported. Test cross-tenant/book access, rate injection, limit suppression, stale-data use, approval swapping and export leakage.

## 12. Audit and observability

Audit mappings, rate/source selection, snapshot approval, limit versions, breaches/actions/overrides, revaluation simulation/approval/posting and exports. Metrics: source/snapshot age, reconciliation breaks, limit utilization/breach age, missing/stale rates, run duration/failure/uncertainty. Alerts link runbooks without position payloads.

## 13. Failure, retry and recovery

Missing/stale data blocks or marks snapshot degraded only per approved policy. Provider timeout never invents rate. Failed run resumes from durable item checkpoints. Ledger timeout stays uncertain and is queried/reconciled. Restore verifies snapshot digests, active limits, open breaches, runs and postings.

## 14. Configuration

Version/approve books/entities/accounts, position dimensions, maturity buckets, currency pairs, rate/curve hierarchy, quality/staleness/fallback, precision/rounding, limits/aggregation, breach escalation, revaluation/accounting mappings, schedules and retention.

## 15. Implementation slices

1. Baseline detailed requirements and ownership/accounting ADRs.
2. Treasury book/account/cash-flow contracts and snapshot reconciliation.
3. Market-data canonical model, quality and rate selection.
4. Maturity/FX calculations and golden tests.
5. Limits, breaches, escalation and overrides.
6. Revaluation simulation/approval/ledger posting.
7. EOD, recovery, audit, dashboards and acceptance evidence.

## 16. Test plan

| ID | Scenario | Expected evidence |
| --- | --- | --- |
| TFX-01 | Cash position from approved accounts | Snapshot reconciles to ledger |
| TFX-02 | Maturity buckets/boundaries | Approved policy reproducible |
| TFX-03 | FX position/rate conversion | Source/base amounts and rate version retained |
| TFX-04 | Stale/missing/tampered rate | Approved block/degrade behavior |
| TFX-05 | Limit breach/replay | One open breach and escalation |
| TFX-06 | Unauthorized override/export | Denied and audited |
| TFX-07 | Revaluation posting/retry | Balanced single outcome |
| TFX-08 | Period lock/provider/ledger timeout | Stable failure/uncertain state |
| TFX-09 | Cross-tenant/book query | Non-disclosing denial |
| TFX-10 | Restore/restart | Snapshot/run/checkpoint integrity |

## 17. Acceptance criteria

- [ ] Cash positions, maturity gaps, FX positions and exposure limits are available to authorised treasury users.
- [ ] Positions reconcile to approved ledger and market data.
- [ ] Limits and breaches are configurable, effective-dated, alerted and audited.
- [ ] EOD revaluation uses approved rates/accounting and posts balanced idempotent entries.
- [ ] Detailed treasury requirements and stakeholder approvals are baselined.

## 18. Definition of Done

Repository DoD plus approved requirements/ownership/market-data/accounting ADRs, calculation golden tests, migrations, reconciliation, limit/security tests, EOD/recovery rehearsal, dashboards and treasury/finance/risk approval.

## 19. Open decisions

Owner/database, books/accounts, cash-flow/maturity methodology, rate/curve providers and hierarchy, staleness/fallback, FX precision/rounding, limit aggregation/overrides, revaluation/P&L/accounting, EOD ordering, retention and load targets remain open.

## 20. Prohibited assumptions

Do not invent rates/curves/buckets/limits, use `double` for financial calculation, treat snapshots as balances, silently use stale data, net exposures without approved scope, or post revaluation outside Ledger.

## 21. Agent implementation instructions

Required reading: Phase 4 overview, ledger/EOD/risk/reconciliation/reporting epics and approved requirements/ADRs. Files may change only in approved owner, contracts, migrations, tests and documented consumers. Run build, golden calculation, migration, contract, concurrency, posting, reconciliation, security, load and recovery tests. Stop before persistence if ownership, market-data or accounting rules are unresolved.

