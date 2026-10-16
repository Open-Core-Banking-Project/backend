---
phase: "Phase 1 - Banking Foundation"
epic: "Core Ledger and Financial Posting"
owning_service: "Core Banking and Ledger"
participating_services:
  - Payments and VTU
  - Operations and Workflow
  - Audit and Reconciliation
  - Risk and Reporting
  - Customer and Onboarding
modules:
  - Chart of Accounts
  - Accounting Event Mappings
  - Financial Transactions
  - Journals and Journal Lines
  - Authoritative Balances
  - Holds and Available Funds
  - Reversals and Adjustments
  - Accounting Periods
  - End-of-Day and Period Processing
  - Transaction History and Statement Data
status: Draft
requirements: [LED-001, LED-002, LED-003, LED-004, LED-005, LED-006, LED-007, LED-008, LED-009, LED-010, LED-011, LED-012, LED-013, ACC-005, ACC-007, DAT-001, DAT-002, DAT-003, DAT-004, DAT-005, DAT-008, NFR-PERF-002, NFR-SCAL-001, NFR-RES-002, NFR-DR-001, NFR-DR-002, NFR-OBS-001, NFR-OBS-002, NFR-MNT-001, SEC-009, SEC-010, SEC-011, SEC-013, AUD-001, AUD-002, AUD-003, AUD-004, BR-001, BR-002, BR-003, BR-004, BR-005, BR-006, BR-007, BR-008, BR-010, BR-013, BR-014, BR-015]
stories: [US-018, US-019, US-020, US-021, US-022, US-023]
dependencies:
  - Phase 0 - Messaging, Outbox, Inbox and Idempotency
  - Phase 0 - Observability and CI
  - Phase 1 - Institution and Tenancy
  - Phase 1 - Identity, Roles and Permissions
  - Phase 1 - Products and Accounts
adrs: [ADR-0005]
last_updated: 2026-10-15
---

# Epic: Core Ledger and Financial Posting

## 1. Objective

Deliver the protected accounting source of truth for the platform: atomic balanced double-entry posting, authoritative ledger and available balances, scoped idempotency, immutable journals, linked reversals and adjustments, holds, accounting periods, restartable end-of-day processing, and authoritative transaction-history queries.

Only this bounded context may authoritatively change a customer balance. No controller, payment handler, provider callback, workflow, report, cache, channel or other service may update balances directly or write ledger tables.

ADR-0005 must be approved before production implementation. It must fix the ledger consistency model, balance representation, database transaction boundary, concurrency/locking strategy, overdraft behaviour, account-control freshness policy and reconciliation approach.

## 2. Success measures

- Every posted journal balances total debits and credits within the approved accounting scope and currency before commit.
- Journal header, lines, transaction, balance movements, authoritative balance rows, idempotency outcome, audit evidence and outbox record commit atomically.
- Repeated financial commands with the same scoped idempotency key and same request hash return the original outcome; a different hash is rejected.
- Posted journals and lines cannot be edited or deleted through application or ordinary administrative paths.
- Corrections create linked reversals or adjustments; original postings remain unchanged and traceable.
- Concurrent debits cannot overspend the approved available-funds policy.
- Ledger balance, available balance, holds, liens, blocks and pending authorised amounts remain distinct and reconcilable.
- Fees, commissions, taxes, interest, penalties and waivers are separate typed accounting components.
- Business date, transaction timestamp, posting date and value date remain distinct.
- EOD/month-end/year-end processing resumes from durable checkpoints without duplicate posting.
- Statements and transaction histories derive from authoritative posted records and reconcile to balances.
- Backup restore, replay, balance rebuild and reconciliation demonstrate no duplicate or missing financial outcome.

Performance, availability, capacity, RTO and RPO are release gates only after approved workload and institution targets exist. Do not treat SRS target examples as measured success without running the approved profile.

## 3. Scope

### Included

- Institution/legal-entity chart of accounts and ledger-account lifecycle required for Phase 1.
- Versioned accounting-event mappings from canonical business events to journal templates.
- Financial command validation and scoped idempotency.
- Atomic single-currency double-entry journals and immutable journal lines.
- Financial transaction lifecycle and unique references.
- Customer account ledger balance and authoritative balance snapshots/projections maintained in the posting transaction.
- Available-funds evaluation using approved holds, liens, blocks, restrictions and pending-authorisation policy.
- Hold creation, capture/consume, release and expiry where required by Phase 1 payment flows.
- Full reversal and controlled adjustment foundations.
- Explicit fee, commission, tax, interest, penalty and waiver components.
- Value-dated and backdated policy validation and approval gates.
- Accounting periods, locks and controlled manual journals.
- Restartable EOD, month-end and year-end runs with checkpoints, dependencies and balancing evidence.
- Authoritative transaction-history and statement-data query contracts.
- Ledger-to-balance and ledger-to-event reconciliation.

### Excluded

- Provider calls, payment routing and final provider-state decisions, owned by Payments and VTU/Integration Hub.
- Workflow queue/orchestration, owned by Operations and Workflow. An approval authorises a ledger command; it never proves the command posted.
- Product formula definition, owned by Products and Accounts. Ledger consumes exact approved versions for accrual/posting.
- Generated PDF/Excel/CSV files, watermarking, secure download links and artifact storage, owned by Documents and Statements.
- General-ledger financial statements and regulatory reporting, owned by Risk and Reporting using governed ledger outputs.
- Reconciliation case management and settlement operations, owned by Audit and Reconciliation. That service requests controlled postings; it cannot edit balances.
- Cross-currency/FX posting until currency-pair, rate source, gain/loss, rounding and clearing-account policies are approved.
- Cash/teller/vault, lending, cards, treasury and fixed-deposit-specific subledgers unless separately allocated.
- Invented chart of accounts, accounting mappings, balance formula, overdraft rules, rounding, cut-off, backdate, reversal or EOD policies.

Phase 1 must support the required accounting primitives without pretending that unapproved mappings or financial policies exist.

## 4. Ownership and authority boundaries

| Capability | Authoritative owner | Boundary |
| --- | --- | --- |
| Chart of accounts and ledger account | Ledger module | Versioned/approved financial configuration |
| Account identity/status/restrictions/liens | Account module | Ledger consumes current controls through explicit contract |
| Journal, lines, financial transaction and movements | Ledger module | No direct writes outside Ledger application layer |
| Ledger/available balance and holds | Ledger module | Reports/caches are projections only |
| Product fee/interest/tax rule definition | Product module | Ledger uses exact approved rule/mapping versions |
| Payment instruction/provider status | Payments and VTU | Ledger posts only an authorised canonical financial command |
| Approval workflow | Operations and Workflow | Decision authorises exact command/version; Ledger still validates/posts |
| Reconciliation investigation | Audit and Reconciliation | Exceptions request reversal/adjustment; no balance writes |
| Statement artifact | Documents and Statements | Ledger supplies authoritative posting records/query snapshot |
| Financial reports | Risk and Reporting | Governed read models never become authoritative |

Hard boundaries:

1. Ordinary business callers submit canonical accounting events and business references, not arbitrary debit/credit ledger-account IDs.
2. Ledger resolves the approved accounting mapping and independently calculates/validates components.
3. Only the separately permissioned manual-journal flow may name ledger accounts, and it requires reason, approval and period validation.
4. Provider success/failure does not directly mutate Ledger. Payments decides the eligible canonical command after provider-state controls.
5. Workflow approval is necessary where configured but never sufficient to mark a financial transaction Posted.
6. A published event or API response is not a commit boundary; the PostgreSQL transaction is authoritative.

## 5. Actors, roles and permissions

Human role names are illustrative. Normal posting permissions belong to narrowly scoped service identities.

| Actor | Permission | Contextual scope | Step-up | Approval |
| --- | --- | --- | --- | --- |
| Approved domain service | `ledger.transaction.post` | Institution/event type/source/audience/account/currency/amount | Service authentication | Upstream approval evidence where required |
| Approved domain service | `ledger.hold.create` | Institution/account/purpose/currency/amount | Service authentication | Upstream policy |
| Approved domain service | `ledger.hold.release` | Original hold/source reference | Service authentication | Upstream policy |
| Customer/staff | `ledger.balance.view` | Own or authorised institution/account | Channel policy | None |
| Customer/staff | `ledger.transaction_history.view` | Own or authorised account/period | Sensitive access policy | None |
| Finance officer | `ledger.reversal.propose` | Institution/transaction/type/amount | Required | Maker-checker |
| Finance approver | `ledger.reversal.approve` | Institution/transaction/type/amount | Required | Cannot approve own proposal |
| Finance officer | `ledger.manual_journal.create` | Institution/legal entity/period/currency/accounts/amount | Required | Always approved |
| Finance approver | `ledger.manual_journal.approve` | Same scope | Required | Cannot approve own journal |
| Finance operations | `ledger.period.manage` | Institution/legal entity/period | Required | Dual control by policy |
| Finance operations | `ledger.processing.execute` | Institution/business date/run type | Required | Start/resume/override policy |
| Finance supervisor | `ledger.processing.approve` | Run/checkpoint/exception | Required | Cannot approve own restricted override |
| Auditor | `ledger.journal.view` | Institution/legal entity/period/classification | Required for sensitive detail | Read-only |
| Reconciliation service | `service.ledger.reconciliation.read` | Institution/period/accounting scope | Service authentication | Read-only |
| Reconciliation service | `service.ledger.adjustment.request` | Approved exception/reference | Service authentication | Human approval where required |

Rules:

- No browser, mobile, USSD, chatbot, partner or provider identity receives `ledger.transaction.post` directly.
- Service posting grants restrict issuer, audience, environment, institution, accounting event, source and amount/limit where applicable.
- Manual journals, reversals, period unlocks and backdated postings require enhanced audit and step-up.
- Maker-checker uses stable actor identity across roles, sessions and channels.
- Balance/history permission never grants posting or unrestricted journal access.

## 6. Domain model

### Aggregates and entities

| Concept | Responsibility | Identity | Important relationships |
| --- | --- | --- | --- |
| `LedgerAccount` | Accounting identity, type, currency, legal entity and lifecycle | `LedgerAccountId` | May reference customer Account or GL classification |
| `AccountingEventMappingVersion` | Canonical event-to-journal template | `MappingVersionId` | Event code, component rules and account resolvers |
| `FinancialTransaction` | Business posting request, lifecycle and trace root | `FinancialTransactionId` | One or more journals; idempotency and external refs |
| `JournalEntry` | Atomic balanced accounting entry | `JournalEntryId` | Owns immutable lines and dates |
| `JournalLine` | Debit or credit against one ledger account | `JournalLineId` | Journal, amount, currency, component and sequence |
| `BalanceMovement` | Immutable per-account effect of a posted line | `BalanceMovementId` | Journal line and before/after sequence/reference |
| `AccountBalance` | Authoritative current ledger/available components | `LedgerAccountId` | Versioned and reconciled to movements |
| `Hold` | Temporary reservation reducing approved availability | `HoldId` | Account, source, amount, currency and lifecycle |
| `ReversalLink` | Original/reversal relationship and scope | `ReversalLinkId` | Original transaction/journal and reversing transaction |
| `AdjustmentRequest` | Controlled correction proposal | `AdjustmentRequestId` | Evidence, approval and generated transaction |
| `AccountingPeriod` | Open/closed/locked period by legal entity | `AccountingPeriodId` | Posting policy and close evidence |
| `ProcessingRun` | EOD/month-end/year-end orchestration state | `ProcessingRunId` | Business date, steps and checkpoints |
| `ProcessingStep` | Restartable dependency-aware work unit | `ProcessingStepId` | Run, partition/checkpoint and output summary |
| `IdempotencyRecord` | Scoped request hash and durable original outcome | `IdempotencyRecordId` | Institution/source/operation/key |

### Value objects

- `Money`: decimal amount plus explicit ISO currency.
- `DebitCreditIndicator`, `AccountingComponentType` and `AccountingEventCode`.
- `TransactionReference`, `JournalReference`, `ExternalReference`, `CorrelationId` and `IdempotencyKey`.
- `AccountingScope`: institution, legal entity, currency and approved additional balancing dimension.
- `TransactionTimestamp`, `BusinessDate`, `PostingDate` and `ValueDate` as separate types/concepts.
- `BalanceVersion`, `ControlVersion`, `MappingVersion` and `ProductRuleVersion`.
- `PostingSource`, `PostingPurpose`, `ReversalReason`, `AdjustmentReason` and `EvidenceReference`.
- `HoldPurpose`, `HoldStatus`, `Expiry` and `CaptureAmount`.

### Non-negotiable invariants

1. Only Ledger application use cases can persist journals, lines, movements or authoritative balances.
2. Every Posted financial transaction has at least one journal; every journal has at least two lines.
3. Total debits equal total credits in each approved accounting scope and currency before commit.
4. Amounts are positive decimals on lines; direction is represented explicitly, not by mixed sign conventions.
5. Posted journal header, lines and movements are immutable. No normal update/delete operation exists.
6. Every financial command has institution, source/operation, idempotency key and canonical request hash.
7. Same key/scope/hash returns the original outcome; same key/scope with another hash is a conflict.
8. Transaction, journal and configured external references are unique in their defined scopes.
9. Account/currency on each line matches the ledger account; implicit currency conversion is prohibited.
10. A normal posting resolves an effective approved accounting mapping version for the transaction dates/event.
11. Fees, commissions, taxes, interest, penalties and waivers use separate components/lines; they are not hidden in the principal amount.
12. Available-funds validation uses the approved formula and current controls inside the posting transaction.
13. Concurrent posting cannot allow a result forbidden by the available-funds/overdraft policy.
14. A reversal references an original Posted transaction and creates new opposite entries; it never changes original lines.
15. Reversed amount cannot exceed the policy-approved reversible remainder under concurrency.
16. A closed/locked period rejects ordinary posting; approved backdate/adjustment paths remain explicit and audited.
17. Business date, transaction timestamp, posting date and value date are never substituted for one another.
18. An EOD step cannot be marked complete until its postings, checkpoint, summaries and outbox evidence commit.
19. Read models, Redis, reports and exported statements cannot authorise a debit or replace Ledger state.
20. No network call occurs while the posting database transaction holds financial locks.

