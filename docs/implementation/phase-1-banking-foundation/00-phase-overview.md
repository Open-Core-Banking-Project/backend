---
phase: "Phase 1 - Banking Foundation"
status: Draft
owning_services:
  - Core Banking and Ledger
  - Customer and Onboarding
  - Identity and Access
  - Payments and VTU
  - Channels and Engagement
  - Operations and Workflow
  - Integration Hub
  - Audit and Reconciliation
  - Risk and Reporting
requirements:
  - CFG-001..CFG-008
  - IAM-001..IAM-011
  - CUS-001..CUS-010
  - ACC-001..ACC-010
  - LED-001..LED-013
  - PAY-001..PAY-015
  - VTU-001..VTU-010
  - COM-001..COM-010
  - WFL-001..WFL-008
  - API-001..API-008
  - NIB-001..NIB-006
  - PSP-001..PSP-006
  - DAT-001..DAT-008
  - SEC-001..SEC-018
stories:
  - US-001..US-009
  - US-011
  - US-013..US-023
  - US-025..US-029
  - US-032..US-037
  - US-048..US-050
  - US-064
  - US-068..US-076
  - US-079..US-080
dependencies: [Phase 0 - Engineering Foundation]
last_updated: 2026-10-15
---

# Phase 1 - Banking Foundation

## Objective

Deliver the first secure, tenant-aware banking foundation: institution setup, identity and permission enforcement, individual customer onboarding, deposit accounts, protected double-entry ledger posting, basic payments and VTU, workflow approvals, customer communications, channel foundations, immutable audit and production-oriented operations.

Phase 1 must prove financial integrity and operational safety. Breadth is secondary to correct end-to-end outcomes.

## Entry criteria

Phase 1 begins only when Phase 0 has verified:

- Deterministic repository build and architecture boundaries.
- Local infrastructure and service-owned databases.
- Trusted tenant context and permission enforcement.
- Transactional outbox/inbox and idempotent consumption.
- Audit, telemetry and CI foundation.
- Walking-skeleton recovery and replay safety.

Any exception requires explicit approval and an owner/date for remediation.

## Phase epics

| Order | Epic | Owning service | Primary stories | Outcome |
| --- | --- | --- | --- | --- |
| 1 | Institution and Tenancy | Core Banking and Ledger | US-001..US-004 | Institution, branches, calendar, configuration and environment-specific provider references |
| 2 | Identity, Roles and Permissions | Identity and Access | US-005..US-009 | Production authentication foundation, roles, permissions, scopes, devices and maker-checker separation |
| 3 | Customer and Onboarding | Customer and Onboarding | US-011, US-013..US-015 | Individual customer, KYC workflow, verification, risk tier and customer 360 |
| 4 | Products and Accounts | Core Banking and Ledger | US-016, US-017, US-023 | Versioned deposit products and controlled account lifecycle |
| 5 | Core Ledger and Financial Posting | Core Banking and Ledger | US-018..US-022 | Balanced, immutable, idempotent posting, reversals, balances and processing checkpoints |
| 6 | Workflow and Maker-Checker | Operations and Workflow | US-048..US-050 | Versioned approvals, cases, failed-payment investigation and controlled overrides |
| 7 | Payments, Collections and VTU | Payments and VTU | US-025..US-029, US-032, US-033 | Internal/interbank payment orchestration, providers, beneficiaries, bills and VTU |
| 8 | Integration Hub and Providers | Integration Hub | US-004, US-068, US-069 | Canonical adapters, credentials, webhooks, status enquiry, health and failover readiness |
| 9 | Channels, CMS and Communications | Channels and Engagement | US-034..US-037, US-075, US-076 | Notification providers/templates plus approved USSD, WhatsApp chatbot and CMS foundations |
| 10 | Documents and Statements | Risk and Reporting with owning services | US-023, US-064 | Statements, receipts and controlled document storage/retrieval |
| 11 | Audit, Traceability and Recovery | Audit and Reconciliation | US-070..US-072 | Immutable audit, end-to-end transaction trace and tested backup/restore evidence |
| 12 | Operations and Release Readiness | Platform-wide | US-074, US-079, US-080 | Staff operations APIs, production pipeline, dashboards, alerts and runbooks |

Each epic receives its own document using `EPIC_TEMPLATE.md` before implementation begins.

## Dependency map

