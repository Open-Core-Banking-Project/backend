---
phase: "Phase 1 - Banking Foundation"
epic: "Payments, Collections and VTU"
owning_service: "Payments and VTU"
participating_services:
  - Core Banking and Ledger
  - Identity and Access
  - Operations and Workflow
  - Integration Hub
  - Risk and Reporting
  - Channels and Engagement
  - Audit and Reconciliation
modules:
  - Payment Instructions
  - Internal Transfers
  - Interbank Transfers
  - Provider Collections
  - Beneficiaries and Limits
  - Provider Routing
  - Scheduled Instructions
  - VTU Catalogue and Purchases
  - Bill Payments
  - Refunds, Recalls and Reversals
status: Draft
requirements: [PAY-001, PAY-002, PAY-003, PAY-004, PAY-005, PAY-006, PAY-007, PAY-008, PAY-009, PAY-010, PAY-011, PAY-012, PAY-013, PAY-014, PAY-015, VTU-001, VTU-002, VTU-003, VTU-004, VTU-005, VTU-006, VTU-007, VTU-008, VTU-009, VTU-010, LED-004, LED-005, API-003, API-005, API-006, NIB-001, NIB-002, NIB-004, NIB-005, PSP-001, PSP-003, PSP-004, PSP-006, DAT-001, DAT-004, NFR-RES-001, NFR-RES-002, NFR-OBS-001, NFR-OBS-002, NFR-DR-001, SEC-009, SEC-010, SEC-011, SEC-012, SEC-013, AUD-001, AUD-002, AUD-004, BR-001, BR-005, BR-006, BR-007, BR-011, BR-012, BR-013]
stories: [US-025, US-026, US-027, US-028, US-029, US-032, US-033]
dependencies:
  - Phase 0 - Messaging, Outbox, Inbox and Idempotency
  - Phase 1 - Identity, Roles and Permissions
  - Phase 1 - Products and Accounts
  - Phase 1 - Core Ledger and Financial Posting
  - Phase 1 - Workflow and Maker-Checker
last_updated: 2026-10-15
---

# Epic: Payments, Collections and VTU

## 1. Objective

Deliver idempotent payment orchestration for internal transfers, NIBSS NIP/interbank transfers, configured provider collections, beneficiaries, payment limits, airtime/data and bill payments while preserving one customer debit and one intended financial outcome.

Payments and VTU owns payment instructions, orchestration state, beneficiaries, limit reservations, route decisions, provider-attempt references, collection matching references, VTU catalogue/purchase state and refund/recall/reversal requests. It does not own provider protocols/credentials or customer balances.

Only Core Banking and Ledger changes balances. Integration Hub executes provider-specific protocols and validates external webhooks. A provider callback, redirect, timeout, notification or workflow decision never proves a final financial outcome by itself.

## 2. Success measures

- Each payment has a stable instruction ID, bank reference, scoped idempotency key and trace to every provider and ledger attempt.
- Internal transfers post debit and credit atomically through one approved Ledger transaction.
- NIP name enquiry succeeds and is bound to the exact transfer confirmation before submission.
- A provider timeout becomes `Uncertain`; status enquiry resolves the original attempt before retry, failover, reversal or resubmission.
- Failover cannot create a second customer debit or a second provider submission for the same unresolved attempt.
- Provider redirects and callbacks are never treated as final without verified server-side status where required.
- Beneficiary creation, verification, cooling-off, removal and limits enforce current customer, channel, account, KYC and risk policy.
- Fees, commissions, taxes, provider costs, charges and margins are calculated before execution from approved versions and posted explicitly.
- VTU/bill products and parameters are canonical, versioned and mapped to provider products without leaking provider models into domain code.
- Provider token/voucher results, delivery state, wallet/availability state and references remain distinct from ledger/payment state.
- Daily reconciliation can match payment, provider and ledger outcomes and keeps unresolved breaks visible.

Provider specifications, certification, financial sequence, routing weights, fee/tax policy, limits, reconciliation tolerances and performance/recovery targets require approval before production enablement.

## 3. Scope

### Included

- Internal transfers between eligible accounts within one institution.
- NIBSS NIP name enquiry, transfer submission, status enquiry and controlled reversal/recall request orchestration through Integration Hub.
- Server-side Paystack/Flutterwave-style collection initialisation and verified outcome processing through configured adapters.
- Payment instruction lifecycle, attempts, status history and financial trace.
- Beneficiary creation, validation, verification status, cooling-off and removal.
- Per-transaction/daily limit evaluation and durable limit reservation/consumption model.
- Versioned route policy using approved service, amount, destination, cost, availability, limit and performance inputs.
- Scheduled-transfer and standing-order foundations required by PAY-001, enabled only for approved Phase 1 journeys.
- Refund, recall, reversal and payment-investigation request foundations.
- Canonical VTU/service catalogue for airtime, data and approved bill categories.
- Customer/meter/account parameter validation, provider attempt, token/voucher and delivery tracking.
- Provider-wallet/availability projections and operational alerts.
- VTU commissions, margins, charges and settlement references.
- Receipt/notification request events and daily reconciliation feeds.

### Excluded

- Provider SDKs, credentials, certificates, endpoints, signatures and webhook ingress, owned by Integration Hub.
- Ledger journals, balances, holds and reversals, owned by Core Banking and Ledger.
- Workflow tasks/approval orchestration and investigations, owned by Operations and Workflow.
- Fraud/AML models and risk scores, owned by Risk and Reporting.
- Notification template/delivery and receipt artifact rendering, owned by Channels/Documents capabilities.
- Settlement-file ingestion and reconciliation case management, owned by Audit and Reconciliation.
- Live bulk/payroll/corporate payment execution from PAY-011/US-030, scheduled for Phase 3.
- Live static/dynamic virtual-account issuance from PAY-010/US-031, scheduled for Phase 3. Phase 1 provider collections use approved provider references without exposing the later virtual-account product.
- Unapproved bill categories, scheduled journeys, provider failover, refund/recall or partial reversal behaviour.
- Cross-currency transfers until FX pricing, accounting and settlement policy is approved.

Schemas may reserve explicit future structures for bulk items and virtual accounts, but those endpoints/workers remain unavailable until their phase epics are approved.

## 4. Ownership and financial boundaries

| Capability | Authoritative owner | Payments responsibility |
| --- | --- | --- |
| Payment instruction/business state | Payments and VTU | Authoritative orchestration status/history |
| Beneficiaries and payment-limit reservations | Payments and VTU | Authoritative within approved policy |
| Customer/account status and restrictions | Core Banking/Customer | Consume versioned facts; revalidate with Ledger |
| Balance, holds, debit/credit and reversal | Core Banking and Ledger | Send idempotent canonical financial commands |
| Provider protocol, credentials, signing, webhook verification | Integration Hub | Send/consume canonical attempt contracts |
| Provider route health/capability | Integration Hub | Consume versioned health/capability projection |
| Financial route policy and selection | Payments and VTU | Record exact policy/input/decision before attempt |
| Risk decision | Risk and Reporting | Bind exact assessment/reference to instruction |
| Human approval/investigation | Operations and Workflow | Start exact subject/hash request and consume decision |
| Reconciliation/settlement breaks | Audit and Reconciliation | Publish trace/status records and accept controlled actions |
| Notification/receipt delivery | Channels/Documents | Request only after authoritative state transition |

