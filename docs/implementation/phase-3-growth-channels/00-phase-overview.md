---
phase: "Phase 3 - Growth Channels"
status: Draft
owning_services:
  - Customer and Onboarding
  - Identity and Access
  - Payments and VTU
  - Operations and Workflow
  - Risk and Reporting
  - Integration Hub
  - Audit and Reconciliation
  - Core Banking and Ledger
requirements:
  - PAY-001..PAY-015
  - CHN-001..CHN-006
  - API-001..API-008
  - PSP-001..PSP-006
  - IAM-001..IAM-011
  - WFL-001..WFL-008
  - RSK-001..RSK-008
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
stories:
  - US-030
  - US-031
  - US-059
  - US-060
  - US-061
  - US-062
  - US-063
  - US-067
dependencies:
  - Phase 0 - Engineering Foundation verified
  - Phase 1 - Banking Foundation verified
  - Phase 2 - Regulated Operations verified for enabled dependencies
last_updated: 2026-10-16
---

# Phase 3 - Growth Channels

## Objective

Extend the verified platform with governed corporate, collections, card, merchant and partner channels while preserving a single authoritative ledger outcome, corporate mandate enforcement, provider uncertainty, tenant isolation and operational evidence.

Phase 3 increases external reach and transaction volume. Its exit emphasis is scale, partner governance and channel security, not merely endpoint count.

## Release boundary

Phase 3 delivers eight allocated stories: bulk/payroll processing, static/dynamic virtual accounts, corporate users/mandates, corporate payment approval, corporate API/webhooks, card lifecycle controls, merchant acquiring and versioned partner APIs.

All backend code remains C#/.NET. The nine initial microservices remain the deployment topology. Card and merchant modules may begin inside an approved existing service, but their ownership and processor boundaries require ADRs before persistence or production integrations are created.

## Entry criteria

- Phase 0 delivery, identity, messaging, observability and security foundations are verified.
- Phase 1 institution, IAM, customer, account, ledger, payment, workflow, provider, document, audit and release controls are verified.
- Phase 2 corporate-party/signatory source data, risk/AML, reconciliation/settlement and regulated operations needed by selected Phase 3 capabilities are verified.
- Corporate and partner authentication, mandate, API versioning, webhook, processor, PCI, merchant settlement and sandbox decisions are approved for implementation.
- Accounting mappings exist for bulk payments, collections, card authorization/clearing, merchant fees/reserves and settlement where enabled.

If prerequisites are only documented, Phase 3 remains planning-only.

## Epic map

| Order | Epic | Primary owner | Stories | Outcome |
| --- | --- | --- | --- | --- |
| 1 | Corporate Users, Mandates and Entitlements | Identity and Access with Customer and Onboarding | US-059 | Bank-bounded corporate administration, users, accounts, limits and signing rules |
| 2 | Bulk and Payroll Payment Processing | Payments and VTU | US-030 | Validated files, repair, item outcomes and aggregate batch status |
| 3 | Corporate Payment Approval and Submission | Operations and Workflow with Payments and VTU | US-060 | Mandate/amount/account/user approval and idempotent submission |
| 4 | Virtual Accounts and Collection Matching | Payments and VTU with Integration Hub | US-031 | Static/dynamic accounts, collection references, validated matching and investigation |
| 5 | Corporate APIs and Webhooks | Integration Hub with Identity and Access | US-061 | Scoped credentials, rate limits, signed webhooks, certification and activation |
| 6 | Card Lifecycle and Controls | Owner requires card-boundary ADR | US-062 | Physical/virtual card lifecycle, tokenisation, limits and channel/geography/MCC controls |
| 7 | Merchant Acquiring and Settlement | Owner requires merchant-boundary ADR | US-063 | Merchant KYC, terminals, fees, reserves, settlement and reconciliation |
| 8 | Partner APIs and Sandbox | Integration Hub | US-067 | Documented versioned APIs, authentication/scopes/signing, quotas and deprecation |

## Dependency map

