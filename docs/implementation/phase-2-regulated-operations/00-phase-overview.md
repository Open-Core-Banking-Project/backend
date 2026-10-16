---
phase: "Phase 2 - Regulated Operations"
status: Draft
owning_services:
  - Core Banking and Ledger
  - Customer and Onboarding
  - Identity and Access
  - Payments and VTU
  - Channels and Engagement
  - Operations and Workflow
  - Risk and Reporting
  - Integration Hub
  - Audit and Reconciliation
requirements:
  - ARC-001..ARC-010
  - IAM-009..IAM-010
  - CUS-001..CUS-010
  - ACC-001..ACC-010
  - LED-001..LED-013
  - WFL-001..WFL-008
  - REC-001..REC-010
  - LND-001..LND-008
  - BRN-001..BRN-006
  - RSK-001..RSK-008
  - RPT-001..RPT-005
  - RPT-008
  - API-001..API-008
  - DAT-001..DAT-008
  - NFR-PERF-001..NFR-PERF-002
  - NFR-SCAL-001
  - NFR-AVAIL-001
  - NFR-RES-001..NFR-RES-002
  - NFR-DR-001..NFR-DR-002
  - NFR-OBS-001..NFR-OBS-002
  - NFR-USA-001
  - NFR-ACC-001
  - NFR-I18N-001
  - NFR-MNT-001
  - SEC-001..SEC-018
  - AUD-001..AUD-004
  - REP-001..REP-003
  - REG-001..REG-003
stories:
  - US-010
  - US-012
  - US-024
  - US-038
  - US-039
  - US-040
  - US-041
  - US-042
  - US-043
  - US-044
  - US-045
  - US-046
  - US-047
  - US-051
  - US-052
  - US-053
  - US-054
  - US-055
  - US-056
  - US-057
  - US-058
  - US-065
  - US-073
dependencies:
  - Phase 0 - Engineering Foundation verified
  - Phase 1 - Banking Foundation verified
last_updated: 2026-10-16
---

# Phase 2 - Regulated Operations

## Objective

Extend the verified banking foundation into controlled regulated operations: privileged-access review, corporate-party onboarding, term deposits, lending and recovery, fraud/AML and sanctions, reconciliation and settlement, branch/teller cash, agency banking, regulatory and scheduled reporting, and explicitly bounded degraded operation.

Phase 2 must prove control evidence, financial balancing and regulatory readiness. It must not trade ledger authority, tenant isolation, maker-checker, auditability or recoverability for feature breadth.

## Release boundary

Phase 2 delivers the backend capability and operational evidence for the 23 stories allocated to regulated operations. Institution-specific legal rules, report layouts, accounting mappings, credit policy, AML thresholds, provider contracts and offline limits remain approved configuration or policy inputs. They are not invented by implementation agents.

Phase 2 continues to use the nine initial C#/.NET microservices. Logical modules may be added inside their approved boundaries, but a new deployable microservice requires an ADR and operational justification.

## Entry criteria

Phase 2 implementation begins only when Phase 1 has verified:

- institution, branch, business-calendar and effective-dated configuration ownership;
- production identity, permission, contextual scope, step-up and maker-checker enforcement;
- individual customer onboarding and customer/KYC lifecycle foundations;
- approved deposit-product and account lifecycle controls;
- balanced, immutable, idempotent ledger posting and reversal;
- payment orchestration, provider uncertainty and controlled integration behaviour;
- workflow, case, SLA and evidence foundations;
- document storage, legal hold, audit, transaction trace and recovery evidence;
- controlled releases, service health, observability, alerts and runbooks;
- approved Phase 1 exit with no unresolved severity-1 or severity-2 financial-integrity, security or tenant-isolation defects.

If Phase 1 is only documented and not verified, Phase 2 remains planning-only. Phase 2 code must not be used to bypass unfinished Phase 1 controls.

## Epic map