### Financial transaction lifecycle

```text
Received -> PendingAuthorisation -> ReadyToPost -> Posted -> Disputed
    |               |                  |             -> Reversed
    |               +-> Rejected       +-> Rejected
    +-> Rejected                         -> FailedBeforeCommit
```

- `Posted` is possible only after the atomic database commit.
- A crash/timeout after commit does not change Posted to Failed; the idempotency query returns the committed result.
- `Disputed` is a business overlay on a posted outcome; it does not make journal lines mutable.
- `Reversed` means an approved reversal transaction posted, not that the original disappeared.

### Hold lifecycle

```text
Created -> Active -> PartiallyCaptured -> Captured
                  -> Released
                  -> Expired
                  -> CancelledBeforeActivation
```

Capture/release amounts cannot exceed the active remainder. Expiry uses business/timestamp policy explicitly and is idempotent.

### Accounting period lifecycle

```text
Future -> Open -> SoftClosed -> Closed -> Locked
                    |             |
                    +-> Open      +-> ReopenedByException
```

Exact states and reopen authority require approval. Period transitions never rewrite posted entries.

### Processing run lifecycle

```text
Planned -> Running -> PausedForFailure -> Running -> Completed
                  -> AwaitingApproval -> Running
                  -> FailedTerminal
```

The same institution, run type and business date cannot have two active authoritative runs unless an approved partition model proves they are disjoint.

## 7. Posting model and atomic transaction

### Normal posting command

Normal callers submit:

- Trusted institution and service identity context.
- Canonical accounting event code and event version.
- Stable source business reference and correlation/causation IDs.
- Scoped idempotency key.
- Explicit principal and component inputs with currencies.
- Source/destination customer-account or business references required by the mapping.
- Transaction timestamp, requested value date and upstream approval/risk references where required.
- Expected account-control and relevant configuration versions when the contract requires them.

Callers do not supply arbitrary GL account IDs, final fee/tax calculations, approval truth or a claimed final balance.

### Posting transaction sequence

```text
validate trusted caller, tenant and contract
  -> canonicalise request and calculate request hash
  -> resolve locally available approved mapping/config/reference versions
  -> load affected ledger accounts and account controls
  -> begin PostgreSQL transaction
  -> atomically claim/read and lock scoped idempotency record
  -> lock affected balance/control rows in deterministic order
  -> revalidate status, period, dates, controls, limits and available funds
  -> build journal and explicit component lines
  -> assert debit equals credit per accounting scope/currency
  -> insert financial transaction, journal, lines and movements
  -> update authoritative balance rows/version
  -> persist audit evidence, original outcome and outbox events
  -> commit once
  -> return stable Posted result
```

Any network-dependent fact must be resolved before the lock-holding transaction and represented by a version/reference. Facts that can change correctness are revalidated from local authoritative/reference state in the transaction.

### Concurrency baseline

- PostgreSQL is the financial concurrency authority; Redis/distributed locks cannot protect balances.
- Lock every affected authoritative balance/control row in a deterministic key order to avoid overspend and reduce deadlocks.
- Recompute available funds after locks are acquired.
- Use unique constraints for idempotency and references, and retry only database errors classified as safe/transient.
- Deadlock/serialization retry reuses the same stable IDs, idempotency key and request hash.
- The exact isolation/locking strategy and retry limits are fixed by ADR-0005 and proven under contention tests.

## 8. Balance and control model

The API and database must keep these concepts distinct:

| Concept | Meaning | Authority |
| --- | --- | --- |
| Ledger balance | Net effect of posted journal movements for the customer ledger account | Ledger |
| Available balance | Amount available under approved overdraft, holds, liens, blocks, restrictions and pending policy | Ledger calculation/snapshot |
| Hold | Temporary reserved amount with lifecycle/source | Ledger |
| Lien | Approved claim/control with amount and authority | Account metadata; Ledger enforces in availability |
| Block/restriction | Prohibition by direction/channel/type or broad scope | Account metadata; Ledger enforces |
| Pending authorised amount | Approved reservation not yet finally posted where supported | Ledger/payment contract |

Do not encode a universal formula until approved. ADR-0005/accounting policy must define debit/credit sign conventions, overdraft/credit-limit treatment, hold and lien precedence, pending amounts, credit restrictions and when snapshots update.

Authoritative balance rows are transactionally maintained accelerators over immutable movements, not an excuse to lose movement history. Rebuild must reproduce balances from a verified checkpoint plus movements, and reconciliation must compare calculated and stored results.

Balance queries return amount/currency, balance version, as-of timestamp, business date and applicable control summary. Cached copies are explicitly non-authoritative and cannot be used for posting decisions.

## 9. Accounting mappings and components

Accounting mappings are versioned, approved and effective-dated. Each mapping declares:

- Canonical event code/version and supported institution/legal entity.
- Required input components and validation schema.
- Account resolvers for customer, settlement, suspense, fee, commission, tax, interest, penalty and other approved roles.
- Debit/credit template and balancing scope.
- Currency and cross-currency prohibition/handling.
- Date and period policy.
- Rounding/calculation version references.
- Reversal/adjustment eligibility and mapping.
- Required upstream approval/risk/control evidence.

Mapping simulation uses synthetic entries and proves balance by scope/currency. Publication requires finance approval. A missing, ambiguous, inactive or stale mapping rejects the posting; no suspense account is chosen implicitly.

Suspense use requires an explicit approved mapping and creates reconciliation visibility. It is not a generic fallback for unknown accounts.

## 10. Core use cases

### LED-UC-01 - Post a canonical financial transaction

- Actor: Approved service identity.
- Permission: `ledger.transaction.post` scoped to event/institution/audience.
- Flow: Execute the atomic posting sequence in Section 7.
- Idempotency: Institution, caller/source, operation/event and key; canonical request hash protected.
- Success: Stable transaction/journal refs, Posted status, posting/value/business dates and resulting balance versions.
- Rejections: `ledger_unbalanced`, `ledger_insufficient_available_funds`, `ledger_account_restricted`, `ledger_period_closed`, `ledger_mapping_unavailable`, `ledger_currency_mismatch`, `ledger_idempotency_conflict`.

### LED-UC-02 - Create, capture or release a hold

- Actor: Approved Payments/service identity.
- Flow: Validate account/control/current availability, lock account/hold rows, create or transition the hold, update only the reserved/available components and balance version, and commit event/audit/idempotency atomically. A hold transition never changes ledger balance unless a separate approved journal posts.
- Guard: A hold is not a posted journal unless the approved accounting policy explicitly requires one. Capture normally links to the final posting transaction.
- Concurrency: Capture/release/expiry serialize on the hold and affected balance.

### LED-UC-03 - Reverse a posted transaction