```text
Verified prior phases
    |
    +--> 01 Corporate Users, Mandates and Entitlements
    |        +--> 02 Bulk and Payroll Payment Processing
    |        +--> 03 Corporate Payment Approval and Submission
    |        +--> 05 Corporate APIs and Webhooks
    |
    +--> 04 Virtual Accounts and Collection Matching
    |        +--> 05 Corporate APIs and Webhooks
    |        +--> 07 Merchant Acquiring and Settlement
    |
    +--> 06 Card Lifecycle and Controls
    |        +--> 07 Merchant Acquiring and Settlement where processor/acquiring scope overlaps
    |
    +--> 08 Partner APIs and Sandbox
             exposes only approved contracts from enabled owner services
```

## Service ownership

### Identity and Access

- Own corporate identities, credentials, authentication, corporate-user status and bank-approved entitlement ceilings.
- Enforce delegated administration without allowing corporate administrators to exceed bank grants.
- Own partner/corporate client identity references and credential lifecycle where selected by ADR.

### Customer and Onboarding

- Remain source of truth for corporate and merchant parties, beneficial owners, signatories and KYC status.
- Publish safe versioned facts; do not own payment mandates or authentication credentials by implication.

### Payments and VTU

- Own bulk/payment instructions, virtual accounts/collection matching and selected card/merchant payment orchestration if approved.
- Submit all money changes through Core Banking and Ledger.
- Preserve one intended financial outcome across file retry, approval retry, webhook replay and provider failover.

### Operations and Workflow

- Own corporate approval tasks, file-repair/exception work and controlled overrides.
- A completed approval authorises submission; it does not prove the payment posted.

### Integration Hub

- Own corporate/partner API gateway integration, client registration/certification, outbound webhook delivery and processor/provider adapters.
- Never become the authority for customer, payment, card, merchant or ledger state.

### Risk and Reporting

- Own card/merchant/corporate risk decisions, monitoring alerts and governed reporting read models.
- Risk decisions cannot directly alter balances.

### Audit and Reconciliation

- Reconcile bulk items, virtual-account collections, card clearing, merchant transactions, reserves and settlements to payment/ledger outcomes.
- Retain partner certification, approval and manual-resolution evidence.

### Core Banking and Ledger

- Remain the only authority that changes customer, settlement, reserve or fee balances.
- Enforce balanced immutable postings, reversals, periods, dates and explicit accounting components.

## Required cross-service contracts

- Corporate organisation, user, role, entitlement ceiling, account grant, limit and mandate/signing-rule versions.
- Bulk file, validation result, repair version, batch, payment item and aggregate-status contracts.
- Corporate approval context containing mandate, account, amount, currency, user authority, fees and validation version.
- Static/dynamic virtual account, collection reference, incoming notification, match and investigation contracts.
- API client, credential reference, scope, rate plan, certificate, certification run and environment activation contracts.
- Outbound webhook subscription, signed delivery, attempt, retry, dead-letter and replay contracts.
- Card token/reference, lifecycle state, controls, processor attempt, authorization/clearing and dispute references.
- Merchant, outlet, terminal, pricing, reserve, settlement schedule, transaction and reconciliation contracts.
- Partner API catalogue, version, quota, request-signing, deprecation and sandbox-test contracts.

Every contract carries version, institution, correlation/causation and minimum necessary data. Financial commands carry explicit currency and scoped idempotency.

## Financial-integrity gates

- Each bulk item has its own idempotency and financial outcome; batch status is derived and never substitutes for item truth.
- Repair creates a new version and does not silently change an approved file/item.
- Corporate approval is bound to the validated payment content, fees, account, amount, currency and mandate version.
- Virtual-account notification or callback never alone proves final funds; provider/bank status and ledger posting are verified.
- A virtual account is linked to institution, customer/invoice/collection reference and approved provider mapping.
- Card authorization, clearing, reversal, refund, chargeback, fee, reserve and settlement components remain explicit and reconcilable.
- Merchant settlement and reserves reconcile to acquiring transaction records and ledger entries.
- Provider timeout or ambiguity becomes `Uncertain`; no blind retry, failover or duplicate debit.
- Only Core Banking and Ledger posts balances; all corrections use linked reversals/adjustments.

