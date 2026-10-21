---
phase: "Phase 1 - Banking Foundation"
epic: "Products and Accounts"
owning_service: "Core Banking and Ledger"
participating_services:
  - Customer and Onboarding
  - Identity and Access
  - Operations and Workflow
  - Risk and Reporting
  - Audit and Reconciliation
modules:
  - Deposit Product Catalogue
  - Product Versioning and Simulation
  - Eligibility and Pricing Rules
  - Account Opening and Activation
  - Account Lifecycle
  - Restrictions, Freezes, Liens and Blocks
  - Dormancy and Reactivation
  - Statement Request Authorisation
status: Draft
requirements: [ACC-001, ACC-002, ACC-003, ACC-004, ACC-005, ACC-006, ACC-007, ACC-008, ACC-009, ACC-010, LED-013, ARC-006, DAT-001, DAT-004, DAT-005, DAT-007, DAT-008, RPT-002, SEC-009, SEC-010, SEC-011, SEC-013, AUD-001, AUD-002, AUD-003, NFR-OBS-001, NFR-OBS-002, NFR-DR-001, NFR-MNT-001, BR-001, BR-007, BR-008, BR-013, BR-014, BR-015]
stories: [US-016, US-017, US-023]
dependencies:
  - Phase 0 - Messaging, Outbox, Inbox and Idempotency
  - Phase 1 - Institution and Tenancy
  - Phase 1 - Identity, Roles and Permissions
  - Phase 1 - Customer and Onboarding
last_updated: 2026-10-21
---

# Epic: Products and Accounts

## 1. Objective

Deliver versioned deposit-product definitions and a controlled account lifecycle that can open an eligible account for a verified customer, assign a unique institution-compliant account number, enforce restrictions and lifecycle controls, and expose authorised account information without bypassing the protected ledger.

The epic establishes the rules and contracts needed by the later Core Ledger and Financial Posting epic. Account creation and activation never manufacture an opening balance. Every monetary outcome, including initial funding, interest, fees, tax, penalties, liquidation and correction, must be posted by the ledger through a valid balanced transaction.

## 2. Success measures

- Product versions containing eligibility, pricing, fees, interest, tax, limits and lifecycle rules cannot become effective without simulation and required approval.
- Every account is bound permanently to the exact product version used at opening, with controlled migration to later versions when explicitly approved.
- Product, customer status, KYC tier, age/date, segment, currency, branch and limit eligibility are evaluated server-side from authoritative versioned inputs.
- Concurrent retries cannot create duplicate unintended accounts or duplicate account numbers.
- Account activation creates no balance or journal entry. Funding requires a separate idempotent ledger command.
- Debit/credit restrictions, freezes, blocks and liens have typed scope, reason, authority, effective period, status and immutable history.
- Closed accounts reject ordinary postings; only explicitly authorised correction or settlement event types may proceed under ledger policy.
- Ledger balance, available balance, holds, liens and blocks remain distinct in contracts and persistence.
- Product simulation and account-control changes are traceable to requirement, policy version, actor, approval and tests.
- Statement requests validate ownership, permission and period, then use authoritative posted history and a separate document-generation capability.

The launch team must approve product catalogue, formulas, rounding, account-number scheme, limits, dormancy policy, performance targets, RTO and RPO. This document does not invent those decisions.

## 3. Scope

### Included

- Deposit product families and configurable product catalogue.
- Immutable product versions with effective dates and lifecycle.
- Eligibility, pricing, fee, tax, interest, balance, withdrawal, notice and account-lifecycle rule definitions.
- Deterministic product simulation with an input/output trace and no postings.
- Maker-checker submission, approval, scheduled publication and replacement/rollback by new version.
- Customer account application, eligibility evaluation, opening, approval and activation.
- Unique account-number reservation and assignment under concurrency.
- Account status, status history and product-version binding.
- Debit-only, credit-only, all-posting and channel/transaction-type restrictions where approved.
- Account freeze, block and lien metadata plus ledger-enforcement contracts.
- Dormancy evaluation, transition and controlled reactivation.
- Account summaries that keep ledger balance, available balance, holds, liens and blocks distinct.
- Statement/receipt request authorisation and handoff to authoritative ledger/document services.

### Excluded

- Journal posting, balance mutation, hold reservation/release arithmetic, available-balance calculation and reversals, delivered by the Core Ledger and Financial Posting epic.
- Payment orchestration or provider interaction.
- Rendering, watermarking, secure download links and document storage, delivered by Documents and Statements.
- Fixed-deposit contract creation, funding, maturity instructions, rollover and premature liquidation from US-024, scheduled for Phase 2.
- Interest accrual batches, capitalisation postings and end-of-day processing. This epic owns approved rule definitions and pure simulation; ledger processing owns execution and posting.
- Institution-wide configuration lifecycle already owned by Institution and Tenancy. Product configuration uses that governance pattern but remains owned by the Product module.
- Customer KYC/classification decisions, owned by Customer and Onboarding.
- Workflow queues and approver routing, owned by Operations and Workflow.
- Invented accounting mappings, fee/tax rates, formulas, regulatory limits or account-number algorithms.

Phase 1 product schemas may model term-deposit, notice-deposit and target-savings rule types required by ACC-001, but those types remain unavailable for live account opening until their contract lifecycle and monetary flows are delivered and approved.

## 4. Ownership and financial boundaries

| Capability | Authoritative owner | Boundary |
| --- | --- | --- |
| Product identity, versions and rules | Product module in Core Banking and Ledger | Rules are immutable after publication |
| Product approval orchestration | Operations and Workflow | Product applies exact approved version/hash |
| Customer/KYC/segment/risk facts | Customer and Onboarding | Product consumes versioned facts/events/API |
| Account identity, number, product binding and lifecycle | Account module in Core Banking and Ledger | Account changes do not post money |
| Restrictions/freezes/blocks/lien instruction metadata | Account module | Ledger evaluates effective controls before posting |
| Holds, balance movements and available-balance arithmetic | Ledger module | Only ledger posting/reservation changes authoritative values |
| Account statement transaction history | Ledger module | Posted immutable records only |
| Statement document generation and secure links | Documents and Statements capability | Uses authorised ledger snapshot/query contract |
| Roles and entitlements | Identity and Access | Account/Product enforce permission and resource scope |
| Audit evidence | Source module plus Audit and Reconciliation | Source commits evidence/outbox atomically |

Rules:

1. Product simulation is advisory evidence and cannot change configuration, accounts or balances.
2. Account opening does not call a balance-update API and does not insert a journal.
3. Account status and ledger transaction status are different concepts.
4. A restriction instructs ledger policy; it does not directly rewrite a balance.
5. Balance read models and statement documents never become the source of truth.
6. Cross-module calls inside the same deployable service still use explicit application contracts and dependency direction.

## 5. Actors, roles and permissions