- Actor: Finance maker/approver or approved automated compensation path.
- Flow: Load original and reversible remainder, validate policy/period/reconciliation/provider state and approval, build exact linked opposite/approved reversal mapping, post atomically and publish link/event.
- Guard: No original mutation, no duplicate reversal, no reversal based solely on provider timeout, redirect or unverified callback.
- Partial reversal: Disabled until explicit allocation/component/tax/fee policy is approved; if enabled, remainder is locked and tracked atomically.

### LED-UC-04 - Post a manual journal or adjustment

- Actor: Finance officer and separate approver.
- Flow: Create proposal with balanced lines, reason and evidence; validate accounts/currency/period/date; Workflow approves exact hash; Ledger revalidates and posts.
- Guard: Manual access cannot target customer accounts or closed periods unless policy explicitly permits it. No self-approval or direct database edit.

### LED-UC-05 - Query balances and transaction history

- Actor: Customer/staff/service with resource permission.
- Flow: Resolve authorised account, query authoritative balance row and immutable posted history, apply masking and bounded period/pagination.
- Guard: Read does not mutate state. Running balance derives from posted sequence/checkpoint and states source/as-of.

### LED-UC-06 - Manage accounting periods

- Actor: Finance operations and approver.
- Flow: Validate run/reconciliation prerequisites, transition period, record evidence and publish event.
- Guard: Close/lock never modifies journals. Reopen/unlock is exceptional, time-bound if applicable and enhanced-audit.

### LED-UC-07 - Execute or resume EOD/period processing

- Actor: Scheduler/service plus authorised operations controls.
- Flow: Create unique run, resolve ordered step graph, claim a partition/checkpoint, execute idempotent posting batches, commit each step/checkpoint, stop dependents on failure and produce balancing summary.
- Restart: Resume from last committed checkpoint using original run/batch/idempotency identities.
- Completion: Requires all required steps, reconciliation/balancing checks and approvals.

### LED-UC-08 - Supply statement/receipt data

- Actor: Documents/Statements service identity after requester authorisation.
- Flow: Validate account and query scope, create stable statement-data snapshot/reference from posted records, return paginated/streamed canonical lines and opening/closing reconciliation totals.
- Guard: Pending/failed/unposted items are labelled separately if included; they cannot alter posted totals.

## 11. API contracts

Financial mutation APIs are internal service contracts. They are never exposed directly to browsers or external providers.

| Method | Route | Permission | Request | Response | Idempotent |
| --- | --- | --- | --- | --- | --- |
| POST | `/internal/v1/ledger/transactions` | `ledger.transaction.post` | `PostAccountingEventRequest` | `FinancialTransactionResponse` | Required |
| POST | `/internal/v1/ledger/transaction-lookups` | Same trusted source/scope | Idempotency/source reference in protected body | `FinancialTransactionResponse` | Yes |
| POST | `/internal/v1/ledger/holds` | `ledger.hold.create` | `CreateHoldRequest` | `HoldResponse` | Required |
| POST | `/internal/v1/ledger/holds/{id}/captures` | `ledger.hold.capture` | `CaptureHoldRequest` | `HoldResponse` | Required |
| POST | `/internal/v1/ledger/holds/{id}/releases` | `ledger.hold.release` | `ReleaseHoldRequest` | `HoldResponse` | Required |
| POST | `/api/v1/ledger/reversal-requests` | `ledger.reversal.propose` | `CreateReversalRequest` | `ReversalRequestResponse` | By request ID |
| POST | `/api/v1/ledger/manual-journals` | `ledger.manual_journal.create` | `CreateManualJournalRequest` | `ManualJournalResponse` | By request ID |
| POST | `/api/v1/ledger/periods/{id}/transition-proposals` | `ledger.period.manage` | `PeriodTransitionRequest` | `PeriodResponse` | By request ID |
| POST | `/api/v1/ledger/processing-runs` | `ledger.processing.execute` | `StartProcessingRunRequest` | `ProcessingRunResponse` | By institution/type/date |
| POST | `/api/v1/ledger/processing-runs/{id}/resume` | `ledger.processing.execute` | Checkpoint/expected version | `ProcessingRunResponse` | Yes |
| GET | `/api/v1/accounts/{id}/balances` | Self or `ledger.balance.view` | Trusted scope | `AccountBalanceResponse` | Yes |
| GET | `/api/v1/accounts/{id}/transactions` | Self or `ledger.transaction_history.view` | Bounded dates/cursor/status | `TransactionPage` | Yes |
| POST | `/internal/v1/ledger/statement-data-requests` | Document service permission | Account/period/snapshot request | `StatementDataResponse` | Required |
| POST | `/internal/v1/ledger-change-requests/{id}/approval-decisions` | Workflow service permission | Exact decision/hash | Change response | By decision ID |

Contract requirements:

- Every mutation includes correlation ID; every financial command includes idempotency key and stable source business reference.
- Idempotency keys, external references and full account references must not be placed in URL paths or query strings where infrastructure may log them.
- Amount/currency, event code/version, account refs and requested dates are explicit. Currency is never inferred.
- Normal posting requests cannot specify arbitrary journal lines/accounts.
- Responses distinguish `Received`, `PendingAuthorisation`, `Posted`, `Rejected`, `FailedBeforeCommit`, `Reversed` and `Disputed` without ambiguity.
- Timeouts after request submission instruct callers to query by idempotency/source reference; callers must not generate a new key blindly.
- RFC 7807 errors use stable codes and do not leak balances, hidden accounts, mapping internals or database errors.
- Transaction history is cursor-paginated in stable posting sequence, with bounded periods and explicit posted/pending semantics.

## 12. Event contracts

### Produced events

| Event | Trigger | Key fields | Ordering key | Consumers |
| --- | --- | --- | --- | --- |
| `FinancialTransactionPosted.v1` | Atomic posting commits | Transaction/journal refs, institution, event code, component totals, dates, source ref | Financial transaction ID | Payments, Audit/Reconciliation, Customer notifications, Reporting |
| `FinancialTransactionRejected.v1` | Durable business rejection where event is required | Transaction/request ref, safe reason, institution/source | Source business ref | Initiator, Audit |
| `FinancialTransactionReversed.v1` | Linked reversal posts | Original/reversal refs, scope/amounts, reason category | Original transaction ID | Payments, Reconciliation, Reporting, Notifications |
| `FinancialTransactionDisputed.v1` | Dispute overlay accepted | Transaction ref, dispute/case ref, status | Transaction ID | Payments, Workflow, Reconciliation |
| `AccountBalanceChanged.v1` | Posting/hold/control changes authoritative balance version | Account ref, currency, version, optional minimal amounts by approved contract | Account ID | Channels/read models, Audit/Reconciliation |
| `HoldChanged.v1` | Hold lifecycle commits | Hold/account/source refs, amount/currency/status/remainder/version | Account ID | Payments, Channels, Reconciliation |
| `AccountingPeriodChanged.v1` | Period transition commits | Institution/legal entity, period, old/new status | Period ID | Payments, Product/Processing, Reporting, Audit |
| `ProcessingRunChanged.v1` | Run/step/checkpoint changes | Run/type/date/status/checkpoint/summary ref | Run ID | Operations, Audit/Reconciliation, Reporting |
| `StatementDataPrepared.v1` | Stable data snapshot/reference ready | Request/account refs, period, snapshot/version, totals/hash | Account ID | Documents and Statements |