Hard rules:

1. Payment state, provider-attempt state and ledger state are separate state machines with explicit correlation.
2. `Successful` requires the approved combination of provider/internal outcome and ledger outcome for that payment type.
3. A frontend amount, fee, beneficiary name, account ownership, provider choice or final status is untrusted.
4. No service other than Ledger changes customer balances.
5. No provider-specific status or request model enters the Payments domain.
6. A notification/receipt confirms what the authoritative sources report; it never sets payment status.

## 5. Actors, roles and permissions

| Actor | Permission | Contextual scope | Step-up | Approval |
| --- | --- | --- | --- | --- |
| Customer | `payment.transfer.initiate` | Own account/channel/beneficiary/amount/currency/limit | By risk/amount/device | By policy |
| Operations officer | `payment.transfer.initiate_on_behalf` | Institution/branch/customer/account | Required | By policy |
| Approver | `payment.transfer.approve` | Institution/account/amount/product/mandate | Required | Cannot approve own instruction |
| Customer | `payment.beneficiary.manage` | Own customer/account/channel | New/high-risk beneficiary step-up | Cooling-off/policy |
| Customer/merchant | `payment.collection.initiate` | Institution/service/amount/currency | Channel policy | None/policy |
| Customer | `payment.vtu.purchase` | Own account/channel/product/amount/limit | By risk | By policy |
| Payment operations | `payment.investigation.view` | Institution/provider/payment/status | Required for sensitive detail | Read-only/action-specific |
| Payment operations | `payment.status.requery` | Institution/payment/provider attempt | Required | No new attempt |
| Payment operations | `payment.reversal.request` | Institution/payment/ledger/provider refs | Required | Maker-checker |
| Routing administrator | `payment.routing_policy.create` | Institution/service/environment | Required | Approval/publication |
| Routing approver | `payment.routing_policy.approve` | Institution/service/environment | Required | Cannot approve own version |
| VTU catalogue administrator | `vtu.catalogue.manage` | Institution/service/product | Required | Publication approval |
| Payments service identity | `service.ledger.payment.post` | Ledger event/institution/amount/currency | Service authentication | Exact orchestration state |
| Integration service identity | `service.payment.provider_attempt.update` | Institution/provider/attempt/service | Service authentication | Canonical result only |

Rules:

- Channel possession, phone number, WhatsApp identity or USSD session does not establish sufficient financial authority.
- Limits and step-up are enforced server-side using current entitlements and risk context.
- Service identities cannot use customer/staff permissions.
- Requery permission does not grant resubmit, reverse or override permission.
- Routing administration cannot execute a customer payment.

## 6. Domain model

### Aggregates and entities

| Concept | Responsibility | Identity | Important relationships |
| --- | --- | --- | --- |
| `PaymentInstruction` | Stable payment intent, state and trace root | `PaymentInstructionId` | Attempts, ledger refs, approval/risk refs |
| `TransferRequest` | Source/destination/amount/confirmation data | `TransferRequestId` | Instruction and beneficiary/name enquiry |
| `PaymentAttempt` | One submission to a route/provider | `PaymentAttemptId` | Route decision and canonical provider attempt |
| `ProviderStatusEnquiry` | Resolution of an existing attempt | `StatusEnquiryId` | Attempt, external ref and result |
| `RoutePolicy` | Stable financial routing policy identity | `RoutePolicyId` | Immutable effective versions |
| `RouteDecision` | Exact eligible candidates/inputs/chosen route | `RouteDecisionId` | Instruction/attempt and policy version |
| `Beneficiary` | Customer-owned destination and lifecycle | `BeneficiaryId` | Verification and cooling-off history |
| `LimitReservation` | Durable allocation against a limit window | `LimitReservationId` | Instruction, limit version and outcome |
| `NameEnquiry` | Bound destination-name verification | `NameEnquiryId` | Destination, provider ref, expiry and confirmation hash |
| `CollectionInstruction` | Server-created collection intent | `CollectionInstructionId` | Provider init, callback/status and ledger credit |
| `CollectionReceipt` | Verified collection matching evidence | `CollectionReceiptId` | Instruction/provider/ledger/reconciliation refs |
| `ScheduledInstruction` | Schedule and deterministic occurrences | `ScheduledInstructionId` | Payment template and occurrence IDs |
| `ServiceProduct` | Canonical VTU/bill product/version | `ServiceProductId` | Provider product mappings |
| `VtuPurchaseInstruction` | Airtime/data/bill purchase state | `VtuInstructionId` | Validation, attempts, ledger and delivery refs |
| `ProviderResult` | Canonical attempt/status/token metadata | `ProviderResultId` | Protected raw evidence reference in Integration Hub |
| `RefundRecallReversalRequest` | Controlled after-sale financial action | `ActionRequestId` | Original instruction/attempt/ledger refs |

### Value objects

- `Money` with decimal amount and explicit ISO currency.
- `PaymentType`, `PaymentStatus`, `AttemptStatus`, `LedgerOutcomeStatus` and `ProviderOutcomeStatus`.
- `BankReference`, `ProviderReference`, `SessionReference`, `ExternalReference`, `CorrelationId` and `IdempotencyKey`.
- `DestinationBankCode`, `DestinationAccountReference`, `MaskedAccountNumber` and `BeneficiaryNameSnapshot`.
- `FeeComponent`, `CommissionComponent`, `TaxComponent`, `ProviderCostComponent` and `MarginComponent`.
- `LimitWindow`, `LimitVersion`, `RiskDecisionReference`, `ApprovalReference` and `RoutePolicyVersion`.
- `ServiceCategory`, `ServiceCode`, `ProductCode`, `CustomerParameter`, `MeterAccountReference` and protected `TokenVoucher`.

### Invariants

1. Every payment belongs to one trusted institution and has one stable bank reference.
2. Every financial initiation has a scoped idempotency key and canonical request hash.
3. Same key/scope/hash returns the original instruction; a different hash is rejected and audited.
4. One payment instruction has at most one active unresolved provider submission at a time unless an approved provider protocol proves otherwise.
5. `Uncertain` blocks failover/resubmission/reversal until status enquiry or authorised investigation resolves the attempt.
6. A new route cannot be submitted until the previous attempt is proven not processed/terminal and failover policy permits it.
7. One instruction produces at most the approved single customer debit and intended beneficiary/settlement credit outcome.
8. Internal transfer debit and credit post in one atomic Ledger transaction.
9. Provider redirect/callback does not create final success without verified canonical backend status and ledger outcome.
10. Name enquiry/beneficiary verification is bound to destination details, institution, provider/session, expiry and transfer confirmation.
11. Fees, commissions, taxes and provider costs are explicit components calculated from approved versions before execution.
12. Limit reservation is atomic and idempotent; concurrent instructions cannot exceed the approved window.
13. Payment status transitions are monotonic except explicit linked dispute/reversal paths; provider events cannot regress a terminal state silently.
14. Ledger/provider/reconciliation references remain traceable and unique in their defined scopes.
15. VTU token/voucher data is encrypted, masked and disclosed only to the authorised customer/channel.
16. Provider catalogue/wallet/health data is time-stamped and cannot by itself prove transaction success.
17. Receipt/notification is created only from authoritative payment state and cannot change it.
18. Replay cannot create a new payment, provider attempt, ledger command, delivery or reconciliation record.

