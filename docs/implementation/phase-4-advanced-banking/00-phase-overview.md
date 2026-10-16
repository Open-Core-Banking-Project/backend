---
phase: "Phase 4 - Advanced Banking"
status: Draft
owning_services:
  - Core Banking and Ledger
  - Operations and Workflow
  - Risk and Reporting
  - Integration Hub
  - Audit and Reconciliation
requirements:
  - LED-001..LED-013
  - RSK-001..RSK-008
  - REC-001..REC-010
  - API-001..API-008
  - DAT-001..DAT-008
  - SEC-001..SEC-018
  - AUD-001..AUD-004
  - NFR-PERF-001..NFR-PERF-002
  - NFR-SCAL-001
  - NFR-AVAIL-001
  - NFR-RES-001..NFR-RES-002
  - NFR-DR-001..NFR-DR-002
  - NFR-OBS-001..NFR-OBS-002
  - NFR-MNT-001
stories: [US-077, US-078]
dependencies:
  - Phase 0 - Engineering Foundation verified
  - Phase 1 - Banking Foundation verified
  - Phase 2 - Regulated Operations verified
  - Phase 3 - Growth Channels verified where corporate and partner dependencies are enabled
last_updated: 2026-10-16
---

# Phase 4 - Advanced Banking

## Objective

Add controlled treasury liquidity/FX exposure monitoring and trade-finance instrument lifecycle support without weakening ledger authority, accounting integrity, sanctions controls, collateral governance, correspondent-message security or settlement traceability.

Phase 4 exit emphasis is advanced accounting, exposure and settlement control. Both allocated stories are `Could` priority. Each capability remains disabled until a launch institution explicitly selects it and approves the missing business, legal, market-data, accounting and provider specifications.

## Requirements-baselining notice

The supplied SRS allocates Phase 4 at roadmap level but does not define dedicated Treasury, FX, Investment, Trade Finance, SWIFT or Correspondent requirement IDs. Therefore:

- US-077 and US-078 acceptance criteria are the current minimum functional scope.
- Existing ledger, risk, reconciliation, API, data, security, audit and non-functional requirements still apply.
- Detailed treasury/trade requirements must be written, reviewed and baselined before either epic becomes `Approved`.
- Implementation agents must not infer market conventions, accounting treatment, SWIFT rules, credit limits, collateral/margin policy or legal obligations.

This gap is explicit and does not prevent planning, contract exploration or stakeholder refinement. It does prevent irreversible production implementation.

## Entry criteria

- All enabled prior-phase ledger, workflow, IAM, corporate-party, document, risk/sanctions, reconciliation, reporting, integration and operational controls are verified.
- Multi-currency ledger/accounting treatment, approved currencies, business/value dates, periods and revaluation mappings are documented.
- Treasury books, entities, accounts, market-data sources, valuation policy and limit ownership are approved for Epic 1.
- Trade instrument types, legal rules, document requirements, sanctions checkpoints, collateral/margin, charges and correspondent/SWIFT integration are approved for Epic 2.
- Provider/correspondent certification, secrets/certificates, retention, RTO/RPO, capacity and support model are approved for selected capabilities.

## Epic map

| Order | Epic | Primary owner | Stories | Outcome |
| --- | --- | --- | --- | --- |
| 1 | Treasury Liquidity and FX Exposure | Owner requires treasury-boundary ADR; coordinated by Risk and Reporting with Core Banking and Ledger | US-077 | Reconciled cash/maturity/FX positions, versioned limits/breaches and approved EOD revaluation |
| 2 | Trade Finance Instruments and Correspondent Messaging | Owner requires trade-finance-boundary ADR; coordinated across Operations, Integration, Risk and Ledger | US-078 | LC/guarantee origination, amendment, approval, expiry, settlement, sanctions and traceable messages |

## Dependency map

```text
Verified prior phases and approved Phase 4 requirements
    |
    +--> 01 Treasury Liquidity and FX Exposure
    |        depends on ledger, market data, limits, reconciliation and EOD
    |
    +--> 02 Trade Finance Instruments and Correspondent Messaging
             depends on corporate parties/mandates, workflow, sanctions,
             documents, collateral, correspondent adapters, ledger and settlement
```

The two epics may share approved currency, market-data, exposure and accounting contracts, but they must not share private tables or collapse their different lifecycle ownership.

## Service ownership boundaries

### Core Banking and Ledger

- Own all authoritative cash, nostro, settlement, margin, collateral-related financial and revaluation postings.
- Own accounting periods, value/posting/business dates and approved accounting-event mappings.
- Never accept position/report totals as direct balance mutation instructions without validated posting components.