Role names are examples only. Code checks permissions plus contextual scope.

| Actor | Permission | Contextual scope | Step-up | Approval |
| --- | --- | --- | --- | --- |
| Product administrator | `product.definition.create` | Institution/product family/currency | Required for sensitive pricing | None until submission |
| Product administrator | `product.version.update` | Institution/draft product version | Required | None until submission |
| Product analyst | `product.version.simulate` | Institution/draft version | Policy-based | None |
| Product approver | `product.version.approve` | Institution/product family | Required | Cannot approve own version |
| Product publisher | `product.version.publish` | Institution/product/effective date | Required | Exact approved version only |
| Operations officer | `account.open` | Institution/branch/product/customer | According to channel/risk | Approval by policy |
| Account approver | `account.open.approve` | Institution/branch/product | Required | Cannot approve own application |
| Operations officer | `account.activate` | Institution/branch/account | Required if manual | Approved application only |
| Authorised operations officer | `account.restriction.apply` | Institution/branch/account/restriction type | Required | High-risk controls require approval |
| Restriction approver | `account.restriction.approve` | Institution/branch/account/type | Required | Cannot approve own control |
| Authorised operations officer | `account.status.change` | Institution/branch/account/transition | Required | Transition-specific approval |
| Lien officer | `account.lien.create` | Institution/account/reason/amount/currency | Required | Approval and legal authority by policy |
| Lien approver | `account.lien.approve` | Institution/account | Required | Cannot approve own lien |
| Customer | `account.self.view` | Own active relationship | Channel authentication | None |
| Staff user | `account.summary.view` | Institution/branch/customer/account | Sensitive access by policy | None |
| Customer/staff | `account.statement.request` | Own or authorised account/period | According to channel | None |
| Ledger service identity | `service.account.controls.evaluate` | Audience/account/transaction context | Service authentication | No human role |

Additional rules:

- `account.summary.view` does not imply permission to view unrestricted transactions, sensitive customer data or internal control reasons.
- Lien, legal freeze, deceased-customer and closure operations require reason/evidence classifications without exposing protected evidence in ordinary responses.
- Product publish, account activation, restriction approval and lien approval enforce maker-checker by stable actor ID across roles and sessions.
- Service identities cannot inherit human roles or use administrative endpoints.

## 6. Domain model

### Aggregates and entities

| Concept | Responsibility | Identity | Important relationships |
| --- | --- | --- | --- |
| `AccountProduct` | Stable product identity/family and lifecycle | `ProductId` | Owns immutable versions |
| `ProductVersion` | Exact rules, status and effective period | `ProductVersionId` | Eligibility/pricing/lifecycle rules and simulation evidence |
| `ProductSimulation` | Reproducible non-posting rule evaluation | `SimulationId` | Exact version/hash and synthetic/approved inputs |
| `AccountApplication` | Requested customer/product/currency/branch and approval state | `AccountApplicationId` | Customer reference and eligibility snapshot |
| `EligibilityEvaluation` | Exact facts, policy versions and outcomes | `EligibilityEvaluationId` | Application, product version and customer fact versions |
| `AccountNumberReservation` | Concurrency-safe number allocation | `ReservationId` | Institution/scheme/sequence/account application |
| `Account` | Banking account identity, product binding and lifecycle | `AccountId` | Customer, branch, currency, product version, ledger account ref |
| `AccountStatusHistory` | Append-only lifecycle transitions | `AccountStatusHistoryId` | Account, reason, approval and effective time |
| `AccountRestriction` | Typed debit/credit/channel/transaction restriction | `RestrictionId` | Account, scope, period, authority and lifecycle |
| `AccountFreeze` | Broad freeze with approved source/reason | `FreezeId` | Account and effective period |
| `AccountBlock` | Policy/legal/operational block metadata | `BlockId` | Account and applicable operations |
| `AccountLien` | Explicit amount/currency claim and priority metadata | `LienId` | Account; ledger consumes for availability enforcement |
| `DormancyAssessment` | Rule evaluation and transition evidence | `DormancyAssessmentId` | Account, rule version and dates |
| `StatementRequest` | Authorised period/format request and downstream reference | `StatementRequestId` | Account, requester and ledger snapshot/query ref |

### Value objects

- `ProductCode`, `ProductFamily`, `ProductStatus` and `VersionNumber`.
- `Money` with decimal amount and explicit ISO currency.
- `Rate`, `RateBand`, `RoundingRule`, `CalculationBasis` and `ScheduleDefinition`.
- `EligibilityCriterion`, `EligibilityFactVersion`, `RuleOutcome` and `SimulationHash`.
- `AccountNumber`, `AccountCurrency`, `AccountPurpose` and `OwnershipType`.
- `RestrictionType`, `PostingDirection`, `ChannelScope`, `TransactionTypeScope` and `EffectivePeriod`.
- `LienAmount`, `LienPriority`, `LegalAuthorityReference` and `ControlReasonCode`.
- `BusinessDate`, `PostingDate`, `ValueDate` and timestamp remain distinct.

### Product invariants

1. Product code is unique within its approved institution scope.
2. Published product versions are immutable and have non-overlapping effective periods unless an explicit coexistence model is approved.
3. Publication requires a successful simulation tied to the exact content hash and required approval.
4. Eligibility, pricing, fee, tax, interest, limit and lifecycle rules declare version, currency/applicability and effective dates.
5. Monetary arithmetic uses `decimal`, explicit precision/scale, currency and approved rounding at defined steps.
6. A rule cannot silently default when a required input, mapping, rate, tax or rounding policy is absent.
7. Rollback publishes a new approved version; it never edits history.
8. Product retirement prevents new openings but does not mutate existing accounts.
9. Existing accounts stay bound to their product version until a separately approved migration changes that binding.

### Account invariants

1. Every account belongs to one institution, customer, currency and exact product version.
2. Customer, product, branch and currency must belong to or be valid for the same institution.
3. Account number is unique within the approved institution/scheme scope and is assigned once.
4. Eligibility is evaluated from authoritative versioned facts immediately before approval/activation as policy requires.
5. Opening or activation never creates a balance, journal line or financial transaction.
6. Ledger balance, available balance, holds, liens and blocks are distinct concepts and fields.
7. Restrictions are additive unless an approved precedence model explicitly states otherwise; a permissive control cannot silently cancel a stronger control.
8. Restriction, freeze, block and lien changes preserve prior state and approval evidence.
9. A lien has explicit amount and currency and cannot be inferred from narrative text.
10. An expired control stops applying only through deterministic effective-date evaluation and a recorded transition/job outcome.
11. Closed accounts reject ordinary posting intents; ledger allows only specifically classified correction/settlement operations under approved policy.
12. Reactivation requires a legal transition, current eligibility/KYC policy as applicable, reason and approval.
13. No account application, status change or restriction can cross tenant boundaries.