```text
Institution and Tenancy
    ├──> Identity, Roles and Permissions
    ├──> Products and Accounts
    └──> Provider/Channel Configuration

Identity, Roles and Permissions
    ├──> Customer and Onboarding
    ├──> Workflow and Maker-Checker
    └──> Channels, CMS and Communications

Customer and Onboarding
    └──> Products and Accounts

Products and Accounts
    └──> Core Ledger and Financial Posting

Core Ledger and Financial Posting
    └──> Payments, Collections and VTU

Workflow and Maker-Checker
    ├──> Product publication
    ├──> Sensitive account actions
    ├──> Payments above policy thresholds
    └──> CMS publication and administrative changes

Integration Hub and Providers
    ├──> KYC verification
    ├──> Payments and VTU
    └──> USSD, WhatsApp and notification providers

All epics
    └──> Audit, Traceability and Operations
```

## Roles and permissions baseline

Every epic defines permissions before endpoints or handlers are implemented. Initial role names are examples and remain configurable.

| Capability | Permission examples | Example role | Important scope |
| --- | --- | --- | --- |
| Institution configuration | `institution.configure`, `institution.configuration.approve` | Institution Administrator | Institution/environment |
| Branch management | `institution.branch.create`, `institution.branch.activate` | Institution Administrator | Institution/region |
| Role administration | `identity.role.create`, `identity.permission.assign` | Security Administrator | Institution |
| Customer onboarding | `customer.profile.create`, `customer.kyc.submit` | Onboarding Officer | Institution/branch |
| KYC approval | `customer.kyc.approve` | Compliance Officer | Institution/risk tier |
| Account opening | `account.open`, `account.open.approve` | Operations Officer/Supervisor | Product/branch |
| Account restriction | `account.restriction.apply`, `account.restriction.approve` | Authorised Operations | Account/branch/reason |
| Financial posting | `ledger.transaction.post` | Internal service identity | Accounting event, not general user access |
| Manual journal | `ledger.manual_journal.create`, `ledger.manual_journal.approve` | Finance Officer/Approver | Ledger entity/amount |
| Payment initiation | `payment.transfer.initiate` | Customer/Operations Officer | Account/channel/limit |
| Payment approval | `payment.transfer.approve` | Supervisor/Corporate Approver | Mandate/amount |
| USSD administration | `channel.ussd.menu.manage`, `channel.ussd.menu.publish` | Channel Administrator | Institution/short code |
| WhatsApp support | `channel.whatsapp.conversation.handle` | Chat Support Agent | Assigned conversation |
| CMS content | `cms.content.create`, `cms.content.approve`, `cms.content.publish` | Content Author/Approver/Publisher | Institution/channel/category |
| Audit access | `audit.event.view` | Auditor | Institution/classification |

No role receives unrestricted wildcard access by default. A frontend menu, USSD journey or chatbot intent is not an authorisation control.

## Cross-service contracts required

Phase 1 must define and version:

- Institution and branch reference events.
- Business-date and configuration-publication events.
- Identity, entitlement-version and permission-change events.
- Customer-created, KYC-status and customer-activated events.
- Account-opened, account-restricted and account-closed events.
- Financial-transaction-posted and transaction-reversed events.
- Payment-instruction, provider-attempt and payment-status contracts.
- Approval-requested and approval-decided events.
- Notification-request and delivery-status contracts.
- Audit-event and financial-trace-link contracts.

Events expose only the minimum consumer data and never become a shortcut for shared domain models.

## Database and migration gates

- Each service owns its migrations and database principal.
- Tenant-owned rows carry trusted institution identity.
- Monetary columns use explicit numeric precision/scale and currency.
- Financial/audit history is append-only through normal paths.
- Unique constraints enforce idempotency and business references under concurrency.
- Every migration has data-impact, rollout and verification notes.
- No service creates foreign keys to another service's database.

## Financial-integrity gates

Before payments can be accepted:

- Balanced double-entry posting is enforced atomically.
- Posted entries cannot be edited or deleted.
- Reversal links to the original transaction.
- Available and ledger balances are distinct and tested.
- Concurrent debits cannot overspend approved available balance.
- Idempotency survives process restart and concurrent duplicate requests.
- Business, transaction, posting and value dates remain distinct.
- Fee, tax, commission and provider-cost components are explicit.
- Provider uncertainty cannot create a duplicate debit or blind retry.

## Channels, USSD, WhatsApp and CMS scope

Phase 1 always includes the Channels and Engagement service foundation, CMS approval/publication, notification templates and delivery tracking.