| Order | Epic | Primary owner | Stories | Release outcome |
| --- | --- | --- | --- | --- |
| 1 | Privileged Access Review | Identity and Access | US-010 | Scheduled access-review campaigns, evidence, revocation and escalation |
| 2 | Corporate Party Onboarding | Customer and Onboarding | US-012 | Corporate parties, ownership, beneficial owners, signatories and verified onboarding |
| 3 | Term Deposits and Maturity | Core Banking and Ledger | US-024 | Fixed-deposit contracts, accrual, tax, maturity, rollover and controlled liquidation |
| 4 | Lending Products and Origination | Owner requires lending-boundary ADR | US-038, US-039 | Versioned credit products, simulation, application, assessment, approval and ledger disbursement |
| 5 | Loan Servicing, Collateral and Recovery | Owner requires lending-boundary ADR | US-040..US-042 | Schedules, repayment, payoff, collateral, delinquency, restructuring and recovery |
| 6 | Risk, Fraud, AML and Sanctions | Risk and Reporting | US-043..US-046 | Screening, monitoring, risk decisions, alerts and separated investigations |
| 7 | Reconciliation and Settlement | Audit and Reconciliation | US-051..US-054 | Provider ingestion, matching, breaks, suspense, positions and certification |
| 8 | Branch, Teller and Cash Operations | Core Banking and Ledger with Operations and Workflow | US-055, US-056 | Tills, vaults, cash movements, dual control, teller balancing and receipts |
| 9 | Agency Banking and Offline Capture | Channels and Engagement with Core Banking and Ledger | US-057, US-058 | Agent onboarding, device/location controls, float, commissions and bounded offline synchronisation |
| 10 | Regulatory and Scheduled Reporting | Risk and Reporting | US-047, US-065 | Governed regulatory returns, operational reports, approval and secure distribution |
| 11 | Degraded Operations and Phase Readiness | Platform-wide | US-073 | Explicit safe degraded capabilities, queues, expiry, replay, reconciliation and release evidence |

Each epic receives its own implementation document using `EPIC_TEMPLATE.md`. Epic numbering expresses the recommended refinement and implementation sequence, not permission to deploy an upstream dependency before its controls are verified.

## Dependency map

```text
Verified Phase 1 controls
    |
    +--> 01 Privileged Access Review
    |
    +--> 02 Corporate Party Onboarding
    |        +--> 04 Lending Products and Origination
    |
    +--> 03 Term Deposits and Maturity
    |
    +--> 06 Risk, Fraud, AML and Sanctions
    |        +--> 02 Corporate Party Onboarding screening
    |        +--> 04 Lending risk decisions
    |        +--> 08 Teller risk signals
    |        +--> 09 Agent risk controls
    |
    +--> 04 Lending Products and Origination
    |        +--> 05 Loan Servicing, Collateral and Recovery
    |
    +--> 07 Reconciliation and Settlement
    |        +--> 03 Term-deposit balancing
    |        +--> 05 Loan subledger balancing
    |        +--> 08 Teller/vault balancing
    |        +--> 09 Agent settlement
    |
    +--> 08 Branch, Teller and Cash Operations
    |        +--> 09 Agency Banking and Offline Capture
    |
    +--> 10 Regulatory and Scheduled Reporting
    |        depends on governed read models from all enabled modules
    |
    +--> 11 Degraded Operations and Phase Readiness
             depends on every enabled Phase 2 journey and runbook
```

Implementation may overlap only where contracts, ownership and prerequisites are explicit. Financial postings remain behind Core Banking and Ledger even when another service owns the initiating lifecycle.

## Service ownership and boundaries

### Core Banking and Ledger

- Own term-deposit products/contracts if confirmed by the product/deposit boundary.
- Own all authoritative funding, interest, tax, penalty, cash, repayment, disbursement, write-off and recovery postings.
- Own branch tills, vaults and cash-position authority within the existing branch-cash module.
- Expose idempotent posting commands and authoritative financial outcomes.
- Never permit lending, teller, agent, risk, workflow or reconciliation modules to update balances directly.

### Customer and Onboarding