### Product version lifecycle

```text
Draft -> Simulated -> PendingApproval -> Approved -> Scheduled -> Published -> Superseded
   |         |              |               |
   +-> Withdrawn            +-> Rejected    +-> RejectedBeforePublication

Published -> RetiredForNewAccounts
```

Any edit after simulation invalidates the simulation hash and returns the version to Draft. Publication applies the exact approved content hash once.

### Account application and account lifecycle

```text
Draft -> EligibilityPending -> Eligible -> PendingApproval -> Approved -> Open -> Active
                      |                         |              |
                      +-> Ineligible            +-> Rejected   +-> CancelledBeforeActivation

Active -> Restricted -> Active
       -> Frozen -> Active
       -> Dormant -> Active
       -> PendingClosure -> Closed
```

- `Open` means the record and number exist; it does not imply monetary funding or channel access.
- Whether approval occurs before number reservation or activation is an approved institution policy, but no rejected application may leak reusable number semantics.
- `Closed` is terminal for ordinary use. Reopening, if permitted at all, requires an explicit new-account or exceptional policy decision.

### Restriction/lien lifecycle

```text
Draft -> PendingApproval -> Active -> Expired
                     |        -> Released
                     |        -> Superseded
                     +-> Rejected
```

Release is a new authorised transition with reason; it does not delete the control. Legal controls may prohibit manual expiry/release.

## 7. Product rules and simulation

Product versions must separate rule definitions rather than store one unvalidated configuration blob:

| Rule group | Required characteristics | Phase 1 execution |
| --- | --- | --- |
| Eligibility | Customer type, KYC tier, segment, risk, age/date, branch, residency and currency references | Evaluate for account opening |
| Balance limits | Minimum/maximum and applicable balance concept | Validate/configure; ledger enforces applicable postings |
| Transaction limits | Direction, channel, period, count/amount and currency | Publish canonical rules; owning transaction service enforces |
| Withdrawal/notice | Notice duration, exceptions and effective rules | Evaluate eligibility/control; no Phase 2 notice contract workflow |
| Fees | Trigger, basis, amount/rate, bands, min/max, currency, waiver eligibility | Pure simulation; ledger posts later |
| Tax | Jurisdiction/tax type, basis, rate, rounding and exemption reference | Pure simulation; never invent tax law |
| Interest | Balance basis, bands, rate type, day-count/accrual/capitalisation schedule and rounding | Pure deterministic calculation/simulation; EOD posts later |
| Lifecycle | Opening, activation, dormancy, reactivation and closure rules | Enforce supported Phase 1 transitions |
| Term/maturity | Tenure, maturity and liquidation schema vocabulary | Configuration validation only; live contract is Phase 2 |
| Accounting mapping reference | Approved event-to-ledger mapping version | Validate reference; ledger owns mapping and posting |

Simulation rules:

- Use synthetic or approved test inputs and explicitly version every input.
- Return calculation steps, rule IDs/versions, rounding points and warnings without posting money.
- Include edge cases at band boundaries, leap days, date conventions, min/max caps and missing inputs.
- Store a stable input/output hash and exact product content hash.
- A simulation is invalid if the product or referenced rule version changes.
- Simulation success means the engine behaved as configured; it does not certify legal, accounting or commercial correctness.

## 8. Core use cases

### ACC-UC-01 - Create, simulate and publish a product version

- Actor: Product administrator, analyst, separate approver and publisher.
- Permissions: `product.definition.create`, `product.version.update`, `product.version.simulate`, `product.version.approve`, `product.version.publish`.
- Flow: Create product/version, define typed rules, validate references, simulate exact hash, submit to Workflow, apply exact approval decision, schedule and publish.
- Concurrency: Product/version optimistic token plus unique effective-period constraints.
- Events: `ProductVersionPublished.v1` after local commit.
- Failure codes: `product_rule_invalid`, `product_simulation_required`, `product_simulation_stale`, `product_self_approval`, `product_effective_period_conflict`.

### ACC-UC-02 - Open and activate an account

- Actor: Operations officer and approver, or an approved channel using equivalent server-side policy.
- Permission: `account.open`, `account.open.approve`, `account.activate`.
- Flow: Resolve customer and current classification, select effective product version, evaluate eligibility, reserve unique account number, submit/approve as configured, create account and activate.
- Idempotency: Institution, operation, initiator/channel and request ID plus payload hash.
- Transaction: Application, eligibility snapshot, reservation/account, history, audit and outbox commit at their explicit boundaries.
- Result: Account ID/number/status/product version. Ledger/available balance is absent or explicitly zero only after an authoritative ledger account/balance initialization contract, never inferred by Account module.

### ACC-UC-03 - Apply or release an account restriction

- Actor: Authorised operations maker and separate approver where required.
- Flow: Select typed control and scope, require reason/evidence ref, validate dates/current state, submit approval, apply exact decision and publish control event.
- Guard: Cannot weaken another active control; legal controls obey release authority; maker cannot approve.
- Ledger effect: Ledger evaluates active controls on later posting requests; no balance mutation occurs here.

### ACC-UC-04 - Create or release a lien

- Actor: Lien officer and approver.
- Flow: Validate account/currency, explicit amount, authority reference, effective period and priority; approve; activate; publish event for ledger availability enforcement.
- Guard: Amount is positive decimal; currency equals account currency unless an approved multi-currency design exists; release retains history.
- Consistency: Ledger acknowledgement/read model lag is visible. High-risk posting policy fails closed until required control version is current.

### ACC-UC-05 - Evaluate dormancy and reactivate

- Actor: Scheduled service for assessment; authorised officer/customer journey for reactivation.
- Flow: Evaluate last qualifying activity against exact rule/business date, propose/approve transition if configured, mark Dormant, and publish event. Reactivation validates current customer/account/KYC/security requirements.
- Guard: A read/query or non-qualifying event cannot reset dormancy; qualifying activity definition is approved, not inferred.

### ACC-UC-06 - Close an account

- Actor: Authorised operations officer and approver.
- Preconditions: Approved closure policy, ledger-provided authoritative zero/settled conditions, no unresolved prohibitive holds/liens/blocks/disputes, and required statement/notice actions.
- Flow: Create closure proposal, capture reason, validate authoritative conditions, approve exact version, transition to Closed and publish event.
- Guard: Account module does not force balances to zero or delete ledger history.

### ACC-UC-07 - Request a statement or receipt

- Actor: Customer or authorised staff.
- Permission: `account.statement.request` plus account ownership/resource scope.
- Flow: Validate account access, allowed period/type and sensitive-delivery policy; create idempotent request; Documents and Statements requests authoritative posted history from Ledger and returns a secure artifact reference.
- Guard: Account summaries, caches or UI transactions cannot be used as the financial source.
- Audit: Request, generation reference, access/download events and delivery context are correlated without logging statement contents.