### Payment instruction lifecycle

```text
Created -> Validating -> PendingConfirmation -> PendingApproval -> Ready
   |            |                |                    |
   |            +-> Rejected     +-> Expired          +-> Rejected
   v
Processing -> Successful -> Disputed -> Reversed
          -> Failed
          -> Uncertain -> StatusEnquiry -> Processing/Successful/Failed/Investigation
          -> Investigation
```

`Successful` rules differ by internal transfer, outbound transfer, collection and VTU and must be explicitly configured/tested. A terminal provider status without required ledger outcome is a reconciliation exception, not a reason to fabricate success.

### Provider attempt lifecycle

```text
Planned -> Submitted -> Acknowledged -> Successful
                    |              -> Failed
                    |              -> Uncertain -> Enquiring -> Successful/Failed/Unresolved
                    +-> FailedBeforeSubmission
```

Only `FailedBeforeSubmission` or an authoritative terminal-not-processed outcome can be eligible for safe resubmission/failover under policy.

### Beneficiary lifecycle

```text
Draft -> PendingVerification -> CoolingOff -> Active -> Suspended -> Active
                              |          -> Removed
                              +-> Rejected
```

Changing destination identity/details creates a new revision and may restart verification/cooling-off.

### VTU/bill lifecycle

```text
Created -> ValidatingCustomer -> Ready -> Processing -> Successful -> Delivered
                         |                 |          -> DeliveryPending
                         +-> Rejected      -> Failed
                                           -> Uncertain -> StatusEnquiry -> ...
```

Financial success and token/service delivery remain separate. A successful debit with missing delivery creates investigation/reconciliation work.

## 7. Financial sequence models

### Internal transfer baseline

```text
validate identity, ownership, accounts, controls, limits, risk and fees
  -> obtain approval/confirmation if required
  -> reserve limit outcome
  -> send one idempotent canonical internal-transfer event to Ledger
  -> Ledger atomically posts debit, credit and explicit components
  -> record exact Ledger result
  -> mark Successful only after Posted
  -> request notification/receipt and reconciliation trace
```

### External transfer baseline

The institution must approve debit-first, hold-then-capture, prefund or another financial sequence. Regardless of choice:

- One logical instruction and stable idempotency identities cover all attempts.
- Ledger reservation/debit and provider submission are separately durable and correlated.
- An ambiguous submission never triggers a second debit or route.
- Provider terminal outcome and Ledger completion/reversal converge through explicit states and reconciliation.
- Compensation is a controlled Ledger posting, never an in-place balance edit.

### Provider collection baseline

```text
backend validates service/amount/settlement configuration
  -> creates collection instruction and server-side provider initialisation
  -> frontend receives only approved public/redirect data
  -> Integration Hub verifies callback/webhook and replay identity
  -> Payments performs server-side status verification where required
  -> verified collection is matched idempotently
  -> Ledger posts explicit customer/settlement/components once
  -> reconciliation and notification/receipt are requested
```

## 8. Routing and failover

Route policy versions may consider only approved inputs:

- Institution/environment and enabled service/capability.
- Payment/service type, amount/currency and destination/bank/category.
- Provider limits and certification state.
- Current health, availability and wallet state with timestamp/freshness.
- Approved success-rate/latency windows and cost.
- Customer/product/channel restrictions and risk policy.

Every decision stores the policy version, candidate set, excluded-safe reasons, input freshness and selected route. Route scoring must be deterministic for the same snapshot or record approved randomisation semantics.

Failover rules:

1. `Uncertain` is not eligible for failover.
2. Query the original attempt using its original provider/session/reference.
3. Submit to another route only after authoritative not-processed/failed-before-submit evidence and policy approval.
4. Reuse the same payment instruction while generating a new attempt ID; never create another customer debit.
5. Route health/cost cannot override financial safety, certification, tenant enablement or provider limits.

## 9. Beneficiaries, limits and confirmation

- Beneficiary destination values are encrypted/tokenised as required and masked in ordinary reads.
- Verification/name-enquiry result records destination, canonical name snapshot, provider/session reference, expiry and evidence reference.
- Confirmation binds the user to the exact source account, destination, displayed name, amount, currency, fees and expiry using a server-generated confirmation reference/hash.
- Any bound value change invalidates confirmation and may require a new name enquiry.
- Cooling-off duration and allowed transaction types/limits are versioned policy.
- Limit evaluation considers approved KYC tier, channel, account/product, beneficiary state, device/risk, per-transaction and time-window rules.
- A durable limit reservation is created before execution and consumed/released based on explicit instruction outcomes. Timeout does not automatically release an amount that may still settle.
- Limit increases are versioned requests with step-up and approval where configured.

## 10. VTU and bill catalogue

The canonical catalogue separates institution-facing services from provider products:

- Service category and code.
- Product code/version, display metadata and allowed parameters.
- Pricing/charge/commission/margin/settlement rule references.
- Customer/meter/account validation requirements and response schema.
- Provider mapping versions and enabled environments.
- Availability/wallet requirements and freshness.
- Delivery type: direct, token/voucher, asynchronous fulfilment or status-only.
- Receipt and masking rules.

Provider catalogue sync is staged: ingest canonical proposal, validate/map differences, approve/publish and retain history. A provider change cannot silently change customer pricing or required parameters.

Token/voucher values are restricted secrets. Store encrypted, disclose once/retrievably only by approved policy, never log/event them, and retain a masked/hash form for support/reconciliation.

## 11. Core use cases

### PAY-UC-01 - Perform an internal transfer

- Actor: Customer/authorised staff.
- Flow: Validate/confirm, reserve limits, obtain Workflow approval if required, submit one Ledger command and record result.
- Success: Ledger Posted reference is present; notification is a later side effect.
- Failure: No provider attempt; Ledger rejection maps to safe payment failure and releases eligible limit reservation.

### PAY-UC-02 - Perform an NIP transfer

- Actor: Customer/authorised staff.
- Flow: Name enquiry, exact confirmation, validations/approval, approved Ledger sequence, route decision, canonical Integration Hub attempt, status resolution, Ledger completion/compensation and reconciliation.
- Timeout: `Uncertain` plus status enquiry using original session/reference.
- Guard: No alternate routing or reversal until original outcome eligibility is established.

### PAY-UC-03 - Process a provider collection

- Actor: Customer/merchant through approved channel.
- Flow: Server initialises exact amount/service, returns public provider data, consumes verified canonical callback, verifies status, matches reference, posts once and reconciles.
- Guard: Redirect/browser result, unsigned/unverified event or replay cannot credit an account.

### PAY-UC-04 - Create/use a beneficiary