### Consumed events

| Event | Producer | Consumer behaviour | Deduplication key | Failure handling |
| --- | --- | --- | --- | --- |
| `AccountActivated.v1` | Account module | Create/link approved customer ledger account and zero-state balance metadata | Event ID/account version | Retry; account remains non-postable |
| `AccountStatusChanged.v1` | Account module | Update local posting-control reference | Event ID/control version | Gap blocks affected postings and triggers resync |
| `AccountRestrictionChanged.v1` | Account module | Update restriction snapshot/version | Event ID/control version | Fail closed according to ADR |
| `AccountLienChanged.v1` | Account module | Update lien availability component | Event ID/control version | Serialize/reconcile affected balance |
| `ProductVersionPublished.v1` | Product module | Refresh product/mapping/rule references | Event ID/version | Reject dependent posting if stale |
| `BusinessDateAdvanced.v1` | Institution module | Make new date eligible for controlled processing | Event ID/date version | EOD/date-sensitive posting pauses on gap |
| `ApprovalDecided.v1` | Workflow | Apply exact reversal/manual/period/backdate decision | Decision ID | Remain pending on mismatch |
| `PaymentOutcomeConfirmed.v1` | Payments and VTU | Map eligible confirmed business outcome to canonical posting command | Event ID | Inbox/idempotency; provider uncertainty not accepted |
| `ReconciliationAdjustmentApproved.v1` | Audit and Reconciliation/Workflow | Create controlled adjustment command | Decision/event ID | Manual investigation on mismatch |

Financial events contain explicit currency and component totals where necessary but exclude customer PII, secrets and unnecessary account details. Event consumers cannot use `AccountBalanceChanged` as the posting authority.

Outbox events are inserted in the posting transaction and published later. Replay cannot create another posting because consumer and financial idempotency scopes are independent and durable.

## 13. Database design

### Ownership

- Database: `core_ledger_db`.
- Schemas: `ledger`, `processing`, relevant read-only references under `account`/`product`, and `integration`.
- Migration assembly: Core Banking and Ledger Infrastructure project.
- Runtime principal: Ledger writer is isolated from other services/modules; reporting and reconciliation receive controlled APIs/read feeds, not write access.

### Ledger tables

| Table | Purpose | Key constraints | Mutability/retention |
| --- | --- | --- | --- |
| `ledger.ledger_accounts` | Customer and GL accounting identities | Institution/legal entity/code/currency uniqueness; status | Controlled lifecycle; permanent |
| `ledger.accounting_event_mappings` | Stable mapping identity | Institution/event code unique | Controlled |
| `ledger.accounting_mapping_versions` | Immutable approved templates | Mapping/version unique; effective constraints/hash | Append-only versions |
| `ledger.financial_transactions` | Posting trace root/current overlay status | Scoped transaction/source/external refs unique | Posted core immutable; controlled overlay |
| `ledger.transaction_status_history` | Append-only lifecycle/dispute/reversal states | Transaction/sequence unique | Append-only/permanent |
| `ledger.journal_entries` | Posted journal header/scope/dates | Journal ref unique; posted status | Append-only/permanent |
| `ledger.journal_lines` | Debit/credit lines/components | Journal/sequence unique; positive amount/currency | Append-only/permanent |
| `ledger.balance_movements` | Per-account immutable line effects | Journal-line/account unique; account sequence | Append-only/permanent |
| `ledger.account_balances` | Authoritative current balance components/version | Ledger account unique; numeric precision/currency | Transactional/versioned |
| `ledger.balance_checkpoints` | Verified rebuild/reconciliation checkpoint | Account/sequence/version unique | Append-only |
| `ledger.holds` | Reservation/remainder/status | Institution/source ref and idempotency uniqueness | Controlled lifecycle/permanent history |
| `ledger.hold_status_history` | Hold transitions | Hold/sequence unique | Append-only |
| `ledger.reversal_links` | Original/reversal and amount/scope | Reversal transaction unique; original constraints | Append-only |
| `ledger.adjustment_requests` | Controlled correction proposal/evidence | Request ref/hash unique | Permanent evidence |
| `ledger.manual_journal_requests` | Manual journal proposal/approval/post ref | Institution/request key unique | Permanent evidence |
| `ledger.reconciliation_checkpoints` | Ledger/balance totals and discrepancies | Scope/date/version unique | Append-only |

### Processing and integration tables

| Table | Purpose | Key constraints | Mutability/retention |
| --- | --- | --- | --- |
| `processing.accounting_periods` | Legal entity period status/lock | Scope/period unique | Controlled plus history |
| `processing.period_status_history` | Period transitions/evidence | Period/sequence unique | Append-only |
| `processing.end_of_day_runs` | Institution/business-date run | Institution/type/date/run key unique | Controlled lifecycle |
| `processing.end_of_day_steps` | Dependency step state/summary | Run/step/partition unique | Controlled lifecycle/history |
| `processing.job_checkpoints` | Durable resume cursor/output hash | Run/step/partition/checkpoint unique | Append-only progression |
| `processing.posting_batches` | Deterministic financial batch identity | Institution/run/batch key unique | Permanent evidence |
| `processing.interest_accruals` | Exact rule/balance/date inputs and posting link | Account/date/rule version unique | Append-only/correct by adjustment |
| `integration.idempotency_records` | Request hash and original durable response | Institution/source/operation/key unique | Retained beyond retry horizon by policy |
| `integration.outbox_messages` | Committed event publication | Event ID unique | Operational retention |
| `integration.inbox_messages` | Consumer deduplication | Consumer/event ID unique | Replay horizon |

### Database enforcement

- Explicit PostgreSQL `numeric(precision, scale)` is approved for every monetary/rate column; `money`, floating types and implicit conversions are prohibited.
- Currency is non-null on every monetary record.
- Check constraints require positive line amounts and valid debit/credit/status/date relationships.
- Deferred constraint trigger or equivalent approved database guard verifies each newly Posted journal balances per scope/currency at transaction commit, supplementing domain validation.
- Runtime database roles cannot update/delete posted journal entries, lines or movements.
- Unique constraints protect idempotency, journal/transaction/source refs, external refs where applicable, account movement sequence and reversal links.
- Balance rows use explicit version and are locked in deterministic order for postings.
- Partitioning may be introduced by approved volume/retention design, but must preserve uniqueness, ordering, restore and query correctness.
- Database triggers must not hide business posting logic; guards enforce invariants and reject corruption.

### Migration and cutover

- No default chart of accounts or mapping is invented/seeded as production truth. Synthetic mappings are development-only and visibly labelled.
- Chart/mapping import requires totals, duplicate checks, account-type/currency validation, approval and dry-run simulation.
- Legacy financial migration uses a separately approved opening-balance/cutover design with balanced control journals, source totals and independent reconciliation; ordinary account activation cannot do this.
- Released migrations are append-only. Financial rows are never deleted, rewritten or backfilled without explicit migration/cutover controls.
- Long-running changes use expand/backfill/verify/switch/contract stages with dual-read/write only when formally designed.