## 9. API contracts

| Method | Route | Permission | Request | Response | Idempotent |
| --- | --- | --- | --- | --- | --- |
| POST | `/api/v1/products` | `product.definition.create` | `CreateProductRequest` | `ProductResponse` | By request ID |
| POST | `/api/v1/institutions/{institutionId}/products/{productId}/savings-eligibility/versions` | `product.version.update` plus step-up | `CreateSavingsEligibilityDraftRequestV1` plus `Idempotency-Key` | `SavingsEligibilityDraftResponseV1` | By idempotency key |
| PATCH | `/api/v1/product-versions/{id}` | `product.version.update` | Explicit fields plus expected version | `ProductVersionResponse` | By concurrency version |
| POST | `/api/v1/institutions/{institutionId}/product-versions/{versionId}/simulations` | `product.version.simulate` | `SimulateSavingsEligibilityRequestV1` plus `Idempotency-Key` | `SavingsEligibilitySimulationResponseV1` | By input/hash and idempotency key |
| POST | `/api/v1/institutions/{institutionId}/product-versions/{versionId}/submit` | `product.version.update` plus step-up | `SubmitSavingsEligibilityRequestV1` plus `Idempotency-Key` | `SavingsEligibilityApprovalSubmissionResponseV1` | By idempotency key |
| POST | `/api/v1/institutions/{institutionId}/product-versions/{versionId}/publish` | `product.version.publish` plus step-up | `PublishSavingsEligibilityRequestV1` plus `Idempotency-Key` | `SavingsEligibilityPublicationResponseV1` | By idempotency key |
| GET | `/api/v1/products` | `product.catalogue.view` | Bounded filters/effective date | `ProductPage` | Yes |
| POST | `/api/v1/account-applications` | `account.open` | `CreateAccountApplicationRequest` | `AccountApplicationResponse` | By request ID |
| POST | `/api/v1/account-applications/{id}/submit` | `account.open` | Expected version/reason | `AccountApplicationResponse` | Yes |
| POST | `/api/v1/accounts/{id}/activate` | `account.activate` | Approved application/version | `AccountResponse` | Yes |
| GET | `/api/v1/accounts/{id}` | Self or `account.summary.view` | Requested field set | `AccountSummaryResponse` | Yes |
| POST | `/api/v1/accounts/{id}/restrictions` | `account.restriction.apply` | `CreateRestrictionRequest` | `RestrictionResponse` | By request ID |
| POST | `/api/v1/accounts/{id}/liens` | `account.lien.create` | `CreateLienRequest` | `LienResponse` | By request ID |
| POST | `/api/v1/accounts/{id}/status-proposals` | `account.status.change` | `AccountStatusProposalRequest` | `StatusProposalResponse` | By request ID |
| POST | `/api/v1/accounts/{id}/statement-requests` | `account.statement.request` | `StatementRequestContract` | `StatementRequestResponse` | By request ID |
| POST | `/internal/v1/product-account-changes/{id}/approval-decisions` | Workflow service permission | Exact decision/version/hash | Change-specific response | By decision ID |
| POST | `/internal/v1/accounts/{id}/controls/evaluate` | Ledger service permission | Posting context and known control version | `AccountControlDecision` | Yes |
| GET | `/internal/v1/accounts/{id}/posting-context` | Ledger service permission | Institution/account | `AccountPostingContext` | Yes |

Contract rules:

- Product rules use typed versioned contracts; do not expose arbitrary executable code or unrestricted expressions.
- Customer, institution, branch, approval, ownership, account status and calculation results supplied by clients are untrusted.
- Monetary contracts use decimal JSON representation policy plus explicit ISO currency; no binary floating point.
- Summary responses label balance source/version/as-of data supplied by Ledger. Account module does not compute it.
- Sensitive control reasons/evidence are masked unless a specific permission and purpose are present.
- Expected failures use RFC 7807 with stable codes and no cross-tenant resource disclosure.
- Collections use bounded pagination and allow-listed filters. Examples use synthetic data.

## 10. Event contracts

### Produced events

| Event | Trigger | Key fields | Ordering key | Consumers |
| --- | --- | --- | --- | --- |
| `ProductVersionPublished.v1` | Approved version becomes effective | Product/version IDs, institution, family, currency, effective period, rule refs/hash | Product ID | Account, Ledger, Payments, Channels, Audit |
| `ProductRetiredForNewAccounts.v1` | Product stops accepting openings | Product/version, institution, effective time | Product ID | Account, Channels, Audit |
| `AccountOpened.v1` | Account and number commit | Account ID, masked/account reference, customer, product version, branch, currency, status | Account ID | Customer, Ledger, Channels, Audit |
| `AccountActivated.v1` | Account becomes Active | Account ID, institution, customer, product version | Account ID | Ledger, Payments, Channels, Audit |
| `AccountStatusChanged.v1` | Lifecycle status changes | Account ID, old/new status, reason category, control version | Account ID | Ledger, Payments, Customer, Channels, Audit |
| `AccountRestrictionChanged.v1` | Restriction activates/releases/expires | Account/restriction IDs, type/scope/status/effective period, version | Account ID | Ledger, Payments, Channels, Audit |
| `AccountLienChanged.v1` | Lien activates/releases | Account/lien IDs, amount/currency, status, priority, version | Account ID | Ledger, Audit |
| `AccountDormancyChanged.v1` | Dormant/reactivated transition | Account ID, status, rule version, business date | Account ID | Ledger, Customer, Channels, Audit |
| `StatementRequested.v1` | Authorised request commits | Request/account IDs, period, format, requester type, ledger query ref | Account ID | Documents/Reporting, Audit |

### Consumed events

| Event | Producer | Consumer behaviour | Deduplication key | Failure handling |
| --- | --- | --- | --- | --- |
| `InstitutionConfigurationPublished.v1` | Institution module | Refresh supported currencies, product/account-number policy refs | Event ID/version | Block affected publication/opening until current |
| `BranchStatusChanged.v1` | Institution module | Update valid account-opening branch reference | Event ID | Fail closed for inactive/stale branch |
| `CustomerActivated.v1` | Customer and Onboarding | Maintain minimal customer eligibility reference | Event ID/version | Opening remains pending until current |
| `CustomerStatusChanged.v1` | Customer and Onboarding | Re-evaluate account/control actions per approved policy | Event ID/version | No automatic financial mutation |
| `CustomerClassificationChanged.v1` | Customer and Onboarding | Refresh KYC/segment/risk eligibility facts | Event ID/version | New opening/migration uses current facts |
| `ApprovalDecided.v1` | Operations and Workflow | Apply exact pending product/account/control version once | Decision ID | Remain pending on mismatch/failure |
| `LedgerAccountInitialised.v1` | Ledger module | Link protected ledger account and authoritative balance context | Event ID | Account remains non-postable until reconciled |
| `AccountBalanceSnapshotUpdated.v1` | Ledger module | Optional non-authoritative display projection only | Event ID/version | Mark stale; never use to authorise posting/closure |
| `StatementDocumentReady.v1` | Documents and Statements | Link secure artifact status/reference to request | Event ID | Retry; request remains processing/failed explicitly |