- Actor: Customer.
- Flow: Capture protected destination, verify/name-enquire, step-up, apply cooling-off, activate and use under current limits.
- Guard: Beneficiary ownership and destination revision are server-controlled; removal does not erase payment history.

### PAY-UC-05 - Purchase airtime/data or pay a bill

- Actor: Customer.
- Flow: Resolve canonical product, validate customer/meter/account parameters, price exact request, reserve/charge through Ledger policy, select provider, submit, resolve uncertainty, record token/delivery, reconcile and request receipt/notification.
- Guard: Provider wallet/availability check is fresh enough under policy; missing delivery remains explicit even when financial status is final.

### PAY-UC-06 - Requery an uncertain attempt

- Actor: Automated worker or authorised operations user.
- Flow: Create idempotent status-enquiry record for original attempt, Integration Hub queries provider, apply monotonic canonical status, then continue original instruction or open investigation.
- Guard: Requery does not create a new transaction at provider or Ledger.

### PAY-UC-07 - Request refund, recall or reversal

- Actor: Customer/operations according to type and policy.
- Flow: Create action request linked to original instruction/attempt/ledger transaction, validate provider and financial eligibility, obtain approval, execute owning-service commands and track each outcome.
- Guard: “Refund”, “recall” and “reversal” are distinct actions; original records remain unchanged.

### PAY-UC-08 - Execute a scheduled occurrence

- Actor: Durable scheduler/service.
- Flow: Materialise deterministic occurrence ID/idempotency key, revalidate current authorisation/account/beneficiary/limits/product, then run the normal payment flow.
- Guard: Schedule creation approval does not guarantee future execution; each occurrence has an independent result.

## 12. API contracts

| Method | Route | Permission | Request | Response | Idempotent |
| --- | --- | --- | --- | --- | --- |
| POST | `/api/v1/payments/internal-transfers` | `payment.transfer.initiate` | `CreateInternalTransferRequest` | `PaymentInstructionResponse` | Required |
| POST | `/api/v1/payments/name-enquiries` | `payment.beneficiary.verify` | Protected destination contract | `NameEnquiryResponse` | Required |
| POST | `/api/v1/payments/interbank-transfers` | `payment.transfer.initiate` | Exact confirmation ref and request | `PaymentInstructionResponse` | Required |
| POST | `/api/v1/collections` | `payment.collection.initiate` | Server-validated service/amount contract | `CollectionInitialisationResponse` | Required |
| GET | `/api/v1/payments/{id}` | Self or `payment.instruction.view` | Trusted resource scope | `PaymentInstructionResponse` | Yes |
| POST | `/api/v1/beneficiaries` | `payment.beneficiary.manage` | `CreateBeneficiaryRequest` | `BeneficiaryResponse` | By request ID |
| DELETE | `/api/v1/beneficiaries/{id}` | `payment.beneficiary.manage` | Expected version | `BeneficiaryResponse` | Yes |
| GET | `/api/v1/vtu/catalogue` | Authenticated channel | Category/provider-independent filters | `ServiceProductPage` | Yes |
| POST | `/api/v1/vtu/purchases` | `payment.vtu.purchase` | `CreateVtuPurchaseRequest` | `VtuPurchaseResponse` | Required |
| POST | `/api/v1/bill-payments` | `payment.vtu.purchase` | `CreateBillPaymentRequest` | `VtuPurchaseResponse` | Required |
| POST | `/api/v1/payments/{id}/action-requests` | Action-specific permission | Typed refund/recall/reversal request | `PaymentActionResponse` | By request ID |
| POST | `/api/v1/payment-attempts/{id}/status-enquiries` | `payment.status.requery` | Reason/expected attempt version | `StatusEnquiryResponse` | By request ID |
| POST | `/api/v1/payment-routing-policies` | `payment.routing_policy.create` | Typed versioned policy | `RoutePolicyResponse` | By request ID |
| POST | `/internal/v1/provider-attempt-results` | Integration service permission | Canonical result/evidence/version | `PaymentInstructionResponse` | By provider event ID |
| POST | `/internal/v1/verified-collection-events` | Integration service permission | Verified canonical event/status ref | `CollectionReceiptResponse` | By provider event ID |
| POST | `/internal/v1/payment-approval-results` | Workflow service permission | Exact decision/subject hash | `PaymentInstructionResponse` | By decision ID |

Contract rules:

- Financial initiation requires idempotency/correlation headers; keys and full account/provider references do not appear in URLs or logs.
- Amount/currency are explicit decimals/ISO codes. Server recalculates pricing/components and returns exact confirmation values.
- Responses expose stable state and next action; asynchronous/provider operations do not pretend to be final.
- Destination/customer/provider token data is masked. Raw token/voucher content uses a separate protected disclosure contract.
- Client cannot submit provider route, fee, tax, account ownership, risk/approval state or final status.
- RFC 7807 stable errors do not enable beneficiary/account/payment enumeration.
- Collections and histories use cursor pagination and bounded dates/filters.

## 13. Event contracts

### Produced events

| Event | Trigger | Key fields | Ordering key | Consumers |
| --- | --- | --- | --- | --- |
| `PaymentInstructionCreated.v1` | Instruction commits | Payment/bank refs, type, institution, amount/currency, safe parties | Payment ID | Workflow, Risk, Audit |
| `PaymentReadyForProvider.v1` | Validations/financial preconditions complete | Payment/attempt/route refs, canonical service, amount/currency | Payment ID | Integration Hub |
| `PaymentStatusChanged.v1` | Monotonic business state change | Payment ID, old/new status, ledger/provider refs, safe reason | Payment ID | Workflow, Channels, Reconciliation, Reporting |
| `PaymentOutcomeConfirmed.v1` | Outcome eligible for Ledger command | Payment/event/amount/components/mapping refs | Payment ID | Ledger |
| `PaymentActionRequested.v1` | Refund/recall/reversal request approved | Action/payment/provider/ledger refs and type | Payment ID | Integration Hub/Ledger as applicable |
| `BeneficiaryChanged.v1` | Beneficiary lifecycle changes | Beneficiary/customer refs, status/version, masked destination | Customer ID | Channels, Risk, Audit |
| `CollectionVerified.v1` | Provider collection verified/matched | Collection/provider/payment refs, amount/currency, match status | Collection ID | Ledger, Reconciliation |
| `VtuPurchaseStatusChanged.v1` | Financial/provider/delivery status changes | Instruction/product/provider/ledger refs, statuses | VTU instruction ID | Channels, Reconciliation, Audit |
| `VtuCataloguePublished.v1` | Canonical catalogue version effective | Catalogue/version/institution/categories/hash | Institution ID | Channels, Integration, Audit |
| `ProviderStatusEnquiryRequested.v1` | Uncertain attempt requires query | Attempt/provider/external refs, original route | Attempt ID | Integration Hub |

### Consumed events