## Identity, RBAC and mandate gates

- Corporate administrators operate only within bank-approved corporate, account, permission and limit ceilings.
- Corporate mandates support approved single, joint and multi-level signing rules with effective dates and versions.
- Corporate and bank approval is required for entitlement/mandate changes where configured.
- Approvers cannot approve their own restricted initiation and cannot exceed account/user/amount/currency/mandate limits.
- Corporate users, technical clients, partner clients, merchant operators and service identities have distinct policies.
- API clients use approved authentication, scopes, quotas, network restrictions and optional signing.
- Every owner service revalidates permission, tenant, resource and mandate; gateway checks are not sufficient.

## Channel and provider gates

- Corporate files are authenticated, size/format bounded, malware-scanned where applicable and validated before approval/posting.
- Invalid rows return stable reasons without exposing unrelated data.
- Webhook endpoints and deliveries verify/sign source, timestamp, event identity and replay state.
- Credentials and signing secrets are stored in secret management, rotatable and never returned after creation.
- Production corporate/partner clients require approved certification and activation.
- Card data is tokenised; PCI-sensitive values are not stored unless explicitly required and approved.
- Card controls cover channel, amount, geography and merchant category under processor capability.
- Merchant KYC, risk, terminals, fees, reserves and settlement schedules are approved/versioned.

## Data and migration gates

- Each domain has one approved owner/database before migrations begin.
- Card and merchant ownership ADRs are mandatory before schema creation.
- Tenant/resource IDs, explicit decimals/currency, effective dates, concurrency versions and immutable history are enforced.
- File content and large evidence live in approved object storage; databases store digest/classification/reference.
- Full card PAN/CVV/PIN, secrets and unrestricted file contents never enter logs/events/test fixtures.
- High-volume payment item, delivery, authorization and clearing data has approved partition/retention design.
- No cross-service foreign keys or direct database reads.

## Security and privacy gates

- Threat models cover corporate-admin escalation, mandate tampering, malicious files, credential theft, webhook forgery, API abuse, card-data exposure, terminal compromise, merchant fraud and sandbox-to-production crossover.
- Strong authentication/step-up applies to sensitive corporate/card/merchant actions.
- Request signing includes approved canonicalization, timestamp, nonce and replay protection.
- Rate limits/quotas are tenant/client/route aware and cannot be bypassed by key rotation.
- Sandbox uses synthetic or approved masked data and non-production credentials only.
- Sensitive views/exports and privileged changes generate immutable audit evidence.

## Observability and operations gates

- Trace corporate user/client through file/item, approval, payment, provider, ledger, webhook, collection, clearing and settlement.
- Metrics cover validation/repair, item outcomes, approval age, virtual-account matches, unmatched collections, API quotas/errors, webhook backlog, card/merchant processor uncertainty, terminal health and settlement breaks.
- High-cardinality identifiers do not become metric labels.
- Alerts are tenant-safe, actionable and linked to runbooks.
- Replay and recovery cannot duplicate payment, collection match, webhook delivery effect, card outcome, merchant settlement or audit evidence.

## Phase-level test journeys

1. Corporate administrator creates a user but cannot exceed bank-approved accounts, permissions or limits.
2. Single, joint and multi-level mandates enforce signer count/order and current authority.
3. A bulk file returns row validation reasons, repairs safely, obtains approval and posts each valid item once.
4. Batch aggregate state accurately represents mixed independent item outcomes.
5. Corporate approval binds visible validation results/fees and submits the approved payload idempotently.
6. Static/dynamic virtual accounts map validated incoming funds to customer/invoice and route unmatched funds to investigation.
7. Corporate API credentials are scoped, rate-limited, rotated and activated only after certification.
8. Signed corporate webhooks retry safely and expose delivery evidence without duplicating business effects.
9. Card activation/freeze/replacement/limits enforce authentication and tokenisation.
10. Merchant onboarding, terminal assignment, fees, reserves and settlement reconcile to transactions/ledger.
11. Partner API authenticates/scopes/signs/rate-limits, requires correlation/idempotency and applies version deprecation.
12. Sandbox credentials/data cannot access production resources.
13. Cross-tenant, self-approval, stale mandate, replay, provider timeout and sensitive-data leakage tests fail safely.