Events carry standard identity, institution, correlation, causation, schema version and occurred time. They exclude full account numbers when a protected reference/masked form is sufficient, customer PII, document content, secrets and unrestricted product internals.

Outbox state commits with source aggregate changes. Consumers use inbox deduplication and monotonic versions. Replay cannot republish a product, create a second account/number, reactivate a control or duplicate a statement artifact.

## 11. Database design

### Ownership

- Database: `core_ledger_db`.
- Schemas in this epic: `product`, `account`, `integration`.
- Migration assembly: Core Banking and Ledger Infrastructure project.
- Runtime principal: Access limited by module schema/table privileges where practical; no other service gets direct database access.
- Ledger tables are not modified by Account commands except through an approved Ledger application use case/local contract.

### Product tables

| Table | Purpose | Key constraints | Tenant-owned | Retention |
| --- | --- | --- | --- | --- |
| `product.account_products` | Stable product identity/family | Institution/product code unique | Yes | Permanent history |
| `product.product_versions` | Immutable version/status/effective dates/hash | Product/version unique; publication/effective constraints | Yes | Permanent |
| `product.eligibility_rules` | Typed eligibility criteria | Version/rule code unique | Yes | With version |
| `product.pricing_rules` | Shared pricing metadata/basis | Version/rule code unique | Yes | With version |
| `product.interest_rules` | Rate bands/accrual/capitalisation definitions | Typed schema and currency/applicability | Yes | With version |
| `product.fee_rules` | Fee triggers/bands/min/max/waiver refs | Typed schema | Yes | With version |
| `product.tax_rules` | Tax basis/rate/exemption refs | Typed schema and jurisdiction ref | Yes | With version |
| `product.limit_rules` | Balance/transaction/withdrawal limits | Amount/currency/period validation | Yes | With version |
| `product.lifecycle_rules` | Opening/dormancy/reactivation/closure rules | Typed transitions/version | Yes | With version |
| `product.notice_maturity_rules` | Extensible notice/term vocabulary | Cannot enable unsupported live flow | Yes | With version |
| `product.accounting_mapping_refs` | Approved Ledger mapping version refs | External reference; no cross-schema entity sharing | Yes | With version |
| `product.simulations` | Exact input/output/version hashes and result | Unique request/input hash scope | Yes | Evidence policy |
| `product.version_status_history` | Append-only lifecycle/approval refs | Version/sequence unique | Yes | Permanent evidence |

### Account tables

| Table | Purpose | Key constraints | Tenant-owned | Retention |
| --- | --- | --- | --- | --- |
| `account.account_applications` | Opening request/current state | Institution/request idempotency; concurrency | Yes | Policy/permanent evidence |
| `account.eligibility_evaluations` | Immutable facts/rules/outcomes | Application/evaluation version unique | Yes | Permanent evidence |
| `account.number_sequences` | Scheme-specific next allocation state | Institution/scheme unique; concurrency/locking | Yes | Permanent/current |
| `account.number_reservations` | Reserved/assigned/voided number evidence | Institution/scheme/account number unique | Yes | Permanent |
| `account.accounts` | Account identity, customer, product version and status | Institution/account number unique; stable ID | Yes | Permanent/legal hold |
| `account.account_status_history` | Append-only status transitions | Account/sequence unique | Yes | Permanent |
| `account.restrictions` | Typed effective account controls | Account/type/version/status constraints | Yes | Permanent history |
| `account.restriction_status_history` | Append-only control transitions | Restriction/sequence unique | Yes | Permanent |
| `account.holds` | Ledger-owned reservation metadata/reference | Ledger commands only; amount/currency/status | Yes | Financial policy |
| `account.liens` | Approved lien amount/authority/status | Positive amount; currency; concurrency | Yes | Legal/financial policy |
| `account.blocks` | Broad control metadata | Typed scope/reason/status | Yes | Permanent history |
| `account.dormancy_history` | Assessment and transition evidence | Account/business-date/rule version | Yes | Permanent |
| `account.statement_requests` | Authorised request and artifact state | Institution/request key unique | Yes | Document/audit policy |
| `integration.idempotency_records` | Stable replay outcome | Institution/operation/key unique; request hash | Yes | Approved horizon |
| `integration.outbox_messages` | Durable event publication | Event ID unique | Yes | Replay horizon |
| `integration.inbox_messages` | Consumer deduplication | Consumer/event ID unique | Yes | Replay horizon |

`account.holds` is listed for schema completeness but is owned and mutated only by Ledger use cases. Account-lifecycle handlers must not write it.

### Constraints and indexes

- Tenant-owned unique constraints begin with `institution_id` unless deliberately platform-wide.
- PostgreSQL numeric precision/scale is explicit for every amount/rate; currency is required for monetary values.
- Check constraints enforce positive lien/limit amounts, legal status values, chronological periods and required reason/authority fields.
- Partial unique indexes enforce one applicable active product version where policy requires it and one assigned account number.
- Account-number allocation uses an atomic sequence/allocation strategy scoped by institution/scheme; application-level `max + 1` is prohibited.
- Concurrency tokens protect product drafts, publication, applications, accounts and controls.
- Product published rows and account/control history are append-only under runtime privileges.
- Customer, branch, workflow, document and other service IDs are external references without cross-database foreign keys.

### Migration and backfill

- Seed only approved product/account reference types, never assumed products, prices, rates, taxes, account numbers or real customer data.
- Existing product migration preserves original effective versions and marks unverifiable rules for review; it does not label them approved automatically.
- Existing accounts require reconciled customer, product, account number, currency, lifecycle and ledger links before cutover.
- Migration totals include accounts by institution/product/status/currency plus duplicate-number and orphan checks.
- Released migrations are append-only. Financial/account history is never deleted or rewritten during ordinary migration.
- Destructive/long-running changes require expand-migrate-contract rollout, verification and forward-fix plan.

## 12. Consistency, concurrency and idempotency

- Product simulation and approval reference the exact canonical content hash. Any mutation invalidates prior simulation/approval.
- Publication uses optimistic concurrency and database effective-period constraints within one local transaction.
- Account opening rechecks mutable eligibility facts at the policy-defined final gate; a stale initial evaluation cannot silently authorise activation.
- Account-number reservation is atomic. Retries with the same scoped idempotency key return the original reservation/account outcome.
- A conflicting payload under the same key is rejected and audited.
- Product/account/control state, history, audit evidence, idempotency outcome and outbox event commit atomically within the owning module transaction.
- Simultaneous restriction/release operations compare expected versions and recompute effective controls before commit.
- Ledger maintains the authoritative control version it evaluated. Version gaps or stale high-risk controls fail closed according to approved posting policy.
- Cross-service/workflow convergence uses outbox/inbox and reconciliation, not distributed transactions.
- Cancellation after an authoritative account/control commit must not cause the API to report a false failure; the original outcome remains queryable by idempotency key.