| Event | Producer | Consumer behaviour | Deduplication key | Failure handling |
| --- | --- | --- | --- | --- |
| `AccountActivated/Status/RestrictionChanged.v1` | Core Accounts | Maintain minimal eligibility/control version | Event ID/version | Financial initiation fails closed on gap |
| `CustomerClassificationChanged.v1` | Customer | Refresh KYC/risk-tier limit facts | Event ID/version | New initiation blocked if stale |
| `ApprovalDecided.v1` | Workflow | Apply exact payment/action version/hash once | Decision ID | Remain pending on mismatch |
| `FinancialTransactionPosted/Reversed.v1` | Ledger | Bind authoritative financial outcome to instruction/action | Event ID | Reconcile gaps; never infer from provider alone |
| `HoldChanged.v1` | Ledger | Update reservation/capture reference state | Event ID/version | Instruction stays explicit/pending |
| `ProviderAttemptChanged.v1` | Integration Hub | Apply monotonic canonical attempt status | Provider event ID/version | Gap/status enquiry/manual review |
| `ProviderHealthChanged.v1` | Integration Hub | Refresh route eligibility input with timestamp | Event ID/version | Stale health handled by policy |
| `ProviderCatalogueSnapshotChanged.v1` | Integration Hub | Stage provider-to-canonical mapping proposal | Event ID/version | No automatic customer catalogue change |
| `RiskDecisionChanged.v1` | Risk and Reporting | Bind exact risk decision/version | Decision ID | Instruction pending/rejected per policy |
| `NotificationDeliveryChanged.v1` | Channels | Track side-effect delivery only | Event ID | Does not change payment outcome |

Events exclude full account numbers, customer PII, credentials, signatures, raw provider payloads and token/voucher values. Source state and outbox commit atomically; consumers use inbox deduplication and version-gap handling.

## 14. Database design

### Ownership

- Database: `payments_vtu_db`.
- Schemas: `payment`, `beneficiary`, `collection`, `vtu`, `provider`, `integration`.
- Migration assembly: Payments and VTU Infrastructure project.
- Provider raw evidence/credentials: Integration Hub ownership; Payments stores canonical protected references only.
- No direct access to Ledger, Customer, Identity, Workflow or Integration databases.

### Payment and beneficiary tables

| Table | Purpose | Key constraints | Tenant-owned | Retention |
| --- | --- | --- | --- | --- |
| `payment.payment_instructions` | Stable instruction/current state | Institution/bank ref unique; idempotency scope | Yes | Financial/legal policy |
| `payment.payment_status_history` | Append-only transitions | Payment/sequence unique | Yes | Permanent evidence |
| `payment.transfer_requests` | Protected source/destination/confirmation snapshot | Payment unique; encrypted/tokenised fields | Yes | Financial/privacy policy |
| `payment.payment_components` | Principal/fee/tax/commission/provider cost/margin | Payment/type/sequence unique; amount/currency | Yes | Financial policy |
| `payment.payment_attempts` | Attempt/route/current canonical status | Payment/attempt number unique; one unresolved rule | Yes | Financial policy |
| `payment.attempt_status_history` | Append-only provider attempt transitions | Attempt/sequence unique | Yes | Financial policy |
| `payment.limit_reservations` | Durable limit allocation/outcome | Instruction/limit version unique | Yes | Window plus audit |
| `payment.scheduled_payments` | Schedule/template/current state | Institution/schedule ref unique | Yes | Policy |
| `payment.scheduled_occurrences` | Deterministic execution instances | Schedule/due time/occurrence unique | Yes | Financial policy |
| `payment.refund_requests` | Linked refund lifecycle | Request/original payment unique per policy | Yes | Permanent evidence |
| `payment.recall_requests` | Provider recall lifecycle | Request/original attempt unique per policy | Yes | Permanent evidence |
| `payment.reversal_requests` | Controlled Ledger reversal request | Request/original transaction unique per policy | Yes | Permanent evidence |
| `payment.payment_investigations` | Workflow case/reference projection | Payment/case ref unique | Yes | Operational/audit |
| `beneficiary.beneficiaries` | Customer-owned protected destination | Customer/destination token/version constraints | Yes | Privacy/financial policy |
| `beneficiary.beneficiary_verifications` | Name/validation result and expiry | Beneficiary/revision/provider ref unique | Yes | Evidence policy |
| `beneficiary.cooling_periods` | Cooling-off lifecycle | Beneficiary/revision unique | Yes | Evidence policy |

### Collection, VTU and provider tables

| Table | Purpose | Key constraints | Tenant-owned | Retention |
| --- | --- | --- | --- | --- |
| `collection.collection_instructions` | Server-side collection intent | Institution/reference/idempotency unique | Yes | Financial policy |
| `collection.collection_references` | Provider/internal matching refs | Provider/environment/ref unique | Yes | Financial policy |
| `collection.collection_receipts` | Verified outcome/match/ledger trace | Provider event/match unique | Yes | Permanent evidence |
| `collection.matching_results` | Match rule/version/outcome | Receipt/rule version unique | Yes | Evidence policy |
| `collection.virtual_accounts` | Future Phase 3 structure only | Disabled without approved feature | Yes | Not active in Phase 1 |
| `vtu.service_catalogue` | Canonical catalogue/version | Institution/catalogue version unique | Yes | Historical |
| `vtu.products` | Canonical service product/version | Catalogue/service/product code unique | Yes | Historical |
| `vtu.provider_product_mappings` | Provider mapping proposal/effective version | Product/provider/environment/version unique | Yes | Historical |
| `vtu.purchase_instructions` | Airtime/data/bill instruction/state | Institution/bank ref/idempotency unique | Yes | Financial policy |
| `vtu.customer_validations` | Meter/account validation result/expiry | Instruction/type/reference unique | Yes | Evidence policy |
| `vtu.provider_results` | Canonical result/token metadata/evidence ref | Provider event/ref unique | Yes | Financial policy |
| `vtu.delivery_results` | Service/token delivery state | Instruction/delivery attempt unique | Yes | Financial policy |
| `provider.route_policies` | Payments-owned financial route policy | Institution/service/version unique | Yes | Permanent history |
| `provider.route_decisions` | Exact candidate/input/chosen route snapshot | Payment/attempt unique | Yes | Financial evidence |
| `provider.provider_routes` | Minimal versioned Integration route reference | Provider/service/environment/version | Yes | Reference history |
| `provider.provider_attempts` | Canonical external attempt references | Provider/external ref scope unique | Yes | Financial policy |
| `provider.provider_status_enquiries` | Original-attempt status queries | Attempt/request key unique | Yes | Financial policy |
| `provider.provider_wallet_snapshots` | Non-authoritative wallet/availability projection | Provider/service/time/version | Yes | Operational bounded |
| `integration.idempotency_records` | Stable request hash/outcome | Institution/source/operation/key unique | Yes | Approved horizon |
| `integration.outbox_messages` | Durable events/commands | Event ID unique | Yes | Replay horizon |
| `integration.inbox_messages` | Consumer deduplication | Consumer/event ID unique | Yes | Replay horizon |

### Constraints and indexes

- Tenant-owned uniqueness starts with `institution_id` unless provider/network references require a documented broader scope.
- Every amount uses explicit PostgreSQL numeric precision/scale and non-null ISO currency.
- Unique partial constraints enforce no more than one unresolved submitted provider attempt per instruction under the baseline model.
- Status transitions use concurrency versions and append-only history.
- Protected destination/meter/account values use approved encryption/tokenisation; plaintext is not indexed or logged.
- Foreign IDs are references, not cross-database foreign keys.
- Bulk/virtual-account tables/endpoints/workers are absent or deployment-disabled until their later phase, not accidentally active.