## 14. Reversals, dates and period controls

### Reversal and adjustment

- Full reversal uses the approved reversal mapping and opposite accounting effects linked line/transaction-wise where required.
- Reversal eligibility considers original status, remaining reversible amount, period, date, account state, provider/reconciliation status and prior reversals.
- A failed business operation is not automatically a reversal; if nothing posted, there is nothing to reverse.
- A provider timeout/uncertain result never triggers blind reversal.
- Adjustments correct accounting through new balanced entries and reference the reason/evidence/original where applicable.
- Fee/tax/commission reversal treatment is explicit per mapping; principal-only assumptions are prohibited.

### Dates

- Transaction timestamp records when the business request/event occurred with offset.
- Business date is the institution processing date.
- Posting date assigns the journal to an accounting period.
- Value date determines approved economic/accrual effect.
- Backdated/value-dated requests validate open periods, allowed windows, product policy, approval and downstream recalculation/reconciliation impacts.
- Future-dated requests are scheduled instructions, not silently posted with a future timestamp.

### Periods

- Period lookup and lock validation occurs inside the posting transaction.
- Soft-close behaviour, allowed event categories and final-lock authority are configured/approved.
- Exceptional reopen records actor, reason, scope, approval, effective duration and post-close reconciliation.
- Closing a period requires balancing/reconciliation evidence; it cannot mask an imbalance.

## 15. EOD and batch processing

The approved run graph may include prerequisites, accrual calculation, interest/fee/tax posting, dormancy handoff, scheduled instructions, period checks, reconciliation summaries and business-date advancement gates. The exact graph remains an institution decision.

Required mechanics:

- One stable run ID per institution/type/business date and deterministic batch/partition IDs.
- Explicit step dependencies and no start when a prerequisite failed.
- Lease/ownership with heartbeat for workers, but database checkpoint/idempotency remains authoritative.
- Each posting item uses a deterministic financial idempotency key derived from run/step/account/event/rule version.
- Checkpoint commits only after all effects in its defined atomic unit commit.
- Restart resumes from the last verified checkpoint; it does not reset the run or generate new identities.
- Parallel partitions must be disjoint by a proven key and merge into a balancing summary.
- Completion records counts, debit/credit totals by scope/currency, exceptions, mapping/rule versions and reconciliation result.
- Business date advances only through the approved institution process after required run gates.

## 16. Security, audit and observability

### Security

- All posting endpoints require authenticated service identities with issuer/audience/environment/event scopes.
- Tenant, account ownership, amount, fee, settlement account, mapping and approval claims from callers are independently validated.
- Normal callers cannot select arbitrary GL accounts or privileged event codes.
- Protect against replay at both message inbox and financial idempotency layers.
- Manual/reversal/period/EOD privileged actions require step-up, reason, exact approval and conflict-of-duty enforcement.
- Full account numbers and sensitive narratives/evidence are masked in APIs/logs/events; narration length/content is validated and classified.
- Rate/volume limits and circuit breakers must not create false financial outcomes. Accepted commands remain queryable by stable ID/key.

### Audit evidence

Record mapping publication/use, posting request decision, financial commit, hold lifecycle, reversal/adjustment, manual journal, period transition, EOD start/checkpoint/failure/resume/completion, balance reconciliation and sensitive history access.

Evidence includes actor/service, permission/audience, institution/legal entity, event/mapping/rule versions, transaction/journal/source refs, component totals by currency, dates, reason/approval, idempotency scope, balance versions, correlation/causation and timestamp. Do not duplicate full sensitive payloads.

### Metrics and traces

Minimum metrics:

- Posting accepted/posted/rejected/failed-before-commit by event/safe reason.
- Posting latency and lock wait/deadlock/serialization retry rates.
- Idempotent replay and conflicting-key counts.
- Debit/credit imbalance attempts and database-guard rejections.
- Insufficient-funds/control/period/mapping rejection rates.
- Hold active/expired/stuck and capture/release conflicts.
- Reversal/adjustment/manual-journal state and approval age.
- Balance movement/snapshot reconciliation discrepancy count/value.
- Outbox backlog, event publication lag and consumer version gaps.
- EOD run/step duration, checkpoint age, posting totals and failure state.
- Statement query volume/latency and snapshot reconciliation failure.

Traces cross source service, Workflow, Ledger transaction, outbox/event, reconciliation and document request using correlation/causation identifiers. Never add raw command bodies, full account numbers or unrestricted narrations as span attributes.

## 17. Failure, retry and recovery

| Failure | Authoritative outcome | Recovery |
| --- | --- | --- |
| Validation fails before transaction | Rejected; no posting | Correct input/new appropriate request |
| Deadlock/serialization failure before commit | Not posted | Bounded safe retry with same IDs/key/hash |
| Process/network fails after commit | Posted despite missing response | Query original outcome by idempotency/source ref |
| Outbox publishing fails after commit | Posting remains Posted | Durable publisher retries; no reposting |
| Mapping/control/version missing | Rejected or pending, never guessed | Resync approved reference then retry same logical command policy |
| Balance/control lock contention | No partial outcome | Bounded wait/retry; alert against target |
| EOD item fails | Step/run pauses; later dependencies stop | Fix cause and resume original checkpoint/IDs |
| Event replay | Original outcome returned/consumer deduplicates | No duplicate posting/event side effect |
| Balance reconciliation mismatch | Financial writes may be gated by severity | Investigate/rebuild from verified movements; no ad hoc edit |
| Restore/PITR | Traffic closed until consistency proven | Reconcile ledger, balances, idempotency, outbox and consumers |

Recovery rules:

- Never tell callers to retry with a new idempotency key merely because they timed out.
- Never repair balances by direct arithmetic update. Rebuild a projection from immutable movements or post an approved adjustment.
- A balance rebuild runs in controlled mode, records checkpoint/hash/totals and compares before switching the authoritative projection.
- PITR cutover reconciles transaction/journal/line counts, debit-credit totals, latest movement sequences, balances, idempotency outcomes, reversal links, periods, EOD checkpoints and outbox publication.
- Reconciliation exceptions remain open until resolved, authorised or formally written off.

## 18. Configuration

| Setting | Owner | Secret | Validation | Reload behaviour |
| --- | --- | --- | --- | --- |
| Chart of accounts | Finance | No | Types, currency, legal entity, control accounts | Versioned/approved |
| Accounting-event mappings | Finance/Product/domain owner | No | Balanced simulation, resolvers, component/date policy | Versioned/effective-dated |
| Monetary precision/rate scale/rounding | Finance | No | Currency/product/accounting tests | Versioned; never implicit |
| Available-funds/overdraft formula | Finance/Risk/Core | No | Components, precedence and contention tests | Versioned/approved |
| Posting isolation/lock/retry policy | Core architecture | No | ADR and load/contention proof | Deployment-controlled |
| Backdate/value-date windows | Finance/Compliance | No | Period/product/event scope and approval | Versioned/effective-dated |
| Period calendar/close policy | Finance | No | Legal entity/date/state graph | Versioned |
| Reversal/adjustment policy | Finance/Operations | No | Event/component/period/provider conditions | Versioned |
| EOD step graph/partitioning | Finance/Core operations | No | Dependencies, idempotency and totals | Versioned/run snapshot |
| Financial retention/partition policy | Compliance/DBA | No | Legal hold, query and restore | Controlled migration |