- Own corporate party identity, organisations, directors, shareholders, beneficial owners, signatories, relationships and corporate KYC lifecycle.
- Store references to documents and external screening evidence, not another service's records.
- Publish versioned corporate-party and relationship facts after durable state changes.
- Do not implement Phase 3 corporate channel users, corporate payment files or delegated corporate administration as incidental scope.

### Identity and Access

- Own access-review campaigns, reviewer assignment, entitlement snapshots, temporary permission expiry and revocation commands.
- Continue to own staff, customer, agent, corporate and service identities and permission grants.
- Enforce independent review, step-up and conflict-of-duty policies.
- Do not treat an access-review approval as a permanent override of current security policy.

### Risk and Reporting

- Own sanctions/PEP/watchlist screening requests/results, monitoring rules, risk scores, alerts, investigations, governed read models, report definitions and regulatory return preparation.
- Recommend allow, challenge, hold, reject or review outcomes according to approved policy.
- Never mutate balances, declare provider settlement, or silently close a financial exception.
- Maintain source lineage from every reported field to approved data contracts.

### Audit and Reconciliation

- Own provider/ledger reconciliation batches, source records, match rules/results, breaks, settlement positions, suspense investigations and certification evidence.
- Request controlled ledger corrections through approved commands and workflows.
- Never edit ledger history or directly clear authoritative balances.
- Retain immutable evidence for matching, manual decisions, waivers and sign-off.

### Operations and Workflow

- Own approval cases, review work, investigations where allocated, dual-control tasks, exceptions, SLAs, escalation and controlled override workflows.
- Store cross-service references rather than copying authoritative customer or financial state.
- A completed workflow authorises an action; it does not prove that the financial posting succeeded.

### Channels and Engagement

- Own agent-channel sessions and offline-capture envelopes if confirmed by the agency-boundary ADR.
- Enforce allow-listed functions, explicit confirmation, expiry and synchronisation status.
- Never treat offline state, agent float display or a channel receipt as authoritative ledger state.

### Integration Hub

- Own sanctions, watchlist, bureau, settlement-file/API and other external provider adapters.
- Authenticate sources, validate signatures/checksums, map canonical contracts and retain safe provider evidence references.
- Do not own customer risk decisions, reconciliation decisions or final financial outcomes.

### Payments and VTU

- Continue to own payment and bill/VTU orchestration used by branch or agent journeys.
- Publish canonical outcomes for reconciliation and risk monitoring.
- Preserve a single intended debit and credit across retries, failover and offline synchronisation.

## Cross-service contracts required before implementation

The following contracts must be approved, versioned and compatibility-tested before dependent slices begin:

- entitlement snapshot, access-review assignment, decision, revocation and expiry contracts;
- corporate party, ownership, beneficial-owner, director, signatory and mandate-reference contracts;
- product version, term-deposit instruction, accrual, maturity, liquidation and ledger-posting contracts;
- lending application, decision, facility/contract, schedule, repayment, payoff, collateral and delinquency contracts;
- risk-screening request/result, monitoring signal, risk decision, alert and investigation contracts;
- reconciliation source-record, matching result, break, settlement position, certification and correction-request contracts;
- till/vault session, cash movement, teller transaction, overage/shortage and balancing contracts;
- agent, device, location, float, commission, offline envelope, synchronisation and expiry contracts;
- report definition, read-model input, report run, regulatory return, approval and distribution contracts;
- degraded-capability policy, queued-command, expiry, recovery, replay and customer-status-correction contracts.

Contracts expose the minimum necessary data. They carry institution, correlation and causation identity, schema version and authoritative source. Financial commands also carry scoped idempotency identity and explicit currency.

## Financial-integrity gates

Before any Phase 2 financial journey is enabled:

- accounting event mappings are approved for every principal, interest, fee, tax, penalty, commission, cash, suspense, settlement, write-off and recovery component;
- all journals balance in the same currency and accounting scope;
- posted entries remain immutable and corrections use linked reversal or adjustment;
- duplicate commands, messages, files, offline envelopes and retries cannot create duplicate postings;
- term-deposit, loan, till, vault, agent-float and settlement views reconcile to the authoritative ledger;
- business date, transaction time, posting date and value date remain distinct;
- maker-checker and amount/branch/risk authority are enforced before the ledger command;
- provider uncertainty remains explicit and is resolved by status enquiry or reconciliation;
- manual matching, waiver, adjustment, write-off, collateral release and cash correction require authority, reason and immutable audit evidence;
- reports and read models never become authoritative balance sources.