### Migration and backfill

- Seed only synthetic catalogue/provider routes in development. No production credentials or assumed provider products.
- Payment migration reconciles every imported instruction to provider and ledger refs/status or marks it explicitly unresolved.
- Never mark imported records Successful merely because a legacy status says so without approved evidence mapping.
- Released migrations are append-only; financial histories and idempotency outcomes are not rewritten.

## 15. Consistency, concurrency and idempotency

- Payment creation, components, limit reservation, initial history, idempotency outcome, audit and outbox commit locally in one transaction where the use case permits.
- Every provider and Ledger command has a stable child idempotency key derived from the payment instruction and operation, never a newly generated retry key.
- Concurrent duplicate requests contend on database uniqueness and return the original instruction/outcome.
- Provider event application compares attempt/version and legal transition inside one transaction.
- Terminal state conflicts become investigation/reconciliation; “last event wins” is prohibited.
- Limit reservations use row/range locking or an approved atomic counter model in PostgreSQL and are reconciled to final payment states.
- Scheduled workers claim occurrences through database coordination; occurrence ID is deterministic across restart.
- Cross-service sagas expose durable intermediate states and compensating commands. No distributed transaction is used.
- Cancellation after local or remote financial commit cannot be surfaced as a clean failure; callers query the stable instruction.

## 16. Security and privacy

- Revalidate authentication, entitlement freshness, source-account ownership, beneficiary, channel, device/risk, limits and confirmation server-side.
- Step-up/approval evidence is purpose-bound to exact payment hash, amount, currency, source and destination.
- Never expose provider credentials, webhook secrets, signatures, private keys, unrestricted raw payloads or full account/meter/token data.
- Integration Hub verifies webhook signature, source, timestamp, event ID and replay before Payments consumes the event.
- Provider callbacks/redirects are untrusted until canonical backend verification.
- Explicit contract mapping prevents mass assignment of provider, route, fee, status, approval, ledger reference or reconciliation state.
- Token/voucher disclosure is authorised, encrypted and audited; it is never included in general events/logs/notifications.
- Beneficiary/payment searches are bounded and resist enumeration.
- Rate limiting and velocity controls cannot replace durable idempotency or financial limits.
- Non-production uses synthetic payment/customer/provider data and credentials.

Threat tests include account/beneficiary substitution, fee/amount tampering, replay, forged callback/result, stale confirmation, route manipulation, cross-tenant references, concurrent limit use, timeout/failover race, token leakage and confused-deputy service calls.

## 17. Audit and observability

Audit initiation/confirmation/approval, route selection, limit reservation, provider attempt/status enquiry, callback application, Ledger command/result, status transition, beneficiary lifecycle, catalogue publication, VTU validation/delivery, refund/recall/reversal request, manual operation and sensitive token access.

Evidence includes actor/service, permission/audience, institution/channel, payment/bank/source/provider/ledger refs, amount/currency and explicit components, policy/risk/approval/route/catalogue versions, old/new status, reason, idempotency scope, correlation/causation and timestamp. Do not copy secrets or full sensitive payloads.

Minimum metrics:

- Instructions by type/status/age/channel and safe failure reason.
- Idempotent replays/conflicts and duplicate-attempt prevention.
- Provider attempts by route/status/latency and Uncertain age/status-enquiry backlog.
- Failover decisions and blocked failover due to unresolved attempts.
- Ledger hold/post/reversal result and payment-ledger divergence.
- Limit reservation utilisation/conflict/stuck-release counts.
- Beneficiary verification/cooling-off/step-up outcomes.
- Collection callback verification/match/credit/reconciliation lag.
- VTU catalogue freshness, validation, wallet/availability, provider/delivery status and token-disclosure failures.
- Outbox/inbox/dead-letter lag, reconciliation breaks and audit failures.

Traces span channel, Identity/Risk/Workflow, Payments, Ledger, Integration/provider, notification and reconciliation. Log stable refs and safe state only; never log financial request/response bodies wholesale.

Alerts/runbooks cover aged Uncertain instructions, provider health/wallet threshold, stuck holds/limits, ledger/provider divergence, duplicate external refs, failed status enquiry, token delivery failure, reconciliation backlog and callback attack/replay.

## 18. Failure, retry and recovery

| Failure | Safe state | Recovery |
| --- | --- | --- |
| Validation/risk/approval fails | Rejected; no provider attempt | Correct/new approved request |
| Ledger precondition fails | Rejected/Failed with authoritative ref | No provider submission |
| Provider fails before submission | `FailedBeforeSubmission` attempt | Safe retry/failover under policy |
| Provider timeout after submission | Payment/attempt `Uncertain` | Status enquiry using original refs |
| Conflicting provider events | Investigation/reconciliation | Verify status; no last-write-wins |
| Provider success, Ledger completion fails | Explicit inconsistent/pending state | Idempotent Ledger retry/controlled action and reconciliation |
| Ledger debit, provider proven failure | CompensationEligible/Investigation | Approved Ledger reversal/compensation, never direct credit |
| Delivery missing after VTU success | DeliveryPending/investigation | Requery/redelivery policy; do not duplicate charge |
| Outbox publish failure | Local state remains authoritative | Publisher retries; no duplicate command outcome |
| Process crash/restart | Original state/key persists | Resume worker/saga from durable state |
| Restore/PITR | New execution paused until cross-service reconciliation | Reconcile payments, attempts, limits, Ledger, Integration and outbox |

Manual recovery uses typed authorised commands. Operators cannot edit statuses, provider refs, Ledger refs, limit reservations or token values directly.

Restore exercises verify instruction/attempt histories, idempotency, one-active-attempt invariant, limit reservations, schedules/occurrences, encrypted tokens, catalogue/mappings, provider/Ledger refs and outbox/inbox continuity.

## 19. Configuration

| Setting | Owner | Secret | Validation | Reload behaviour |
| --- | --- | --- | --- | --- |
| Enabled payment/service types | Product/Payments | No | Institution/channel/certification | Versioned/effective-dated |
| Financial sequence per payment type | Finance/Payments/Ledger | No | Accounting/idempotency/failure analysis | Versioned; production gate |
| Beneficiary/cooling-off policy | Risk/Product | No | Channel/KYC/device/amount conditions | Versioned |
| Payment/limit policy | Product/Risk | No | Amount/currency/window/fact versions | Versioned/effective-dated |
| Pricing/component rules | Product/Finance/Tax | No | Exact approved calculation refs | Versioned/effective-dated |
| Route policy | Payments/Operations | No | Capabilities, health freshness, limits, cost, safety | Versioned/approved |
| Provider route/capability refs | Integration Hub | Credential refs only | Environment/certification/services | Versioned projection |
| Timeout/status enquiry/failover policy | Provider owner/Payments | No | Provider semantics/idempotency | Versioned |
| VTU catalogue/mappings | Product/Payments/Integration | No | Parameters, pricing, provider codes | Versioned/approved |
| Provider wallet thresholds | Operations/Finance | No | Currency/service/freshness | Versioned |
| Reconciliation/tolerance policy | Finance/Reconciliation | No | Payment/provider/ledger keys | Versioned |