Financial configuration is immutable after effective use. Missing or stale critical configuration rejects/pauses processing; feature flags cannot bypass balancing, idempotency, authorisation, audit or period controls.

## 19. Implementation slices

1. Approve ADR-0005 plus chart, mapping, precision, rounding, available-funds, reversal, period and EOD decisions.
2. Define Money/date/reference/idempotency contracts and architecture guards that prohibit Ledger persistence access outside the module.
3. Implement ledger-account and approved mapping-version lifecycle with balanced simulation.
4. Implement transaction/journal/line domain invariants and database append-only/balance guards.
5. Implement scoped idempotency claim/outcome and canonical request hashing under concurrency.
6. Implement atomic single-currency posting transaction with balance rows, movements, audit and outbox.
7. Integrate account controls and prove concurrent debit/available-funds safety using real PostgreSQL contention tests.
8. Implement hold lifecycle and capture-to-posting linkage.
9. Implement full reversal plus original/reversal trace and duplicate/concurrent reversal prevention.
10. Implement manual journal/adjustment and Workflow decision application.
11. Implement accounting periods, date policies and controlled backdated/value-dated posting.
12. Implement EOD run graph, batches, checkpoints, restart and balancing summaries for approved Phase 1 steps.
13. Implement authoritative balance/history/statement-data queries and controlled document handoff.
14. Implement balance rebuild, reconciliation feeds, dashboards, alerts and operational runbooks.
15. Execute security, concurrency, resilience, performance, backup/restore and end-to-end acceptance evidence.

Each slice begins with failing invariant tests and leaves the solution unable to expose unsafe partial financial behaviour. Synthetic chart/mappings remain development-only until finance approval.

## 20. Test plan

| Test ID | Requirement/story | Level | Scenario | Expected evidence |
| --- | --- | --- | --- | --- |
| LED-001 | LED-001, LED-002 / US-018 | Unit | Balanced two/multi-line journal | Accepted only when debit equals credit by scope/currency |
| LED-002 | LED-002 / BR-002 | Integration | Attempt unbalanced commit | Domain and database guard reject atomically |
| LED-003 | LED-001 | Integration | Failure inserting a line/movement | No header, lines, balance or event commits |
| LED-004 | LED-003 / BR-003 | Security | Update/delete posted journal/line | Application absent/denied and DB privilege/guard rejects |
| LED-005 | LED-004, LED-005 / US-020 | Concurrency | Many identical idempotent commands | One posting; all return original outcome |
| LED-006 | LED-004, LED-005 / US-020 | Security | Same key with different payload | Conflict, audit and no second posting |
| LED-007 | LED-006 | Integration | Duplicate transaction/journal/external refs | Scoped uniqueness rejects ambiguity |
| LED-008 | LED-008 | Domain | Resolve exact effective event mapping | Exact approved version recorded on journal |
| LED-009 | LED-008 | Security | Mapping missing/ambiguous/stale | Posting rejected; no implicit suspense fallback |
| LED-010 | LED-009 | Domain | Principal plus fee/tax/commission components | Separate balanced typed lines and totals |
| LED-011 | DAT-004 | Unit | Precision/rounding boundary | Approved decimal result; no float conversion |
| LED-012 | DAT-004 | Security | Currency mismatch/implicit conversion | Rejected before commit |
| LED-013 | ACC-005 / US-019 | Integration | Credit/debit changes ledger/available balance | Distinct authoritative values and movements |
| LED-014 | US-019 | Concurrency | Concurrent debits near available limit | Approved policy permits at most valid total |
| LED-015 | US-019 | Integration | Active hold/lien/block/restriction | Each distinct component affects approved decision |
| LED-016 | US-019 / BR-015 | Architecture | Attempt posting from cached balance | Cache never participates in financial decision |
| LED-017 | LED-007 | Domain | Posted transaction becomes disputed | Journal unchanged; overlay/history explicit |
| LED-018 | LED-003 / US-021 | Integration | Full reversal | Opposite balanced journal linked to immutable original |
| LED-019 | LED-003 / US-021 | Concurrency | Two reversal requests race | Reversible remainder prevents duplicate/excess reversal |
| LED-020 | BR-006 | Security | Reverse solely because provider timed out | Denied pending authoritative provider resolution |
| LED-021 | BR-008 / US-021 | Security | Reversal maker approves own request | Denied and audited |
| LED-022 | LED-011 / BR-010 | Integration | Approved manual journal | Exact approved hash, balanced lines, reason/evidence |
| LED-023 | LED-011 | Security | Manual journal names unauthorised/customer account | Denied by scope/policy |
| LED-024 | LED-010 / BR-014 | Domain | Backdated/value-dated posting | Distinct dates and exact policy/approval evidence |
| LED-025 | LED-010, LED-011 | Integration | Post to closed/locked period | Denied except approved exceptional path |
| LED-026 | LED-012 / US-022 | Integration | EOD happy path | Ordered checkpoints, totals and completion evidence |
| LED-027 | LED-012 / US-022 | Recovery | Crash after checkpoint/item commit | Resume original run without duplicate posting |
| LED-028 | LED-012 / US-022 | Integration | Step failure | Dependents stop; alert and resumable state |
| LED-029 | LED-012 | Concurrency | Two workers claim same run partition | One authoritative owner/effect per checkpoint |
| LED-030 | ACC-007, LED-012 | Integration | Approved interest batch | Exact product/rule/input versions and idempotent postings |
| LED-031 | LED-013 / US-023 | Integration | Query statement period | Posted records reconcile opening/movements/closing totals |
| LED-032 | LED-013 | Security | Unauthorised account/period query | Denied without resource disclosure |
| LED-033 | LED-013 | Contract | Include pending/failed item | Clearly non-posted; excluded from posted totals |
| LED-034 | NFR-RES-002 | Messaging | Replay input/output events | No duplicate posting, reversal, hold or downstream event |
| LED-035 | NFR-OBS-001, AUD-004 | End-to-end | Trace source to journal/event/reconciliation | Complete correlated references and evidence |
| LED-036 | SEC-013, NFR-OBS-002 | Security | Inspect logs/traces/events/errors | No full account/PII/secret or command-body leakage |
| LED-037 | DAT-008 | Recovery | Rebuild balance from checkpoint/movements | Exact match; no journal mutation |
| LED-038 | NFR-DR-001 | Recovery | Restore/PITR and replay | Balanced totals, balances, idempotency and events reconcile |
| LED-039 | NFR-PERF-002 | Performance | Approved posting/lock-contention profile | Measured target evidence; no invariant loss |
| LED-040 | LED-001..LED-013 | End-to-end | Post, hold, reverse, EOD and statement journey | All invariants, dates, versions and audit evidence pass |