## 13. Security and privacy

- Resolve institution from trusted context and enforce it in application queries and persistence filters.
- Validate customer ownership/status/classification through trusted contracts/read models and versions.
- Account number is sensitive financial data: mask it by default and never use full values in logs, metrics, traces or broad events.
- Do not accept frontend calculations for eligibility, interest, fees, tax, limits, available balance or closure balance.
- Explicitly map contracts to prevent mass assignment of status, approval, product version, branch, control authority or ledger reference.
- Sensitive control/legal reasons and evidence refs require distinct permission and purpose.
- Product rule input must use an allow-listed typed DSL/schema; arbitrary code, SQL or unbounded expression execution is prohibited.
- Statement requests and downloads require fresh authorisation at the appropriate stages; possession of an artifact reference alone is insufficient.
- Apply rate limits to product simulation, account opening, account search, statement requests and sensitive control APIs.
- Use synthetic account/customer data in tests. Production data cannot enter non-production without approved masking.

Negative tests cover cross-tenant IDs, account-number enumeration, wrong customer/branch scope, stale entitlements, maker self-approval, mass assignment, calculation tampering, duplicate requests and unauthorised statement periods.

## 14. Audit and observability

Audit product creation/edit/simulation/submission/approval/publication/retirement; account application/eligibility/number assignment/opening/activation; restriction/freeze/block/lien proposal/approval/release/expiry; dormancy/reactivation/closure; sensitive summary access; statement request and exceptional overrides.

Evidence includes actor/service ID, role/service context, permission, institution, branch, product/account protected ref, customer ref, reason, exact rule/product/fact versions, content hash, simulation/approval ref, before/after status, business/effective dates, correlation and timestamp. Do not place full account numbers, statement content or protected legal evidence in audit summaries.

Minimum metrics:

- Product versions by state, simulation failures/staleness, approval age and scheduled publication failure.
- Account applications by state/age/branch/product and safe ineligibility reason.
- Account-number reservation conflict/void rate and allocation latency.
- Accounts by lifecycle state, product and currency.
- Restriction/freeze/block/lien counts, approval age, expiry/release backlog and ledger control-version lag.
- Dormancy assessment/transition/reactivation counts and overdue jobs.
- Statement requests by state/age and downstream generation failures.
- Cross-tenant/permission denials, concurrency conflicts, idempotent replays, outbox backlog and audit failure.

Traces connect customer eligibility, Workflow approval, account creation, ledger initialization/control evaluation and statement generation using correlation/causation IDs. Logs use stable event names and exclude monetary/customer payload dumps.

Alerts/runbooks cover publication failure, number-sequence exhaustion/collision, stuck account approvals, control propagation lag, dormant job failure, statement backlog, event gaps, database constraint failure and restore inconsistency.

## 15. Failure, retry and recovery

| Failure | Safe state | Recovery |
| --- | --- | --- |
| Product referenced config missing/stale | Draft/simulation/publication blocked | Refresh exact version; re-simulate/reapprove |
| Customer facts unavailable | Application remains EligibilityPending | Retry/resync; no assumed eligibility |
| Workflow unavailable | Change remains PendingApproval | Resume idempotently |
| Account-number allocation conflict | No duplicate account created | Atomic retry within bounded policy; preserve request ID |
| Ledger initialization unavailable | Account remains non-postable/Open as designed | Retry event/command and reconcile |
| Control event delayed/gapped | Ledger fails closed for affected high-risk posting | Resync control snapshot/version |
| Concurrent restriction/release | One valid transition wins | Return conflict/current state; re-evaluate |
| Statement dependency unavailable | Request remains Pending/Failed explicitly | Safe retry using original request ID |
| Process crash after commit | Original result recoverable | Idempotency/outbox worker resumes |
| Database restore | Traffic held until product/account/ledger/control reconciliation | Verify versions, links, sequences and replay |

Backups are encrypted and restore exercises verify product hashes/effective versions, account-number uniqueness/sequence continuity, account status/history, active controls/liens, ledger links, statement requests, idempotency and outbox/inbox continuity.

Manual recovery uses authorised commands, never direct balance updates or ordinary ad hoc SQL edits. Sequence repair and account/control correction require reconciliation evidence and approval.

## 16. Configuration

| Setting | Owner | Secret | Validation | Reload behaviour |
| --- | --- | --- | --- | --- |
| Product families and live-enabled types | Product governance | No | Supported capabilities and phase gate | Versioned publication |
| Eligibility rules | Product/Compliance | No | Approved fact refs/operators | Product version |
| Fee/interest/tax/limit rules | Product/Finance/Compliance | No | Currency, precision, rounding, dates, legal approval | Product version |
| Account-number scheme | Institution/Core operations | Sensitive config, not secret | Uniqueness, length, check digit, exhaustion | New reservations only |
| Account opening/approval policy | Operations/Risk | No | Channel/product/amount/customer contexts | Versioned/effective-dated |
| Restriction/lien authority policy | Legal/Operations/Risk | Evidence refs protected | Types, approvals, expiry/release authority | Versioned/effective-dated |
| Dormancy/reactivation rules | Product/Compliance | No | Qualifying activity, dates, approvals | Versioned/effective-dated |
| Statement period/format policy | Product/Compliance | No | Retention, access and document support | New requests |
| Simulation datasets | Product/QA | May contain protected refs; synthetic default | Approved cases and expected boundaries | Versioned |
| Cache/control freshness | Core/Ledger/Security | No | Fail-safe and maximum lag | Controlled rollout |

Financial behaviour is versioned, simulated, approved, effective-dated and audited. Missing configuration blocks the affected operation.

## 17. Implementation slices