Missing/stale critical policy disables the affected route/action. Feature flags cannot bypass idempotency, status enquiry, Ledger authority, security, audit or reconciliation.

## 20. Implementation slices

1. Approve Phase 1 payment types, financial sequences, provider semantics, limits, pricing, routing, VTU catalogue and reconciliation decisions.
2. Define canonical payment, provider-attempt, Ledger and event contracts with state-machine/property tests.
3. Implement payment instruction/idempotency/status history and reference consumers.
4. Implement internal transfer end to end through one atomic Ledger command.
5. Implement beneficiary verification, confirmation binding, cooling-off and limit reservations.
6. Implement NIP name enquiry/transfer/status enquiry with synthetic Integration adapter and Uncertain state.
7. Implement route-policy versions/decisions and safe failover eligibility.
8. Implement server-side collection initialisation, verified canonical callbacks, matching and one Ledger credit.
9. Implement canonical VTU catalogue/mapping publication and customer-parameter validation.
10. Implement airtime/data purchase with provider wallet/availability, Ledger, uncertainty and delivery state.
11. Implement approved bill categories and protected token/voucher/receipt flows.
12. Implement refund/recall/reversal/investigation requests through Workflow/owners.
13. Implement scheduled occurrence foundation only for approved Phase 1 journeys; keep bulk/virtual accounts disabled.
14. Add audit, reconciliation feeds/jobs, dashboards, alerts and runbooks.
15. Complete provider certification, security, concurrency, resilience, performance and restore evidence.

Each slice starts with traceable failing tests and leaves incomplete providers/payment types disabled.

## 21. Test plan

| Test ID | Requirement/story | Level | Scenario | Expected evidence |
| --- | --- | --- | --- | --- |
| PAY-001 | PAY-005 / US-025 | Idempotency | Repeat internal transfer concurrently | One instruction and one Ledger posting |
| PAY-002 | US-025 / BR-001 | Integration | Eligible internal transfer | Atomic debit/credit plus explicit components |
| PAY-003 | US-025 | Security | Tampered amount/fee/ownership/restriction | Server rejects; no posting |
| PAY-004 | PAY-003 / US-026 | Integration | Name enquiry then exact confirmation | Bound result/session/destination/expiry |
| PAY-005 | PAY-003 | Security | Destination/amount changes after confirmation | Confirmation invalidated |
| PAY-006 | PAY-006 / US-026 | Integration | NIP successful flow | Distinct payment/provider/Ledger states and trace |
| PAY-007 | PAY-007 / US-026 / BR-006 | Resilience | Provider timeout after submission | Uncertain and status enquiry; no retry/failover |
| PAY-008 | PAY-015 / US-028 | Concurrency | Failover races with late original success | No second debit/submission; investigation if conflict |
| PAY-009 | PAY-014 / US-028 | Domain | Route candidates use approved inputs | Deterministic recorded decision/version/freshness |
| PAY-010 | PAY-014 | Security | Client attempts provider-route override | Ignored/rejected |
| PAY-011 | PAY-008, PAY-009 / US-027 | Security | Forged/replayed callback or redirect success | No credit/final success |
| PAY-012 | US-027 | Integration | Verified collection posts | One matched receipt and one Ledger credit |
| PAY-013 | PAY-013 | Domain | Calculate fee/tax/commission/provider cost | Explicit approved components before execution |
| PAY-014 | PAY-004 / US-029 | Integration | New beneficiary with step-up/cooling-off | Cannot transact before Active |
| PAY-015 | PAY-004 | Security | Cross-customer/tenant beneficiary use | Denied without disclosure |
| PAY-016 | US-029 | Concurrency | Concurrent transfers consume daily limit | Approved limit cannot be exceeded |
| PAY-017 | US-029 | Integration | Limit increase requires step-up/approval | Exact approved version becomes effective |
| PAY-018 | VTU-003 / US-032 | Integration | Publish provider catalogue update | Canonical approved version; pricing unchanged silently |
| PAY-019 | VTU-004 / US-033 | Integration | Validate meter/account/product parameters | Exact provider validation ref bound to purchase |
| PAY-020 | VTU-001 / US-032 | Integration | Airtime/data successful purchase | Ledger/provider/delivery refs and explicit charges |
| PAY-021 | VTU-006 / US-032 | Resilience | VTU timeout | Status enquiry before reversal/failover |
| PAY-022 | VTU-005 | Integration | Financial success but token delivery pending | Separate DeliveryPending/investigation state |
| PAY-023 | VTU-005, SEC-013 | Security | Token/voucher storage/output/log inspection | Encrypted/masked; no event/log leakage |
| PAY-024 | VTU-007 | Resilience | Provider wallet/availability stale/low | Route blocked/degraded per exact policy and alert |
| PAY-025 | VTU-008 | Domain | Commission/margin/charge calculation | Versioned explicit components/settlement ref |
| PAY-026 | VTU-009 / US-033 | Contract | Receipt/notification request | Accurate authoritative refs; no provider secret |
| PAY-027 | PAY-012 | Integration | Approved refund/recall/reversal request | Distinct action and owning-service outcomes |
| PAY-028 | PAY-012 / BR-006 | Security | Reverse unresolved Uncertain payment | Denied pending resolution |
| PAY-029 | PAY-001 | Recovery | Scheduled worker restarts occurrence | Same occurrence/key; no duplicate payment |
| PAY-030 | PAY-010, PAY-011 | Architecture | Call Phase 3 virtual/bulk capability | Endpoint/worker unavailable |
| PAY-031 | PAY-005 | Security | Same key with different request hash | Conflict/audit; no second instruction |
| PAY-032 | SEC-009 | Security | Cross-tenant payment/attempt/action ID | Denied without existence disclosure |
| PAY-033 | SEC-011 | Security | Mass-assign status/provider/ledger/approval refs | Rejected/ignored |
| PAY-034 | NFR-RES-002 | Messaging | Replay provider/Ledger/workflow events | No duplicate transition/command/notification |
| PAY-035 | VTU-010, NIB-005, PSP-006 | Reconciliation | Daily provider-payment-Ledger comparison | Matches and unresolved breaks remain visible |
| PAY-036 | AUD-004, NFR-OBS-001 | End-to-end | Trace channel through provider/Ledger/reconciliation | Complete correlated references/evidence |
| PAY-037 | NFR-DR-001 | Recovery | Restore and resume uncertain/processing payments | No duplicate debit/provider attempt; cross-service reconciliation |
| PAY-038 | PAY-001..PAY-015 | End-to-end | Internal, NIP and collection journeys | State/financial/idempotency invariants pass |
| PAY-039 | VTU-001..VTU-010 | End-to-end | Airtime/data/bill journey | Catalogue, validation, payment, delivery and reconciliation pass |