## Identity, role and permission gates

- Every protected command, query, review, investigation, report, cash operation and degraded-mode action declares a stable permission.
- Permission checks include institution and applicable branch, product, customer/account, case, amount, currency, channel, device, location, mandate and transaction-limit scope.
- Role names are configurable bundles and are not hard-coded into domain decisions.
- Makers cannot approve their own restricted requests even when they hold multiple roles.
- Privileged and high-risk actions use MFA or step-up according to approved policy.
- Temporary and delegated access has explicit start, expiry, reason, approver and review state.
- Access-review evidence uses an entitlement snapshot but revalidates current state before revocation or retention action.
- Agent, teller, corporate, compliance, reconciliation and service identities use distinct security policies.
- Cross-tenant negative tests exist for every tenant-owned Phase 2 resource.

## Data and migration gates

- Each module has one authoritative owner and one service-owned database/schema migration path.
- The lending lifecycle owner and persistence boundary must be approved before its first migration.
- Tenant-owned records include trusted `institution_id`; branch-owned records include branch scope where required.
- Monetary data uses approved fixed precision/scale and explicit currency.
- Percentages, rates, tenors, schedules and thresholds have explicit units and approved rounding/day-count rules.
- Effective-dated rule, product, screening, matching and report versions remain reproducible after replacement.
- Corporate ownership, collateral valuation, loan schedules, cash positions, reconciliation source rows and regulatory submissions preserve history.
- High-volume monitoring, reconciliation and report data has approved partition, retention, archive and purge design.
- Legal hold prevents alteration or deletion of relevant evidence and documents.
- Backfills are bounded, restartable, observable and reconcilable.
- No migration creates a cross-service foreign key or reads another service's private schema.
- No production-derived data enters non-production without approved irreversible masking.

## Reconciliation and settlement gates

- Source files/API records are authenticated and validated for source, institution, checksum/signature, period, schema and duplicate ingestion.
- Canonical source records preserve raw-evidence references and parsing versions.
- Exact and approved tolerance-based matching rules are versioned, effective-dated, simulated and maker-checker controlled.
- Matching produces explicit matched, unmatched, missing, duplicate and amount-mismatch outcomes.
- Breaks retain owner, age, amount, currency, source, SLA, evidence and resolution state.
- Suspense balances are monitored and cleared only through controlled ledger postings.
- Settlement positions use approved calendars, cut-offs, fees and schedules.
- Material open exceptions block certification unless an authorised, evidenced waiver policy applies.
- Reprocessing the same file, record or event does not create a duplicate match, break, settlement position or posting.

## Risk, fraud, AML and sanctions gates

- Screening sources, list versions, provider mappings and refresh evidence are approved and traceable.
- Potential matches create review work; provider success does not imply silent customer approval.
- Monitoring rules support approved amount, velocity, behaviour, geography and counterparty inputs.
- Rules, thresholds and models are versioned, approved, effective-dated and reproducible.
- Risk outputs are explainable to authorised reviewers and do not reveal sensitive internal controls to untrusted callers.
- Risk decisions may allow, challenge, hold, reject or recommend review but never post balances directly.
- Investigations enforce role separation, need-to-know evidence access, SLA, disposition, rationale and approval where required.
- Country-pack reporting and suspicious-activity obligations require compliance approval; agents do not invent legal thresholds or filing rules.

## Branch, teller and agency gates