## Phase exit criteria

- [ ] All Phase 3 Must requirements and selected Should/Could scope are implemented or have approved exceptions.
- [ ] All eight stories trace to design, contracts, databases, code, tests and operational evidence.
- [ ] No open severity-1/2 financial-integrity, security or tenant-isolation defects remain.
- [ ] Bulk/item idempotency, mandates, virtual-account matching, API/webhook replay and provider uncertainty suites pass.
- [ ] Enabled card/merchant PCI, processor, settlement and reconciliation controls are approved.
- [ ] Corporate/partner clients complete certification and production activation controls.
- [ ] Performance, capacity, availability, recovery and monitoring meet approved Phase 3 load targets.
- [ ] Security, privacy, compliance, finance, operations and UAT approvals are complete.

## Open decisions

| Decision | Epics | Owner |
| --- | --- | --- |
| Corporate mandate model, bank ceiling and delegated administration | 1, 3 | Corporate Banking, IAM, Legal |
| Bulk formats, size/row limits, repair and partial-processing policy | 2 | Payments, Corporate Product, Operations |
| Corporate fees/validation snapshot and submission semantics | 3 | Payments, Finance, Corporate Product |
| Virtual-account providers, lifecycle, expiry and unmatched-funds policy | 4 | Payments, Finance, Providers |
| Corporate credential, signing, webhook and certification model | 5 | Security, Integration, Corporate Product |
| Card module/data owner, processor, PCI scope and authorization model | 6 | Architecture, Security, Card Operations |
| Merchant module/data owner, acquirer, pricing/reserves and settlement | 7 | Architecture, Merchant Ops, Finance, Risk |
| Partner catalogue, auth/signing, quota, sandbox and deprecation policy | 8 | Integration, Security, Product |
| Phase 3 volumes, targets, retention, RTO/RPO and support model | All | Product, SRE, Risk, Compliance |

## Prohibited assumptions

- Do not infer a corporate user's authority from corporate-party/signatory status alone.
- Do not let a corporate administrator exceed bank-approved entitlements.
- Do not treat batch approval as proof every item succeeded.
- Do not trust file/client/provider calculations, fees, settlement accounts or outcomes.
- Do not post a virtual-account callback directly to a final outcome without verification.
- Do not store full card PAN, CVV, PIN or processor secrets unnecessarily.
- Do not create card or merchant microservices/databases before ownership ADRs.
- Do not expose service-private models/tables through partner APIs.
- Do not use production credentials or unmasked production data in sandbox.

## Epic implementation documents

1. [`01-corporate-users-mandates-and-entitlements.md`](01-corporate-users-mandates-and-entitlements.md)
2. [`02-bulk-and-payroll-payment-processing.md`](02-bulk-and-payroll-payment-processing.md)
3. [`03-corporate-payment-approval-and-submission.md`](03-corporate-payment-approval-and-submission.md)
4. [`04-virtual-accounts-and-collection-matching.md`](04-virtual-accounts-and-collection-matching.md)
5. [`05-corporate-apis-and-webhooks.md`](05-corporate-apis-and-webhooks.md)
6. [`06-card-lifecycle-and-controls.md`](06-card-lifecycle-and-controls.md)
7. [`07-merchant-acquiring-and-settlement.md`](07-merchant-acquiring-and-settlement.md)
8. [`08-partner-apis-and-sandbox.md`](08-partner-apis-and-sandbox.md)

All Phase 3 epic instructions are drafted and remain subject to approval before implementation.

## Agent instructions

- Read root/service `AGENTS.md`, architecture, Phase 0-2 evidence, this overview and applicable epic.
- Confirm selected Should/Could scope and required ADRs before irreversible design.
- Preserve C#/.NET, nine services, service-owned data, ledger authority and explicit contracts.
- Implement one complete vertical slice with permission, tenant, mandate, idempotency, audit, observability, reconciliation and recovery evidence.
- Stop for unapproved mandate, accounting, provider, PCI, merchant, partner or production-target decisions.