Provider contract tests use certified sandbox fixtures with synthetic data. Fault injection covers every pre-submit/post-submit/status/Ledger/outbox boundary.

## 22. Acceptance criteria

- [ ] PAY-001 through PAY-015, VTU-001 through VTU-010 and Phase 1 stories trace to decisions, contracts, code and tests.
- [ ] Payment/provider/Ledger/delivery/reconciliation states and references remain distinct.
- [ ] Idempotency prevents duplicate instruction, provider attempt and Ledger outcome under concurrency/replay.
- [ ] Internal transfer posts debit and credit atomically through Ledger.
- [ ] NIP name enquiry and exact confirmation precede eligible submission.
- [ ] Uncertain attempts require status enquiry and block failover/reversal/resubmission.
- [ ] Collection redirect/callback cannot create success/credit without verified server status.
- [ ] Beneficiary, cooling-off, limit, risk, permission, step-up and approval controls are server-enforced.
- [ ] Components are approved/calculated before execution and posted explicitly.
- [ ] VTU catalogue, validation, wallet/availability, provider result and delivery state are versioned/traceable.
- [ ] Tokens/secrets/PII are encrypted/masked and absent from logs/events/errors.
- [ ] Daily reconciliation and restore evidence preserve unresolved exceptions without duplicate money movement.
- [ ] Phase 3 bulk/payroll and virtual-account execution cannot be enabled accidentally.

## 23. Definition of Done

- Build, analyzers, formatting, architecture tests and affected suites pass.
- EF Core migrations apply to empty/supported upgrade databases; uniqueness, state and amount/currency constraints pass PostgreSQL tests.
- OpenAPI/internal/event contracts pass compatibility, security and financial review.
- Ledger integration proves one debit/credit outcome; Integration Hub contract tests prove canonical provider isolation.
- Approved provider sandbox/certification covers success, pre-submit failure, timeout, late result, status enquiry, replay and recovery.
- Concurrent idempotency, limits and failover tests prove no duplicate charge/submission.
- Internal, NIP, collection, airtime/data and approved bill journeys pass end to end with synthetic data.
- Reconciliation, provider outage, uncertain payment, token delivery and restore runbooks are demonstrated.
- Traceability includes requirements/stories, provider versions, permissions, APIs/events, migrations, tests and operational evidence.

## 24. Open decisions

| Decision | Impact | Owner | Required by | Status |
| --- | --- | --- | --- | --- |
| Phase 1 enabled payment/service/provider catalogue | Scope/certification | Product/Payments | Slice 1 | Open |
| Financial sequence per transfer/collection/VTU type | Holds/debits/compensation | Finance/Payments/Ledger | Slice 1 | Open |
| NIBSS NIP specification/session/status/reversal semantics | US-026 correctness | Integration/Payments/Provider owner | Slice 6 | Open |
| Paystack/Flutterwave collection verification semantics | US-027 correctness | Integration/Payments | Slice 8 | Open |
| Provider timeout/retry/status/failover eligibility | Duplicate prevention | Provider owner/Risk | Slice 6 | Open |
| Beneficiary verification/cooling-off policy | US-029 security/UX | Risk/Product | Slice 5 | Open |
| Limit windows, reservation/release and timezones | Concurrent control | Risk/Product/Finance | Slice 5 | Open |
| Pricing, fee, tax, commission, margin/provider-cost rules | Financial components | Finance/Tax/Product | Before execution |
| Route scoring inputs/windows/weights/freshness | US-028 behaviour | Payments/Operations/Risk | Slice 7 | Open |
| Approved bill categories/validation parameters | US-033 scope | Product/Payments | Slice 9 | Open |
| VTU provider catalogue sync/mapping approval | US-032/033 product correctness | Payments/Integration | Slice 9 | Open |
| Provider wallet threshold/freshness | Route availability | Operations/Finance | Slice 10 | Open |
| Token/voucher disclosure and retention | Security/support | Security/Product | Slice 11 | Open |
| Refund/recall/reversal/partial action policy | After-sale correctness | Finance/Payments/Provider owner | Slice 12 | Open |
| Scheduled/standing-order Phase 1 journeys | PAY-001 scope | Product/Risk | Slice 13 | Open |
| Reconciliation keys/tolerances/cut-offs | Daily exception handling | Finance/Reconciliation | Slice 14 | Open |
| Capacity, latency, availability, RTO/RPO | Release gates | Product/Platform | Before release testing | Open |

## 25. Prohibited assumptions

- Do not invent provider endpoints, fields, status meanings, retry safety, SLAs, routing weights or certification results.
- Do not treat timeout, redirect, callback, notification or workflow approval as a final financial outcome.
- Do not retry/fail over an Uncertain attempt or create a new idempotency key after timeout.
- Do not allow one instruction to produce a second customer debit.
- Do not trust client amount, fee, beneficiary name, account ownership, provider choice, approval or status.
- Do not call provider SDKs from Payments domain/application; use canonical Integration Hub contracts.
- Do not write Ledger tables or calculate an authoritative balance in Payments.
- Do not collapse payment, provider, Ledger, delivery and reconciliation states into one status.
- Do not release limits/holds automatically while an attempt may still settle.
- Do not expose full account/meter identifiers, tokens/vouchers, provider payloads or secrets.
- Do not change customer pricing silently from provider catalogue sync.
- Do not implement virtual-account or bulk/payroll execution as incidental Phase 1 scope.
- Do not close/write off reconciliation breaks because a payment appears successful in a read model.

Until provider/policy decisions are approved, use synthetic adapters and keep the route/product disabled.

## 26. Agent implementation instructions

- Required reading: root `AGENTS.md`, architecture, prior Phase 1 epics, this epic, PAY/VTU/LED/API/NIB/PSP requirements, US-025..US-029/US-032/US-033 and approved provider/accounting decisions.
- Expected C# boundaries: `CoreBanking.PaymentsVtu.Domain`, `.Application`, `.Infrastructure`, `.Api`, `.Contracts`, `.Workers` and `.Tests`.
- Domain code must not reference ASP.NET, EF Core, provider SDKs, broker clients, Ledger/Workflow/Integration implementations or remote DTOs.
- Use strongly typed IDs/references, `decimal`, explicit currency, `DateOnly`/`DateTimeOffset`, `TimeProvider`, cancellation tokens and optimistic concurrency.
- Keep provider adapters in Integration Hub. Payments Infrastructure contains only canonical clients/consumers and owned persistence.
- Use EF Core migrations owned by Payments and VTU and PostgreSQL/Testcontainers for idempotency, limit and attempt concurrency.
- Start each slice with failing tests, including timeout/late-result/failover, cross-tenant, duplicate-key, limit race, callback replay and no-second-debit cases.
- Use transactional outbox/inbox and stable child idempotency keys for every Ledger/provider/workflow/notification command.
- Stop when work requires an unapproved provider semantic, financial sequence, routing/limit/pricing rule, status mapping, token policy, reconciliation tolerance or Phase 3 capability.
- Completion report must list coverage, provider certifications/versions, permissions, APIs/events, migrations, accounting/components, tests/commands, resilience/reconciliation/recovery evidence, assumptions, risks and rollout notes.