Implementation progress as of 2026-10-21: the first configuration-area engine is implemented for `product_eligibility.v1`. It uses typed explicit criteria for customer type, KYC tier, segment, risk rating, age range, branch, residency and currency. Simulation requires versioned synthetic inputs, returns a deterministic per-rule trace plus stable content/input/output hashes, compares every scenario with its expected outcome and unlocks submission only when the exact content hash passes. Persisted rule content is parsed back through a strict allow-listed schema; unknown fields and executable-style extensions are rejected. ADR-0015 approves Savings as the first live Phase 1 family and requires final account approval to re-fetch and re-evaluate current authoritative customer, branch and currency facts; expired facts fail closed. Institution-defined product codes are stable within each institution. Published versions use non-overlapping half-open effective periods and exact as-of selection; publication is serialized with the configuration-set optimistic-concurrency token. Tenant-scoped product identities, configuration sets, versions, simulations and publication evidence have EF Core mappings and migrations. Authenticated, institution-scoped APIs now expose idempotent draft creation, exact-hash simulation and Workflow submission with separate stable capability permissions; their contracts cannot assign trusted institution, actor, approval or status fields. Submission accepts only the exact successfully simulated content and commits `ApprovalRequested.v1` through the transactional outbox. Core Ledger consumes `ApprovalDecided.v1` idempotently and applies it only to the exact submitted request, version number and content hash; approval moves the version to `approved`, while rejection returns it to `draft` without erasing the decision evidence retained by Workflow and the inbox. The publication endpoint requires `product.version.publish`, step-up authentication and an idempotency key. It binds to the stored Workflow approval and exact content hash, revalidates the typed stored rules, rejects maker self-publication and overlapping half-open periods, serializes concurrent schedules through the configuration-set token, and atomically records publication evidence plus `ProductVersionPublished.v1` in the outbox. Non-development access remains fail-closed pending the production Identity adapter. The Savings configuration journey is now callable end to end in Development. An internal, tenant-scoped eligibility boundary now resolves exactly one effective published Savings version at a requested instant, revalidates its stored hash and typed rules, evaluates authoritative versioned customer/branch/currency facts, and returns exact source-version evidence; missing, ambiguous, stale or corrupt configuration fails closed. It is deliberately not exposed as a public endpoint until the authoritative customer, branch and currency transport contracts in slice 6 are approved, and account opening remains inaccessible.

1. Approve product taxonomy, account-number, rounding/calculation, restriction/lien, dormancy and statement ownership decisions.
2. Define versioned Product, Account, Control and Statement contracts plus module architecture tests.
3. Implement product identity/version lifecycle and typed rule persistence with migration tests.
4. Implement deterministic rule evaluation and simulation with boundary/golden tests and content hashing.
5. Integrate product approval/publication with Workflow using exact version/hash and outbox events.
6. Build customer/branch/classification reference consumers and freshness handling.
7. Implement account application and eligibility snapshots with negative scope tests.
8. Implement atomic account-number reservation, account creation and retry/concurrency tests.
9. Implement approval/activation and ledger-initialization handoff, proving no opening balance/posting.
10. Implement typed restrictions, freezes and blocks plus ledger control-evaluation contract.
11. Implement lien lifecycle and ledger availability-control handoff without Account balance arithmetic.
12. Implement dormancy assessment/reactivation and controlled closure prerequisites.
13. Implement account summaries and statement-request authorisation/handoff.
14. Add audit, observability, reconciliation, alerts and recovery runbooks.
15. Complete security, performance, backup/restore and Phase 1 end-to-end evidence.

Each slice starts with traceable failing tests and leaves the service buildable. Unsupported product types and incomplete control paths remain unavailable.

## 18. Test plan

| Test ID | Requirement/story | Level | Scenario | Expected evidence |
| --- | --- | --- | --- | --- |
| ACC-001 | ACC-001 | Domain | Create supported product families | Typed family/capability validation |
| ACC-002 | ACC-002, ACC-010 | Integration | Simulate, approve and publish exact version | Immutable effective version and audit/outbox |
| ACC-003 | ACC-010 | Security | Maker approves own version through another role | Denied by stable actor ID |
| ACC-004 | ACC-002 | Concurrency | Publish overlapping versions concurrently | Constraint prevents ambiguous effective version |
| ACC-005 | ACC-002 / BR-013 | Integration | Roll back product behaviour | New approved version; history unchanged |
| ACC-006 | ACC-007 | Unit | Interest bands/day-count/rounding boundary cases | Deterministic trace from exact rule version |
| ACC-007 | ACC-002, ACC-006 | Unit | Fee/tax/limit missing required input | Explicit failure; no permissive/default calculation |
| ACC-008 | ACC-008 | Architecture | Attempt live Phase 2 term-deposit contract | Capability unavailable despite schema vocabulary |
| ACC-009 | ACC-003 / US-016 | Integration | Open eligible verified customer account | Exact product/fact versions and unique number |
| ACC-010 | ACC-003 / US-016 | Security | Ineligible KYC/age/segment/limit | Denied with safe reason; no account |
| ACC-011 | ACC-003 / US-016 | Security | Cross-tenant customer/product/branch references | Denied without resource disclosure |
| ACC-012 | ACC-004 / US-016 | Concurrency | Many concurrent number reservations | No duplicates; sequence/reservations reconcile |
| ACC-013 | ACC-004 / US-016 | Idempotency | Repeat identical account-open request | Original account/number returned once |
| ACC-014 | ACC-004 | Idempotency | Reuse key with changed payload | Conflict and audit; no second account |
| ACC-015 | US-016 / BR-001 | Integration | Activate new account | No journal, posting or fabricated opening balance |
| ACC-016 | ACC-003 | Resilience | Ledger initialization delayed | Explicit non-postable state until reconciled |
| ACC-017 | ACC-003 / US-017 | Integration | Apply debit-only restriction | Versioned control enforced by ledger contract |
| ACC-018 | US-017 / BR-008 | Security | Restriction maker self-approves | Denied and audited |
| ACC-019 | US-017 | Concurrency | Apply/release controls concurrently | Legal combined state; no lost stronger control |
| ACC-020 | ACC-005 / US-017 | Domain | Activate lien | Explicit positive amount/currency/authority/history |
| ACC-021 | ACC-005 | Integration | Ledger sees stale control version | Posting fails safely/resyncs per approved policy |
| ACC-022 | ACC-009 | Domain | Qualifying dormancy threshold reached | Exact business date/rule version transition |
| ACC-023 | ACC-009 | Security | Reactivate without required checks/approval | Denied; status remains Dormant |
| ACC-024 | ACC-003 / US-017 | Integration | Close unsettled/restricted account | Denied from authoritative prerequisites |
| ACC-025 | US-017 | Contract | Ordinary posting to closed account | Ledger control decision denies; exception types explicit |
| ACC-026 | ACC-005 | Contract | Return account summary | Balances/holds/liens/blocks distinct with source/as-of |
| ACC-027 | LED-013 / US-023 | Security | Request statement without ownership/scope | Denied without account disclosure |
| ACC-028 | LED-013 / US-023 | Integration | Request authorised statement period | Ledger-backed request and document handoff/audit |
| ACC-029 | LED-013 / BR-015 | Architecture | Attempt statement from account cache | Test/architecture guard rejects non-authoritative source |
| ACC-030 | SEC-011 | Security | Mass-assign status/version/approval/ledger ref | Rejected/ignored; no state elevation |
| ACC-031 | SEC-013 | Security | Inspect logs/events/account APIs | Full number and protected reasons masked/absent |
| ACC-032 | NFR-OBS-001 | End-to-end | Trace customer eligibility through account activation | Correlated redacted evidence across services |
| ACC-033 | NFR-DR-001 | Recovery | Restore and replay database/events | No duplicate number/account/control/publication |
| ACC-034 | ACC-001..ACC-010 | End-to-end | Publish product, open, restrict and query account | All versions, approvals and invariants evidenced |