- Every till and vault session has an authorised custodian, opening state, limits, movement history and close/balance result.
- Cash movements post balanced accounting entries; physical cash state and ledger state are reconciled explicitly.
- High-value or vault operations apply approved dual control.
- Teller transactions enforce customer, account, cash, teller, branch and institution limits before posting.
- Overages and shortages remain visible as investigation records and do not disappear through direct balance edits.
- Agent onboarding includes KYC, approval, status, assigned devices, permitted locations, limits, float and commission configuration.
- Agent float and settlement positions are reconciled to ledger and provider/channel evidence.
- Offline operation is disabled until cryptographic envelope, device binding, expiry, offline limit, duplicate detection, risk review and synchronisation recovery are approved and tested.
- High-risk, expired, stale or conflicting offline requests require online review and never auto-post merely because connectivity returned.

## Reporting and regulatory gates

- Reports read governed service contracts, read models or event-fed analytical stores, never service-private tables or direct ledger mutations.
- Every report field traces to an approved source, transformation and version.
- Report definitions, parameters, templates and distribution rules are versioned and permission controlled.
- Regulatory returns record country pack, period, version, preparer, reviewer, submission status and evidence.
- Adjustments preserve original values, reason, authority and before/after evidence.
- Sensitive report access, generation, download and distribution are audited.
- Exports use secure storage/links, classification, expiry, watermarking and recipient controls where policy requires.
- PDF, Excel and CSV outputs must not differ in financial totals or scope.
- Legal hold and retention apply to report artifacts and submission evidence.

## Degraded-operation gates

Degraded operation is denied by default. Each enabled degraded capability must define:

- unavailable dependency and detection rule;
- allowed and prohibited operations;
- institution, channel, product, amount, currency and time scope;
- idempotency, expiry, ordering and queue capacity;
- customer/staff status wording that does not claim success prematurely;
- approval and feature-flag policy;
- recovery, replay, reconciliation and customer-status correction;
- metrics, alerts, runbook, stop condition and audit evidence.

No degraded path may weaken authentication, authorisation, tenant isolation, sanctions controls, balanced posting, immutable audit or reconciliation. Offline agent capture is a specialised degraded capability and must satisfy both Epic 9 and Epic 11 controls.

## Security and privacy gates

- Threat models cover corporate ownership abuse, insider access, lending fraud, sanctions evasion, alert tampering, cash theft, agent/device compromise, settlement-file tampering, offline replay and report leakage.
- Sensitive identity, beneficial-owner, credit, collateral, AML, investigation and regulatory data is classified and access controlled.
- Secrets and provider credentials remain in approved secret management.
- Provider files, webhooks and APIs use authenticated source, signature/checksum, timestamp and replay controls.
- Evidence access follows need-to-know policy and produces sensitive-access audit records.
- Export, correction, restriction and deletion/anonymisation follow legal approval; financial/audit/regulatory evidence is not silently erased.
- Security events integrate with central monitoring and incident response.
- Vulnerability, patch, dependency and certificate-expiry monitoring covers all Phase 2 adapters and workloads.

## Observability and operational gates

- Correlation and causation continue across channels, workflows, screening/risk decisions, ledger postings, provider records, reconciliation, settlements and reports.
- Metrics cover access-review ageing, onboarding cases, term-deposit maturity, lending stages, arrears, risk alerts, screening backlog, reconciliation breaks, settlement position, till/vault imbalance, agent float, offline queue age and report failures.
- High-cardinality customer, account, loan, transaction, case and agent identifiers are not metric labels.
- Logs, traces and dashboards exclude secrets, full identifiers, investigation evidence and prohibited personal data.
- Alerts are actionable, tenant-safe, owned and linked to tested runbooks.
- Service health distinguishes liveness, readiness and business degradation.
- Recovery/replay does not create duplicate financial outcomes, risk alerts, breaks, cash movements, reports or notifications.
- Capacity and performance tests use approved Phase 2 workload and data-volume models.

## Phase-level test journeys

At minimum, the integrated test suite proves:

1. A privileged entitlement is reviewed, revoked, invalidated and denied on reuse with immutable evidence.
2. A corporate party is onboarded with ownership totals, beneficial owners, signatories, KYC/screening and versioned documents.
3. A term deposit is funded, accrued, matured and paid/rolled according to an approved rule version with balanced postings.
4. A loan product is simulated, an application approved, and disbursement posts once through the ledger.
5. A repayment schedule handles partial payment, auto-debit replay and approved early settlement without balance drift.
6. Collateral valuation, lien, substitution/release and loan-to-value controls preserve approval and history.
7. Delinquency moves through approved buckets, promises/restructuring are recorded, and write-off preserves debt/recovery history.
8. Sanctions screening produces a potential-match case and prevents silent approval.
9. Transaction monitoring produces a prioritised alert linked to customer, transactions, rule version and investigation.
10. A settlement file is authenticated, ingested once, matched and reconciled to payment and ledger records.
11. An amount mismatch creates a break; approved correction references a ledger adjustment without editing history.
12. Certification is blocked by material exceptions unless an approved waiver is evidenced.
13. A till opens, receives cash, performs deposit/withdrawal, transfers to vault and closes with balanced evidence.
14. An overage/shortage creates an investigation and cannot be hidden through direct balance modification.
15. An agent is approved with device/location/float controls and a transaction settles exactly once.
16. An offline agent request is protected, bounded, expires correctly, deduplicates on synchronisation and routes high-risk state to review.
17. A regulatory return traces every material field to approved source data and preserves preparer/reviewer/submission evidence.
18. A scheduled sensitive report distributes only to authorised recipients and records delivery/access evidence.
19. A configured degraded journey queues safely, reports pending status, expires or replays once and reconciles after recovery.
20. Cross-tenant, cross-branch, maker-self-approval, stale entitlement and sensitive-evidence access attempts are denied and audited.

## Phase exit criteria

- [ ] All allocated Must requirements are implemented or have approved, time-bound exceptions.
- [ ] All enabled Should/Could capabilities have an explicit included/deferred decision.
- [ ] Requirement and story traceability reaches design, contract, database, code, tests and operational evidence.
- [ ] No open severity-1 or severity-2 security, financial-integrity or tenant-isolation defects remain.
- [ ] Ledger balance, idempotency, reversal, provider uncertainty, maker-checker and reconciliation suites pass.
- [ ] Term-deposit, loan, cash, agent and settlement subledgers/views reconcile to the authoritative ledger.
- [ ] Regulatory and scheduled report lineage, approval, export and retention evidence is accepted.
- [ ] Sanctions, AML/fraud and investigation controls are approved by compliance/security.
- [ ] Access-review, teller, agent and offline controls are approved by their business/control owners.
- [ ] Performance, availability, recovery, backup restoration and monitoring meet approved targets.
- [ ] Provider certification and production credentials are complete for enabled integrations.
- [ ] Degraded capabilities and recovery runbooks are tested; all other degraded paths fail closed.
- [ ] User acceptance, finance, security, compliance, risk and operational-readiness reviews are approved.

## Open decisions before epic approval

| Decision | Affected epics | Required owner |
| --- | --- | --- |
| Lending lifecycle service, module and database ownership within the nine-service topology | 4, 5 | Architecture and domain owners |
| Loan interest methods, day-count, rounding, accrual, fee, penalty, restructuring and write-off policy | 4, 5 | Credit, Finance, Risk |
| Credit decision authority and bureau/provider semantics | 4 | Credit, Risk, Compliance |
| Corporate ownership validation, mandate meaning and country-specific KYC rules | 2 | Compliance, Legal, Product |
| Term-deposit interest, tax, maturity, rollover and premature-liquidation rules | 3 | Product, Finance, Tax/Compliance |
| Screening providers, watchlists, match policy and disposition obligations | 2, 6 | Compliance, Security |
| AML/fraud rule catalogue, thresholds, alert SLA and filing rules | 6, 10 | Compliance, Risk, Legal |
| Settlement calendars, cut-offs, tolerance, materiality, suspense and certification policy | 7 | Finance, Operations, Risk |
| Physical cash chart of accounts, till/vault limits, denominations and dual-control policy | 8 | Finance, Branch Operations, Security |
| Agency module ownership, provider/device model, float, commission and settlement policy | 9 | Architecture, Agency Operations, Finance |
| Offline transaction types, cryptography, device trust, limits, expiry and conflict resolution | 9, 11 | Security, Risk, Operations |
| Country-pack regulatory return schemas, calendars, submission method and retention | 10 | Compliance, Legal, Regulators |
| Report formats, distribution channels and sensitive-recipient policy | 10 | Business owners, Security, Compliance |
| Explicit degraded-capability matrix and customer-status correction rules | 11 | Business, Risk, Operations, Architecture |
| Phase 2 volumes, availability, capacity, retention, RTO and RPO | All | Product, SRE, Risk, Compliance |