Property-based tests generate balanced/unbalanced journals, component combinations and rounding boundaries. Fault-injection tests stop the process at each transaction/outbox/checkpoint boundary. Golden accounting expectations require Finance approval.

## 21. Acceptance criteria

- [ ] LED-001 through LED-013 and US-018 through US-023 trace to decisions, contracts, code and tests.
- [ ] ADR-0005 and required accounting policies are approved before production posting.
- [ ] Journal/lines/movements are immutable and database-protected after posting.
- [ ] Atomic posting includes transaction, journal, lines, movements, balances, idempotency, audit and outbox.
- [ ] Balance by scope/currency is enforced in domain and database at commit.
- [ ] Idempotency survives concurrency, restart, timeout and event replay.
- [ ] Concurrent debit/hold/control tests prove the available-funds policy cannot be bypassed.
- [ ] Components, currencies, dates, mapping and rule versions are explicit.
- [ ] Reversals/adjustments preserve originals and enforce remaining eligibility under concurrency.
- [ ] Manual journals, backdates and period exceptions require authority, reason, approval and audit.
- [ ] EOD/period jobs restart from checkpoints without duplicate postings and produce balancing evidence.
- [ ] Balance/history/statement queries use authoritative posted records and reconcile.
- [ ] Restore, rebuild and reconciliation evidence demonstrates no duplicate/missing outcome.

## 22. Definition of Done

- Build, analyzers, formatting, architecture tests and all financial suites pass.
- PostgreSQL migrations apply to empty/supported upgrade databases; immutability, balance, uniqueness and precision constraints are verified.
- OpenAPI/internal API/event contracts pass compatibility and financial/security review.
- Finance approves chart, posting matrices, component treatment, precision/rounding, reversal, dates, period and EOD golden results.
- Contention tests prove idempotency and no overspend under approved load.
- Fault injection proves crash-before/after-commit, outbox failure and checkpoint restart safety.
- End-to-end flows prove source request to journal, balance, event, reversal, reconciliation and statement traceability.
- Backup restore, balance rebuild and PITR reconciliation runbooks are demonstrated.
- No direct balance mutation or non-Ledger write path exists in code, database grants or operational procedures.

## 23. Open decisions

| Decision | Impact | Owner | Required by | Status |
| --- | --- | --- | --- | --- |
| ADR-0005 consistency, balances and transaction boundary | Core implementation safety | Architecture/Finance | Slice 1 | Open |
| Chart of accounts and legal-entity balancing scopes | Every journal/account | Finance | Slice 3 | Open |
| Phase 1 accounting event catalogue/mappings | Allowed postings/components | Finance/Domain owners | Slice 3 | Open |
| Monetary precision, rate scale and rounding | Totals and calculations | Finance | Slice 3 | Open |
| Debit/credit sign and balance representation | Storage/query/rebuild | Finance/Architecture | Slice 1 | Open |
| Available-funds, overdraft, hold, lien and pending formula | Concurrent debit outcome | Finance/Risk/Product | Slice 1 | Open |
| PostgreSQL isolation/locking/retry policy | Contention and correctness | Architecture/DBA | Slice 1 | Open |
| External/source reference uniqueness scopes | Deduplication/traceability | Domain owners/Finance | Slice 2 | Open |
| Fee/tax/commission/interest/waiver accounting treatment | Explicit components/mappings | Finance/Tax/Product | Slice 3 | Open |
| Reversal/partial reversal and component policy | Corrections and provider flows | Finance/Operations | Slice 9 | Open |
| Manual journal/customer-account permissions | Privileged financial risk | Finance/Security | Slice 10 | Open |
| Backdate/value-date windows and recalculation impact | Period/accrual correctness | Finance/Compliance | Slice 11 | Open |
| Period close/lock/reopen state and authority | Financial close | Finance | Slice 11 | Open |
| EOD step graph, partitions and completion gates | Restart/order/business date | Finance/Core operations | Slice 12 | Open |
| Statement running-balance/snapshot semantics | US-023 accuracy | Finance/Product | Slice 13 | Open |
| Retention/partitioning/archival | Scale, audit and restore | Compliance/DBA | Before migration approval | Open |
| Load, latency, availability, RTO and RPO | Release/performance/recovery | Product/Platform | Before release testing | Open |

## 24. Prohibited assumptions

- Do not invent a chart of accounts, GL account, suspense fallback or accounting-event mapping.
- Do not use `double`, `float`, PostgreSQL `money` or implicit rounding for money/rates.
- Do not infer currency from institution/account when processing a command.
- Do not let callers submit arbitrary journal lines for normal business events.
- Do not post from a controller, provider callback, report, cache, channel handler or another service database.
- Do not treat workflow/provider/event state as proof that a ledger commit occurred.
- Do not modify/delete a posted journal or repair a balance with direct arithmetic SQL.
- Do not retry a timed-out financial request using a new idempotency key without authoritative status resolution.
- Do not treat holds, liens, blocks, restrictions, pending amounts, ledger balance and available balance as interchangeable.
- Do not allow stale control/configuration state when the approved fail-safe requires freshness.
- Do not reverse an uncertain provider transaction blindly.
- Do not backdate, reopen periods, post manual journals or override controls without explicit policy/approval.
- Do not advance EOD checkpoints before their financial effects commit and reconcile.
- Do not generate statement totals from a cache, report store or analytics table.
- Do not add cross-currency posting until FX accounting decisions are approved.

Until accounting decisions are approved, implement domain/contracts/tests with synthetic mappings only and keep production posting disabled.

## 25. Agent implementation instructions

- Required reading: root `AGENTS.md`, architecture, Phase 0 evidence, prior Phase 1 epics, this epic, LED/ACC/DAT/NFR/SEC/AUD requirements, US-018..US-023, ADR-0005 and approved accounting documents.
- Expected C# boundaries: Ledger/Processing modules inside `CoreBanking.CoreLedger.Domain`, `.Application`, `.Infrastructure`, `.Api`, `.Contracts` and `.Tests` with strict internal boundaries from Account/Product modules.
- Domain code must not reference EF Core, ASP.NET, broker, Workflow or remote clients.
- Use strongly typed IDs, `decimal`, explicit ISO currency, `DateOnly`, `DateTimeOffset`, `TimeProvider`, cancellation tokens and stable generated references.
- Do not honour cancellation after commit in a way that reports a false failed outcome; retrieve/return the durable result.
- Use EF Core migrations owned by Core Ledger and real PostgreSQL/Testcontainers for transactions, locks, constraints, deadlocks, precision and recovery tests.
- Test real concurrency with separate connections; in-memory providers cannot prove financial locking or constraints.
- Add architecture/database tests that prevent non-Ledger code/principals from writing protected tables.
- Use transactional outbox/inbox and dual idempotency layers; never publish before commit.
- Stop if work requires an unapproved account mapping, formula, rounding, balance sign, overdraft, reversal, date, period, EOD, FX or financial recovery decision.
- Completion report must include requirement/story coverage, ADR/policy versions, permissions, APIs/events, migrations, accounting matrices, tests/commands and measured results, fault/recovery evidence, assumptions, risks and rollout notes.