### Risk and Reporting

- Own governed treasury exposure read models, limit evaluation, breach alerts and reporting if approved by the treasury-boundary ADR.
- Consume authoritative ledger/market-data contracts; never become the source of cash balances or exchange-rate truth.
- Continue to own sanctions/risk decisions used by trade workflow.

### Operations and Workflow

- Own approval tasks, amendment/settlement authorization, exceptions, discrepancies, SLA and maker-checker coordination.
- A completed task authorises the owning service command; it does not prove posting, message delivery or settlement.

### Integration Hub

- Own market-data, SWIFT/correspondent and other provider adapters, credentials, certificates, canonical mappings, delivery/status evidence and certification.
- Never determine final valuation, sanctions disposition, trade approval or ledger outcome.

### Audit and Reconciliation

- Reconcile positions, rates, revaluation postings, correspondent messages, fees, margins, settlements and suspense.
- Retain immutable evidence for limits, overrides, documents, approvals, message attempts and settlement.

### Ownership ADRs

The nine-service architecture has no preassigned Treasury or Trade Finance database/module. Each epic must approve its logical owner and schema/database placement before migrations. No tenth microservice is implied by the phase name.

## Cross-service contracts

- Treasury book/entity/account/currency, cash position, maturity bucket, FX position and exposure snapshot.
- Market-data source, rate/curve version, timestamp, quality/staleness and approval status.
- Limit definition/version, utilization, breach, acknowledgement, escalation and override.
- Revaluation run/item, accounting component, posting instruction/outcome and reconciliation evidence.
- Trade instrument/application/version, party/beneficiary, amount/currency, expiry, terms, document/evidence references and lifecycle state.
- Limit/margin/collateral/charge assessment and approval snapshot.
- Sanctions screening request/result and hold/release decision.
- Correspondent/SWIFT canonical message, validation result, attempt, acknowledgement/rejection and correlation.
- Trade settlement instruction/outcome, fee/commission/tax/margin component, discrepancy and reconciliation break.

All contracts use explicit currency, dates, source/version, institution, correlation, causation and minimum necessary data.

## Financial-integrity gates

- Ledger-derived positions reconcile to approved ledger accounts and cannot be overwritten by reporting or market data.
- FX amounts retain original currency, base/reporting currency, rate, rate source/version/time and rounding policy.
- Revaluation creates balanced, approved, traceable entries and never rewrites original transactions.
- Maturity gaps use documented contractual/cash-flow dates and bucket policy.
- Limit utilization and breach do not silently alter balances or approve trades.
- Trade limits, margin, collateral, charges, expiry and settlement are validated against approved versions.
- Trade issuance/amendment/settlement preserves complete version history and maker-checker.
- Sanctions screening is current and required before approval and settlement.
- Correspondent acknowledgement alone does not prove final financial settlement.
- Provider/message uncertainty remains explicit and is resolved by status/reconciliation, not blind retry.

## Security, legal and messaging gates

- Treasury/trade permissions include institution, entity, book, currency, product/instrument, amount, country/counterparty and authority scope.
- Privileged valuation, limit override, instrument amendment, message release and settlement use step-up and immutable audit.
- SWIFT/correspondent messages use approved authentication, signing/encryption, certificates, network restrictions and replay protection.
- Message payloads and trade documents are classified; ordinary logs/traces exclude unrestricted content.
- Sanctions/PEP/watchlist evidence and legal/document checks follow approved compliance policy.
- Dual control applies to message release and financial actions where policy requires.

## Data and migration gates

- Treasury/trade owner and schema are approved before first migration.
- Position snapshots and reports are non-authoritative; source ledger and market-data evidence remain linked.
- Rates, limits, rules, valuations, instruments, amendments, documents, messages and decisions are versioned/append-only where evidential.
- Monetary values use approved decimal precision and explicit ISO currency; FX rates use separately approved precision.
- Dates distinguish business, trade, value, maturity, expiry, posting, settlement and message timestamps.
- No cross-service database access/foreign keys.
- Retention, legal hold, archival, partition and restore design covers high-value evidence and message history.

## Observability and operational gates

- Trace position inputs through rates, valuation, limit result, posting and reconciliation.
- Trace trade application through parties, documents, screening, approval, message attempts, ledger and settlement.
- Metrics cover stale/missing market data, position/reconciliation breaks, limit utilization/breach age, revaluation failures, pending/expired instruments, message reject/timeout and settlement discrepancies.
- Alerts are actionable, tenant-safe and linked to approved runbooks.
- EOD/revaluation/message/settlement jobs checkpoint and replay without duplicate postings or deliveries.