These decisions must become approved ADRs, policies, accounting specifications or provider contracts. An open decision may support interface exploration but not irreversible financial, regulatory or data design.

## Prohibited assumptions

- Do not infer Nigerian or other country-specific regulatory rules from product names or examples.
- Do not invent lending rates, fees, penalties, affordability rules, credit scores, write-off policy or accounting mappings.
- Do not invent sanctions/PEP match thresholds, AML rules, suspicious-activity filing rules or regulator report schemas.
- Do not assume tolerance-based reconciliation means financial amounts may be silently adjusted.
- Do not clear suspense or reconciliation breaks by editing ledger data.
- Do not treat physical cash, agent float, report totals or provider settlements as authoritative customer balances.
- Do not allow teller, agent, risk, workflow, reconciliation or reporting code to post around the protected ledger API.
- Do not assume offline capture is approved merely because the story is in Phase 2; it is Could priority and remains disabled until explicitly selected.
- Do not implement Phase 3 corporate banking, cards, merchants, virtual accounts or partner APIs as incidental scope.
- Do not create a new lending, teller, agency, fraud or reporting microservice without an approved boundary ADR.
- Do not copy unrestricted investigation, KYC, provider or customer payloads into events, logs, reports or test fixtures.
- Do not claim regulatory readiness from passing automated tests alone; required human and external approvals remain mandatory.

## Epic implementation documents

Review and implement in dependency order:

1. [`01-privileged-access-review.md`](01-privileged-access-review.md)
2. [`02-corporate-party-onboarding.md`](02-corporate-party-onboarding.md)
3. [`03-term-deposits-and-maturity.md`](03-term-deposits-and-maturity.md)
4. [`04-lending-products-and-origination.md`](04-lending-products-and-origination.md)
5. [`05-loan-servicing-collateral-and-recovery.md`](05-loan-servicing-collateral-and-recovery.md)
6. [`06-risk-fraud-aml-and-sanctions.md`](06-risk-fraud-aml-and-sanctions.md)
7. [`07-reconciliation-and-settlement.md`](07-reconciliation-and-settlement.md)
8. [`08-branch-teller-and-cash-operations.md`](08-branch-teller-and-cash-operations.md)
9. [`09-agency-banking-and-offline-capture.md`](09-agency-banking-and-offline-capture.md)
10. [`10-regulatory-and-scheduled-reporting.md`](10-regulatory-and-scheduled-reporting.md)
11. [`11-degraded-operations-and-phase-readiness.md`](11-degraded-operations-and-phase-readiness.md)

All Phase 2 epic instructions are now drafted. Each remains subject to review against current requirements and approved decisions before implementation.

## Agent instructions

- Read the root and service-level `AGENTS.md`, architecture document, implementation guide, Phase 0/1 evidence, this overview and the applicable epic.
- Confirm the epic is approved or that the user explicitly authorised draft implementation.
- Preserve the C#/.NET-only backend and nine-service microservices decision.
- Implement the smallest complete vertical slice and keep the solution deployable.
- Keep domain ownership explicit; use APIs/events and never cross-service database access.
- Treat rules affecting money, access, regulation, customer status, evidence or recovery as versioned approved policy.
- Include permission, tenant/branch scope, maker-checker, idempotency, concurrency, audit, observability, reconciliation and recovery tests where applicable.
- Stop and surface conflicts or missing decisions that can affect financial correctness, legal obligations, customer access, tenant isolation, sensitive data or production recovery.
- Report actual commands and results; never claim a build, test, migration, provider, regulatory or recovery flow passed unless it ran successfully.