The launch institution must decide which production channel journeys are included:

- USSD balance enquiry, mini statement, internal transfer, bill/VTU and support journeys.
- WhatsApp approved knowledge responses, service requests, human handoff and selected authenticated banking actions.
- Website chatbot using the same controlled intent and permission model.
- CMS content categories, locales, approval levels and publication channels.

Until approved, implement only the channel foundation and synthetic provider adapters. Do not assume that possession of a phone number or WhatsApp account satisfies customer authentication.

## Test strategy

Every epic includes unit, integration, contract, architecture and appropriate end-to-end tests. Phase-level journeys include:

1. Create institution, branches, business calendar and administrator permissions.
2. Onboard and verify an individual customer.
3. Approve and open a deposit account.
4. Post an initial balanced transaction idempotently.
5. Query authoritative ledger and available balances.
6. Reverse a posted transaction without altering history.
7. Initiate and complete an internal transfer.
8. Handle a provider timeout as uncertain, then resolve through status enquiry.
9. Approve and publish CMS content to an enabled channel.
10. Deny cross-tenant, missing-permission, wrong-scope and maker-self-approval attempts.
11. Trace customer request through approval, provider, ledger, event and audit evidence.
12. Restore from backup and verify critical records and processing checkpoints.

## Operational-readiness gates

- Dashboards and alerts cover service health, latency, error rate, queues, outbox backlog, provider health, financial exceptions and audit failure.
- Runbooks cover deployment, rollback/forward-fix, migrations, provider outage, uncertain payments, stuck workflows, outbox/inbox repair, EOD restart and backup restoration.
- Secrets, certificates and provider credentials have ownership, rotation and expiry monitoring.
- Production capacity, availability, RTO and RPO are approved and tested for enabled services.
- No real customer data exists in non-production without approved masking.

## Phase exit criteria

- [ ] All allocated Must requirements are implemented or have approved exceptions.
- [ ] Traceability exists from requirement and story to epic, design, contract, code and tests.
- [ ] No open severity-1 or severity-2 security, financial-integrity or tenant-isolation defect remains.
- [ ] Ledger balance, idempotency, concurrency, reversal and date invariants pass.
- [ ] Role, permission, contextual-scope and maker-checker negative tests pass.
- [ ] Provider timeout, uncertain outcome, webhook replay and reconciliation scenarios pass.
- [ ] Backup restore and processing restart are demonstrated.
- [ ] Enabled provider certifications and credentials are complete.
- [ ] Security, compliance, UAT and operational-readiness reviews are approved.

## Epic implementation documents

Review and implement in dependency order:

1. [`01-institution-and-tenancy.md`](01-institution-and-tenancy.md)
2. [`02-identity-roles-and-permissions.md`](02-identity-roles-and-permissions.md)
3. [`03-customer-and-onboarding.md`](03-customer-and-onboarding.md)
4. [`04-products-and-accounts.md`](04-products-and-accounts.md)
5. [`05-core-ledger-and-posting.md`](05-core-ledger-and-posting.md)
6. [`06-workflow-and-maker-checker.md`](06-workflow-and-maker-checker.md)
7. [`07-payments-collections-and-vtu.md`](07-payments-collections-and-vtu.md)
8. [`08-integration-hub-and-providers.md`](08-integration-hub-and-providers.md)
9. [`09-channels-cms-and-communications.md`](09-channels-cms-and-communications.md)
10. [`10-documents-and-statements.md`](10-documents-and-statements.md)
11. [`11-audit-traceability-and-recovery.md`](11-audit-traceability-and-recovery.md)
12. [`12-operations-and-release-readiness.md`](12-operations-and-release-readiness.md)

All Phase 1 epic instructions are now drafted. Each remains subject to review against current requirements and approved decisions before implementation.

Execution state is recorded in the [Phase 1 slice ledger](slice-ledger.md): per-slice status and evidence in [`slice-ledger.csv`](slice-ledger.csv), and every outstanding decision with its owner in [`decision-register.csv`](decision-register.csv). A slice is implemented by an agent but approved only by a human reviewer.

## Agent instructions

- Read Phase 0 evidence before implementing a Phase 1 epic.
- Implement one approved vertical slice at a time and keep the repository deployable.
- Do not infer launch-channel scope, accounting mappings, interest/fee/tax rules, provider semantics, regulatory details, RTO/RPO or production load.
- Stop and surface conflicts that can affect money, permissions, tenant isolation, audit evidence or recovery.