Performance tests use approved product/account volumes and contention profiles. Calculation golden tests require business-approved expected results, not agent-generated expectations.

## 19. Acceptance criteria

- [ ] ACC-001 through ACC-010 and US-016, US-017 and US-023 trace to contracts, code and tests with split ownership documented.
- [ ] Product versions are typed, immutable after publication, effective-dated, simulated and approved.
- [ ] Rules use decimal, currency, approved precision/rounding and reproducible calculation traces.
- [ ] Phase 2 fixed-deposit flows cannot be enabled accidentally.
- [ ] Eligibility uses authoritative versioned customer, product, institution and branch facts.
- [ ] Account-number allocation is unique, idempotent and concurrency safe.
- [ ] Account activation creates no balance or financial posting.
- [ ] Restrictions/liens preserve history and reach Ledger through versioned fail-safe contracts.
- [ ] Dormancy, reactivation and closure use approved rules and authoritative prerequisites.
- [ ] Account summaries distinguish balance concepts and identify the Ledger source/as-of version.
- [ ] Statement requests use authoritative posted history and secure document handoff.
- [ ] Tenant, permission, maker-checker, masking, audit, observability and recovery tests pass.

## 20. Definition of Done

- Build, analyzers, formatting, architecture tests and affected test suites pass.
- EF Core migrations apply to empty and supported upgrade databases; number/control/product constraints are verified with PostgreSQL integration tests.
- OpenAPI and event schemas pass compatibility, financial-boundary and data-minimisation review.
- One approved product version is simulated/published with business-approved golden results using synthetic data.
- One eligible account opens/activates end to end without a posting; one ineligible/cross-tenant flow is denied.
- Ledger integration proves current controls are enforced and stale/gapped control state fails safely.
- Statement request proves authoritative Ledger query and secure document handoff.
- Account number, product publication, control propagation and restore/replay runbooks are demonstrated.
- Traceability includes decisions, requirements, stories, contracts, migrations, code, tests and operational evidence.

## 21. Open decisions

| Decision | Impact | Owner | Required by | Status |
| --- | --- | --- | --- | --- |
| Phase 1 live product families | Scope and rule engines | Product/Architecture | Slice 1 | Savings approved by ADR-0015; all other families disabled |
| Product/version code and coexistence rules | Institution-defined code; half-open non-overlapping periods; exact as-of selection | Product | Slice 3 | Approved by ADR-0015 |
| Decimal precision, rate scale and rounding points | Financial correctness | Finance/Product | Slice 4 | Open |
| Interest/day-count/accrual/capitalisation rules | Simulation and later EOD | Finance/Product | Slice 4 | Open |
| Fee/tax definitions and legal approval | Pricing correctness | Finance/Tax/Compliance | Slice 4 | Open |
| Account-number format, scope, check digit and exhaustion | Allocation and migration | Institution/Core operations | Slice 8 | Open |
| Multiple-account-per-product/customer rules | Duplicate opening behaviour | Product/Risk | Slice 7 | Open |
| Eligibility freshness/final recheck point | Race and customer-state risk | Product/Compliance | Slice 7 | Recheck current authoritative facts at final approval per ADR-0015 |
| Account approval/activation sequence | Number exposure and workflow | Operations/Product | Slice 9 | Open |
| Restriction/freeze/block precedence | Ledger allow/deny outcome | Legal/Risk/Core | Slice 10 | Open |
| Lien priority, partial use and release authority | Available balance/legal control | Legal/Finance/Core | Slice 11 | Open |
| Dormancy qualifying activity and business-date policy | Status correctness | Compliance/Product | Slice 12 | Open |
| Closure prerequisites and permitted post-close events | Financial/legal safety | Finance/Operations | Slice 12 | Open |
| Existing-account product migration policy | Customer terms and version binding | Product/Legal | Before migration feature |
| Statement periods, formats, retention and delivery | US-023 and document integration | Product/Compliance | Slice 13 | Open |
| Capacity, latency, RTO and RPO targets | Test/release gates | Product/Platform | Before release testing | Open |

## 22. Prohibited assumptions

- Do not invent product types, rates, tax, fees, limits, rounding, day-count, dormancy or account-number rules.
- Do not use `double`/`float` for monetary or rate calculations.
- Do not store published product behaviour as an unvalidated arbitrary JSON/expression blob.
- Do not change a published version or silently migrate existing accounts.
- Do not trust frontend eligibility, fee, interest, tax, balance, ownership, status or approval values.
- Do not open an account from stale customer facts when policy requires current verification.
- Do not implement account numbers using `max + 1`, random retry without constraints or an unapproved universal format.
- Do not post an opening balance during account creation/activation.
- Do not let Account handlers write journals, balance rows or holds directly.
- Do not treat restriction, block, freeze, hold and lien as synonyms.
- Do not close an account based on a cached/display balance.
- Do not build statements from account summaries, analytics or frontend transactions.
- Do not enable fixed-deposit contracts/maturity flows merely because schemas contain term fields.

While decisions remain open, keep affected product types/actions unavailable and use synthetic configurations for engineering tests.

## 23. Agent implementation instructions

- Required reading: root `AGENTS.md`, architecture, Phase 0 evidence, prior Phase 1 epics, this epic, ACC/LED/DAT/SEC/AUD requirements, US-016/US-017/US-023 and approved decisions.
- Expected C# boundaries: Product and Account modules inside `CoreBanking.CoreLedger.Domain`, `.Application`, `.Infrastructure`, `.Api`, `.Contracts` and `.Tests`, with explicit internal module contracts.
- Domain code does not reference EF Core, ASP.NET, Workflow clients, messaging or provider SDKs.
- Keep Product/Account handlers out of Ledger persistence. Invoke protected Ledger application contracts only.
- Use strongly typed IDs, `decimal`, explicit currency/precision, `DateOnly` for business/effective dates, `TimeProvider`, cancellation tokens and optimistic concurrency.
- Use EF Core migrations owned by Core Banking and Ledger and Testcontainers for PostgreSQL constraints, account-number contention and migration tests.
- Start every slice with traceable failing tests, including cross-tenant, wrong-scope, concurrency, idempotency, self-approval and no-posting tests.
- Publish only committed facts through outbox and consume through inbox with version-gap detection.
- Stop if implementation would invent a product formula, financial configuration, number scheme, control precedence, legal authority, dormancy rule, closure exception or authoritative balance shortcut.
- Completion report must list coverage, decisions, permissions, API/events, migrations, calculations/golden tests, commands run, financial/security evidence, assumptions, risks and rollout notes.