## Phase-level test journeys

1. Ledger cash positions and maturity gaps reconcile to approved accounts/cash flows.
2. FX position uses approved rate version and preserves source/base currency amounts.
3. Limit change follows version/approval; breach alerts and escalation retain evidence.
4. EOD revaluation posts balanced entries once and reverses/adjusts through approved accounting policy.
5. Stale/missing market data blocks or degrades under explicit approved policy.
6. LC/guarantee application validates parties, limits, margin, collateral, charges and expiry.
7. Amendment preserves previous version and revalidates authority, screening and financial effects.
8. Sanctions potential match blocks approval/settlement pending review.
9. SWIFT/correspondent message is validated, signed, traced, deduplicated and correlated to instrument.
10. Message timeout remains uncertain; status/reconciliation prevents duplicate release/settlement.
11. Settlement posts balanced components and reconciles message, instrument and ledger.
12. Cross-tenant, stale authority, self-approval, rate tampering and sensitive-message leakage attempts fail safely.

## Phase exit criteria

- [ ] Selected Could capabilities and detailed requirements are explicitly approved.
- [ ] Both stories trace through ADR, contracts, data, code, tests and operational evidence.
- [ ] Positions/rates/limits and trade instruments/messages/settlements are reconciled and auditable.
- [ ] Revaluation, margin, collateral, charges, settlement and suspense accounting is approved and balanced.
- [ ] Sanctions, legal, message-security and correspondent/provider certification are complete.
- [ ] No open severity-1/2 financial-integrity, security, data-isolation or settlement defects remain.
- [ ] Performance, availability, capacity, recovery, retention and monitoring meet approved targets.
- [ ] Treasury, Finance, Trade Operations, Risk, Compliance, Security, Legal, Operations and UAT approvals are complete.

## Open decisions

| Decision | Epic | Owner |
| --- | --- | --- |
| Treasury module/service/database ownership | 1 | Architecture, Treasury, Finance |
| Books/accounts, position/maturity methodology and authoritative inputs | 1 | Treasury, Finance, Risk |
| Market-data providers, rate/curve hierarchy, staleness and fallback | 1 | Treasury, Risk, Integration |
| FX precision, valuation, revaluation, P&L and accounting mappings | 1 | Finance, Treasury, Accounting |
| Exposure limits, aggregation, breach/override and escalation | 1 | Risk, Treasury, Governance |
| Trade module/service/database ownership and selected instruments | 2 | Architecture, Trade Product, Operations |
| LC/guarantee legal terms, documents, amendments, expiry and claims | 2 | Legal, Compliance, Trade Operations |
| Trade limits, margin, collateral, charges/tax and accounting | 2 | Credit, Finance, Risk, Trade Product |
| SWIFT/correspondent network, message types, validation, signing and certification | 2 | Integration, Security, Trade Operations |
| Settlement, discrepancy, suspense, reconciliation and cutoffs | 2 | Finance, Reconciliation, Operations |
| Phase 4 volumes, targets, retention, RTO/RPO and support | Both | Product, SRE, Risk, Compliance |

## Prohibited assumptions

- Do not treat the roadmap labels as detailed treasury/trade requirements.
- Do not invent FX rates, sources, curves, day-count, valuation, P&L or accounting rules.
- Do not infer SWIFT formats, correspondent behavior, sanctions obligations or legal document rules.
- Do not use a reporting position as an authoritative balance.
- Do not let market data, risk, workflow, trade or integration services post balances directly.
- Do not retry ambiguous messages or settlements blindly.
- Do not create a tenth service or shared database without approved ADR.
- Do not claim full investments/correspondent/trade coverage from the two allocated stories.

## Epic implementation documents

1. [`01-treasury-liquidity-and-fx-exposure.md`](01-treasury-liquidity-and-fx-exposure.md)
2. [`02-trade-finance-instruments-and-correspondent-messaging.md`](02-trade-finance-instruments-and-correspondent-messaging.md)

All Phase 4 epic instructions are drafted and remain subject to detailed requirements and approval before implementation.

## Agent instructions

- Read root/service `AGENTS.md`, architecture, prior-phase evidence, this overview and applicable epic.
- Require an approved detailed requirement baseline and ownership ADR before production persistence.
- Keep financial calculations deterministic, versioned and reconciled to the protected ledger.
- Preserve explicit currency, dates, rates, limits, source versions, permissions, audit and uncertainty.
- Stop for any unapproved accounting, legal, market-data, sanctions, SWIFT/correspondent or settlement decision.

