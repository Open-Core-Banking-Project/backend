# Backend Architecture and Project Structure

## Document status

This document defines the target structure for the C#/.NET backend. It is the detailed starting point for repository scaffolding, database design, service boundaries, contracts, testing, deployment, and coding-agent context.

It is an architecture baseline, not a substitute for approved requirements, accounting rules, provider specifications, security review, or service-specific design. Consequential changes require an Architecture Decision Record (ADR).

## Core decisions

- All production backend services, APIs, workers, integrations, and business logic use C# and .NET.
- The backend uses a microservices architecture.
- The initial topology contains nine coarse-grained, independently deployable microservices.
- Logical domains that do not initially justify separate deployment remain explicit modules inside one of the nine services.
- Each microservice owns its data and exposes versioned APIs and/or durable events.
- No microservice may query or modify another microservice's private tables.
- Only Core Banking and Ledger may authoritatively change customer balances.
- PostgreSQL is the transactional database platform.
- Kafka-compatible messaging is used for durable cross-service business events where asynchronous communication is appropriate.
- Redis is non-authoritative and limited to approved caching, rate limiting, and short-lived coordination.
- Every protected operation is controlled by server-side roles and permissions, with contextual scopes and maker-checker separation where required.
- Financial correctness, tenant isolation, security, auditability, idempotency, and recoverability are mandatory design constraints.

## Initial microservices

| Microservice | Primary responsibilities | Initial internal modules |
| --- | --- | --- |
| Core Banking and Ledger | Institution banking configuration, accounts, products, deposits, ledger, balances, accounting, interest, fees, branch cash, core posting | Institution banking configuration, accounts, deposits, pricing, ledger, accounting periods, EOD/BOD, branch cash, lending posting |
| Customer and Onboarding | Party and customer source of truth, onboarding, KYC lifecycle, customer relationships | Individual customers, corporate parties, contacts, addresses, KYC cases, verifications, duplicate detection, customer lifecycle |
| Identity and Access | Authentication foundation, sessions, roles, permissions, devices, privileged access | Workforce identities, customer identities, corporate identities, roles, permissions, MFA, devices, access review, service identities |
| Payments and VTU | Payment orchestration, beneficiaries, transfers, collections, virtual accounts, bills, VTU | Internal transfer initiation, interbank payments, bulk payments, beneficiaries, virtual accounts, collections, bills, VTU, provider attempts |
| Channels and Engagement | USSD, WhatsApp and website chatbot, CMS, notifications, campaigns, channel sessions and customer engagement | USSD gateway and menus, WhatsApp webhook and conversation orchestration, chatbot intents, human handoff, CMS content, knowledge base, templates, localisation, consent and delivery tracking |
| Operations and Workflow | Maker-checker, human workflow, approvals, cases, disputes, service work | Workflow definitions, approval requests, work queues, complaints, disputes, overrides, SLA and escalation |
| Risk and Reporting | Fraud and AML decisions, sanctions screening, reporting, regulatory preparation, governed read models | Screening, transaction monitoring, risk scoring, alerts, investigations, reports, regulatory returns, data quality and lineage |
| Integration Hub | External adapters, partner APIs, provider configuration, webhook delivery, canonical transformation | NIBSS, KYC, BVN/NIN, payment providers, notification providers, partner APIs, credential references, routing health, webhook delivery |
| Audit and Reconciliation | Immutable audit, transaction traceability, reconciliation, settlement, breaks and evidence | Audit events, provider reconciliation, ledger reconciliation, settlements, suspense investigation, evidence export |

The nine-service topology is deliberately coarse-grained. USSD, WhatsApp chatbot and CMS are explicit modules of Channels and Engagement rather than three small initial deployments. Each may be extracted later when independent scaling, security isolation, data ownership, team ownership, availability, or release cadence justifies the operational cost. Do not create twenty microservices simply because the product has twenty logical domains.

## Repository structure

```text
backend/
├── AGENTS.md
├── CONTRIBUTING.md
├── README.md
├── LICENSE
├── global.json
├── Directory.Build.props
├── Directory.Packages.props
├── .editorconfig
├── CoreBanking.sln
│
├── src/
│   ├── BuildingBlocks/
│   │   ├── CoreBanking.BuildingBlocks.Domain/
│   │   ├── CoreBanking.BuildingBlocks.Application/
│   │   ├── CoreBanking.BuildingBlocks.Infrastructure/
│   │   ├── CoreBanking.BuildingBlocks.Observability/
│   │   └── CoreBanking.BuildingBlocks.Testing/
│   │
│   └── Services/
│       ├── CoreLedger/
│       ├── CustomerOnboarding/
│       ├── IdentityAccess/
│       ├── PaymentsVtu/
│       ├── ChannelsEngagement/
│       ├── OperationsWorkflow/
│       ├── RiskReporting/
│       ├── IntegrationHub/
│       └── AuditReconciliation/
│
├── tests/
│   ├── CoreBanking.ArchitectureTests/
│   ├── CoreBanking.EndToEndTests/
│   ├── CoreBanking.ContractTests/
│   ├── CoreBanking.PerformanceTests/
│   └── CoreBanking.SecurityTests/
│
├── docs/
│   ├── architecture/
│   │   ├── BACKEND_ARCHITECTURE.md
│   │   ├── system-context.md
│   │   ├── container-view.md
│   │   └── sequence-flows/
│   ├── adr/
│   ├── implementation/
│   ├── requirements/
│   ├── accounting/
│   ├── api/
│   ├── events/
│   ├── database/
│   ├── security/
│   └── runbooks/
│
├── deploy/
│   ├── compose/
│   ├── kubernetes/
│   ├── infrastructure-as-code/
│   └── observability/
│
├── scripts/
└── .github/
    ├── workflows/
    └── pull_request_template.md
```

The top-level structure should remain small. Do not introduce a new top-level directory without a durable purpose.

## Per-microservice structure

Each microservice follows the same discoverable layout while retaining its own domain language and data ownership.

```text
src/Services/<Service>/
├── AGENTS.md
├── README.md
├── src/
│   ├── CoreBanking.<Service>.Domain/
│   ├── CoreBanking.<Service>.Application/
│   ├── CoreBanking.<Service>.Infrastructure/
│   ├── CoreBanking.<Service>.Contracts/
│   ├── CoreBanking.<Service>.Api/
│   └── CoreBanking.<Service>.Workers/        # only when background processing is required
└── tests/
    ├── CoreBanking.<Service>.UnitTests/
    ├── CoreBanking.<Service>.IntegrationTests/
    └── CoreBanking.<Service>.ContractTests/
```

### Project responsibilities

| Project | Responsibility | Forbidden dependencies |
| --- | --- | --- |
| Domain | Aggregates, entities, value objects, domain services, invariants and domain events | ASP.NET Core, EF Core, broker clients, provider SDKs, infrastructure projects |
| Application | Commands, queries, use cases, authorisation requirements, ports and transaction coordination | Concrete persistence, provider SDKs, another service's implementation |
| Infrastructure | EF Core, PostgreSQL, outbox/inbox, messaging, cryptography, storage and provider adapters | Another service's private database or infrastructure project |
| Contracts | Versioned HTTP and integration-event schemas | EF entities, private domain models, implementation details |
| API | Authentication, transport validation, OpenAPI, request/result mapping and health endpoints | Business rules that belong in Domain or Application |
| Workers | Scheduled jobs, consumers and durable background processing | Direct balance mutation or unbounded retry loops |

### Channels and Engagement internal structure

USSD, WhatsApp chatbot and CMS are first-class modules with explicit boundaries inside the initial Channels and Engagement microservice:

```text
src/Services/ChannelsEngagement/
├── AGENTS.md
├── README.md
├── src/
│   ├── CoreBanking.ChannelsEngagement.Domain/
│   │   └── Modules/
│   │       ├── Ussd/
│   │       ├── Conversations/
│   │       ├── ContentManagement/
│   │       ├── Notifications/
│   │       └── Campaigns/
│   ├── CoreBanking.ChannelsEngagement.Application/
│   │   └── Modules/
│   │       ├── Ussd/
│   │       ├── WhatsApp/
│   │       ├── Chatbot/
│   │       ├── ContentManagement/
│   │       ├── Notifications/
│   │       └── HumanHandoff/
│   ├── CoreBanking.ChannelsEngagement.Infrastructure/
│   ├── CoreBanking.ChannelsEngagement.Contracts/
│   ├── CoreBanking.ChannelsEngagement.Api/
│   └── CoreBanking.ChannelsEngagement.Workers/
└── tests/
    ├── CoreBanking.ChannelsEngagement.UnitTests/
    ├── CoreBanking.ChannelsEngagement.IntegrationTests/
    └── CoreBanking.ChannelsEngagement.ContractTests/
```

Do not couple the modules through their persistence classes. Use application interfaces and local domain events so that a module can be extracted into a separate microservice later without redesigning its business contract.

## Building-block policy

Shared building blocks must remain small and domain-neutral. Appropriate examples include:

- Strongly typed identifier primitives.
- Correlation and causation context.
- Common result and problem-detail types.
- Transactional outbox/inbox infrastructure.
- OpenTelemetry configuration.
- Testing fixtures and container helpers.

Do not place these in shared building blocks:

- Customer, account, payment, loan, workflow, or ledger domain models.
- Shared EF Core entities.
- A generic business-service layer.
- A universal repository hiding domain-specific queries.
- Cross-service database access.

Duplication is preferable to accidental domain coupling when the concepts only look similar.

## Database topology

### Ownership model

Each microservice owns a separate PostgreSQL database and separate database credentials.

```text
PostgreSQL cluster or managed server
├── core_ledger_db
├── customer_onboarding_db
├── identity_access_db
├── payments_vtu_db
├── channels_engagement_db
├── operations_workflow_db
├── risk_reporting_db
├── integration_hub_db
└── audit_reconciliation_db
```

Development may run these databases on one local PostgreSQL instance. Production placement may use separate clusters according to security, resilience, performance, and recovery requirements. Database separation is a logical ownership rule even when physical infrastructure is shared.

The Core Banking and Ledger database receives the strongest access restrictions. No frontend, provider connector, workflow process, reporting process, or other microservice receives write access to its tables.

### Cross-service data access

- Services communicate through versioned APIs and durable events.
- A service must not join across another service's database.
- Reporting uses approved read models, replicas, or event-fed analytical stores.
- Cross-service workflows store local references rather than database foreign keys to another service.
- Eventual consistency must be represented explicitly in states and user-facing outcomes.
- Reconciliation detects and repairs inconsistent external or replicated views without rewriting ledger history.

## Logical database structures

The following tables are starting structures, not final migration definitions. Columns, constraints, indexes, retention, and partitions must be designed from approved use cases and expected volume.

### 1. Core Banking and Ledger database

Database: `core_ledger_db`

Suggested schemas:

- `institution` - banking configuration owned by the core service.
- `product` - product definitions, versions, pricing and eligibility.
- `account` - customer accounts, restrictions, holds and liens.
- `ledger` - ledger accounts, journals, lines and accounting mappings.
- `processing` - EOD/BOD, periods, batches, schedules and checkpoints.
- `integration` - local outbox, inbox and idempotency state.

Key tables:

```text
institution.institutions
institution.legal_entities
institution.branches
institution.business_calendars
institution.holidays
institution.processing_dates
institution.supported_currencies
institution.configuration_versions

product.account_products
product.product_versions
product.eligibility_rules
product.interest_rules
product.fee_rules
product.tax_rules
product.limit_rules
product.accounting_event_mappings

account.accounts
account.account_status_history
account.holds
account.liens
account.blocks
account.dormancy_history
account.statement_sequences

ledger.ledger_accounts
ledger.journal_entries
ledger.journal_lines
ledger.financial_transactions
ledger.reversal_links
ledger.account_balances
ledger.balance_movements
ledger.accounting_periods
ledger.manual_journal_requests

processing.interest_accruals
processing.posting_batches
processing.scheduled_instructions
processing.end_of_day_runs
processing.end_of_day_steps
processing.job_checkpoints

integration.idempotency_records
integration.outbox_messages
integration.inbox_messages
```

Critical constraints:

- Every posted journal entry must have balanced debit and credit totals within its accounting scope and currency.
- Posted journal entries and lines are append-only.
- A reversal references the original financial transaction and creates new journal lines.
- Scoped idempotency keys are unique.
- Account numbers are unique within their configured institution scope.
- Balances cannot be treated as authoritative unless derived and maintained through approved ledger posting.
- Product and financial configuration is versioned and effective-dated.

### 2. Customer and Onboarding database

Database: `customer_onboarding_db`

Suggested schemas: `party`, `customer`, `kyc`, `onboarding`, `integration`.

Key tables:

```text
party.parties
party.individuals
party.organisations
party.contacts
party.addresses
party.relationships
party.directors
party.shareholders
party.beneficial_owners
party.signatories

customer.customers
customer.customer_identifiers
customer.customer_segments
customer.customer_status_history
customer.service_preferences
customer.relationship_managers
customer.duplicate_candidates
customer.merge_requests

kyc.kyc_cases
kyc.kyc_requirements
kyc.identity_verifications
kyc.screening_references
kyc.document_references
kyc.risk_classifications
kyc.review_schedules

onboarding.applications
onboarding.application_steps
onboarding.application_decisions

integration.outbox_messages
integration.inbox_messages
```

Sensitive identity values must be encrypted or tokenised according to policy and masked in normal queries, logs, and reports.

### 3. Identity and Access database

Database: `identity_access_db`

Suggested schemas: `identity`, `access`, `security`, `integration`.

Key tables:

```text
identity.identities
identity.identity_links
identity.authentication_methods
identity.sessions
identity.refresh_tokens
identity.mfa_enrolments
identity.trusted_devices
identity.service_clients

access.roles
access.permissions
access.role_permissions
access.identity_roles
access.permission_conditions
access.resource_scopes
access.branch_scopes
access.account_scopes
access.transaction_limits
access.conflict_rules
access.temporary_grants
access.access_reviews
access.entitlement_versions

security.login_events
security.lockouts
security.authentication_challenges
security.privileged_access_events

integration.outbox_messages
integration.inbox_messages
```

Passwords, PINs, OTPs, private keys, and recoverable secrets must not be stored as ordinary application data. Password verifiers use an approved adaptive hashing algorithm. Tokens and recovery codes are protected according to their threat model.

### 4. Payments and VTU database

Database: `payments_vtu_db`

Suggested schemas: `payment`, `beneficiary`, `collection`, `vtu`, `provider`, `integration`.

Key tables:

```text
payment.payment_instructions
payment.payment_attempts
payment.payment_status_history
payment.transfer_requests
payment.bulk_payment_batches
payment.bulk_payment_items
payment.scheduled_payments
payment.refund_requests
payment.reversal_requests
payment.payment_investigations

beneficiary.beneficiaries
beneficiary.beneficiary_verifications
beneficiary.cooling_periods

collection.virtual_accounts
collection.collection_references
collection.collection_receipts
collection.matching_results

vtu.service_catalogue
vtu.products
vtu.purchase_instructions
vtu.provider_results
vtu.delivery_results

provider.provider_routes
provider.provider_attempts
provider.provider_status_enquiries

integration.idempotency_records
integration.outbox_messages
integration.inbox_messages
```

This database records orchestration state, not authoritative customer balances. Payment success must be consistent with verified provider state and the resulting Core Banking and Ledger outcome.

### 5. Channels and Engagement database

Database: `channels_engagement_db`

Suggested schemas: `ussd`, `conversation`, `channel`, `cms`, `notification`, `campaign`, `integration`.

Key tables:

```text
ussd.service_codes
ussd.menu_definitions
ussd.menu_versions
ussd.menu_nodes
ussd.session_states
ussd.session_events
ussd.customer_channel_links
ussd.short_code_routes

conversation.channel_accounts
conversation.conversations
conversation.conversation_participants
conversation.messages
conversation.message_status_history
conversation.bot_intents
conversation.bot_actions
conversation.handoff_requests
conversation.agent_assignments
conversation.consent_records

channel.channel_profiles
channel.channel_policies
channel.channel_limits
channel.registered_endpoints
channel.customer_preferences

cms.content_items
cms.content_versions
cms.content_localisations
cms.content_categories
cms.content_approvals
cms.publication_schedules
cms.knowledge_articles
cms.faq_entries
cms.ussd_content_bindings
cms.chatbot_content_bindings

notification.templates
notification.template_versions
notification.notification_requests
notification.delivery_attempts
notification.delivery_status_history
notification.suppression_rules
notification.quiet_hours

campaign.campaigns
campaign.audiences
campaign.campaign_approvals
campaign.campaign_schedules
campaign.delivery_results

integration.idempotency_records
integration.outbox_messages
integration.inbox_messages
integration.webhook_receipts
```

Channel rules:

- USSD sessions are short-lived state machines with strict timeouts, replay protection and no authoritative balance storage.
- WhatsApp and website chatbots use controlled intents and application APIs; generated text never receives unrestricted access to accounts or posting operations.
- A chatbot may explain, collect input or initiate an authorised command, but the owning banking service validates identity, permission, limits and business rules.
- Sensitive or high-risk actions require step-up authentication and must not rely solely on possession of a phone number or chat account.
- CMS content is versioned, localised, approved and effective-dated before publication.
- CMS publication permissions are separate from content-author permissions, and restricted financial or security content requires maker-checker approval.
- Notification delivery state does not prove that a financial transaction succeeded.
- Conversation and campaign retention follows consent, privacy, legal-hold and institution policy.

### 6. Operations and Workflow database

Database: `operations_workflow_db`

Suggested schemas: `workflow`, `approval`, `case_management`, `integration`.

Key tables:

```text
workflow.workflow_definitions
workflow.workflow_versions
workflow.workflow_instances
workflow.workflow_steps
workflow.work_items

approval.approval_requests
approval.approval_steps
approval.approval_decisions
approval.delegations
approval.escalations

case_management.cases
case_management.case_assignments
case_management.case_notes
case_management.case_evidence_references
case_management.sla_events
case_management.disputes
case_management.complaints
case_management.override_requests

integration.outbox_messages
integration.inbox_messages
```

Workflow state does not itself prove a financial posting. A completed approval authorises a command; the ledger outcome remains authoritative.

### 7. Risk and Reporting database

Database: `risk_reporting_db`

Suggested schemas: `risk`, `screening`, `aml`, `fraud`, `reporting`, `governance`, `integration`.

Key tables:

```text
risk.rule_definitions
risk.rule_versions
risk.risk_decisions
risk.risk_scores
risk.thresholds

screening.watchlist_sources
screening.watchlist_entries
screening.screening_requests
screening.screening_results

aml.alerts
aml.investigations
aml.investigation_evidence
aml.suspicious_activity_reports

fraud.alerts
fraud.cases
fraud.device_profiles
fraud.behavioural_signals

reporting.report_definitions
reporting.report_runs
reporting.report_artifacts
reporting.regulatory_returns
reporting.submission_evidence

governance.data_assets
governance.data_lineage
governance.data_quality_rules
governance.data_quality_results

integration.outbox_messages
integration.inbox_messages
```

Reporting tables are read models. They must not become authoritative for balances, payment completion, customer identity, or ledger history.

### 8. Integration Hub database

Database: `integration_hub_db`

Suggested schemas: `provider`, `connector`, `partner`, `webhook`, `delivery`, `integration`.

Key tables:

```text
provider.providers
provider.provider_services
provider.environment_configurations
provider.credential_references
provider.certificates
provider.health_status
provider.routing_policies

connector.connector_definitions
connector.connector_versions
connector.canonical_mappings
connector.status_code_mappings

partner.api_clients
partner.client_scopes
partner.network_restrictions
partner.webhook_subscriptions
partner.certification_runs

webhook.received_events
webhook.replay_records
webhook.signature_validation_results

delivery.outbound_deliveries
delivery.delivery_attempts
delivery.dead_letters

integration.outbox_messages
integration.inbox_messages
integration.replay_requests
```

Store references to secrets and private keys, not plaintext secret material. Provider-specific models remain in adapters and are mapped to canonical contracts.

### 9. Audit and Reconciliation database

Database: `audit_reconciliation_db`

Suggested schemas: `audit`, `reconciliation`, `settlement`, `evidence`, `integration`.

Key tables:

```text
audit.audit_events
audit.sensitive_access_events
audit.configuration_change_events
audit.financial_trace_links

reconciliation.reconciliation_batches
reconciliation.source_records
reconciliation.match_rules
reconciliation.matches
reconciliation.breaks
reconciliation.break_actions
reconciliation.certifications

settlement.settlement_cycles
settlement.settlement_positions
settlement.settlement_files
settlement.settlement_entries
settlement.suspense_investigations

evidence.evidence_packages
evidence.export_requests
evidence.legal_holds

integration.outbox_messages
integration.inbox_messages
```

Audit evidence is append-only and protected from ordinary administrative modification. Reconciliation adjustments request controlled ledger postings; they do not directly update balances.

## Common database conventions

These conventions apply unless an approved service-level ADR states otherwise:

- PostgreSQL schema and table names use `snake_case`.
- C# types and members use normal .NET naming conventions.
- Primary identifiers use stable application-generated UUIDs or an approved strongly typed identifier strategy.
- Tenant-owned tables include a non-null `institution_id` and tenant-aware indexes.
- Timestamps use `timestamptz` and are represented by `DateTimeOffset` in application code.
- Business, posting, and value dates use PostgreSQL `date` and `DateOnly` where time-of-day is not part of the concept.
- Monetary values use PostgreSQL `numeric` with an explicitly configured precision and scale plus a required currency code.
- Do not use PostgreSQL `money`, C# `double`, or C# `float` for monetary values.
- Mutable aggregates use an explicit concurrency version where lost updates are possible.
- Soft deletion is not a universal default. Retention, archival, anonymisation, and legal-hold rules determine lifecycle behaviour.
- Unique constraints enforce idempotency, business references, event identities, and other invariants that must survive concurrency.
- Every service has its own EF Core migration history and migration assembly.

PostgreSQL row-level security may be used as defence in depth, but it does not replace application-level tenant authorisation. The exact tenant isolation model requires an ADR and security review before implementation.

## Roles and permissions model

The platform uses fine-grained role-based access control with contextual permission checks. Authentication proves identity; it does not grant unrestricted access. Every protected endpoint, command, query, workflow action, CMS operation, channel action and administrative function declares and enforces a permission.

### Ownership and enforcement

- Identity and Access owns permission definitions, roles, role-permission assignments, identity-role assignments, scopes, conflicts, temporary grants and access reviews.
- Each business microservice owns the authorisation rules for its resources and enforces permissions locally.
- API gateways, frontends, USSD menus and chatbots may hide unavailable actions, but they are never the enforcement boundary.
- Every service denies access by default when permission or scope cannot be proven.
- Service identities use separate service permissions and cannot inherit human roles.

### Permission naming

Permissions use stable, capability-oriented names:

```text
institution.branch.create
customer.profile.view
customer.kyc.approve
account.restriction.apply
ledger.journal.post
payment.transfer.initiate
payment.transfer.approve
workflow.override.execute
channel.ussd.menu.publish
channel.whatsapp.conversation.handle
cms.content.create
cms.content.approve
cms.content.publish
audit.event.view
reconciliation.break.resolve
```

Application code checks permissions, not hard-coded role names. Roles are configurable bundles of permissions such as Teller, Branch Supervisor, Customer Service Officer, Compliance Analyst, Content Author, Content Approver, Chat Support Agent or Reconciliation Officer.

### Contextual scopes

A permission may be constrained by:

- Institution and legal entity.
- Region and branch.
- Channel, including web, mobile, USSD, WhatsApp, agent or partner API.
- Customer, account, product or corporate mandate.
- Currency, amount and transaction limit.
- Workflow stage and maker-checker role.
- Time-limited delegation or temporary access.
- Risk, device, network or step-up-authentication context.

Possessing a permission without satisfying its required scope does not authorise the action.

### Segregation of duties

- A maker cannot approve the same restricted action.
- Permission-conflict rules prevent prohibited combinations.
- Multi-level and parallel approvals are supported where configured.
- CMS author, approver and publisher permissions may be separated.
- Privileged access and temporary grants require expiry, reason, approval and review.
- Overrides require an explicit permission, reason, audit evidence and additional approval where configured.

### Token and entitlement behaviour

- Tokens carry identity, institution, session and other approved claims; do not place an unbounded permission catalogue or sensitive business data in tokens.
- Permission changes must become effective within an approved revocation window.
- Use an entitlement version or equivalent invalidation mechanism so services can reject stale authorisation state.
- Local permission read models may be distributed through versioned events and caches, but Identity and Access remains authoritative for grants.
- Cache failure or stale state must fail safely for privileged or financial operations.

### Authorisation evidence

For sensitive decisions, record the identity, role, permission, resource scope, institution, decision, reason, policy/version, timestamp and correlation identifier. Do not log secrets or unnecessary personal data.

## API conventions

- External HTTP APIs are versioned.
- OpenAPI is generated and reviewed as a compatibility contract.
- JSON field naming uses one repository-wide policy.
- All requests carry or receive a correlation identifier.
- Financial commands require a scoped idempotency key.
- Authentication and role/permission authorisation are enforced server-side for every protected operation.
- Expected errors use RFC 7807 problem details with stable machine-readable error codes.
- Collection endpoints use bounded pagination.
- Asynchronous operations return an explicit resource or operation status rather than pretending to be final.
- Sensitive values are masked and internal exception details are never returned.

## Event conventions

All integration events include:

```text
event_id
event_type
schema_version
occurred_at
institution_id
correlation_id
causation_id
producer
business_reference
payload
```

Rules:

- Event names describe completed facts in the past tense.
- Events are published through a transactional outbox after source-state commit.
- Consumers are idempotent and use inbox/deduplication state.
- Contracts are versioned and owned by the producing service.
- Events contain the minimum data required by consumers and exclude secrets and unnecessary PII.
- Retry, backoff, dead-letter, replay, and poison-message behaviour is documented for each consumer.
- Ordering is guaranteed only within the smallest required business key, never assumed globally.

## Primary cross-service flows

### Customer onboarding

```text
Channel
  -> Customer and Onboarding
  -> Integration Hub for identity/KYC verification
  -> Risk and Reporting for screening/risk decision
  -> Operations and Workflow when human approval is required
  -> Customer and Onboarding activates customer
  -> CustomerActivated event
  -> Core Banking and Ledger opens approved account
  -> Audit and Reconciliation receives trace evidence
```

### Payment execution

```text
Channel
  -> Payments and VTU creates idempotent payment instruction
  -> Identity and Access / local authorisation checks
  -> Operations and Workflow when approval is required
  -> Core Banking and Ledger performs authoritative debit/reservation
  -> Integration Hub sends canonical request to provider
  -> Payments and VTU records provider outcome
  -> Core Banking and Ledger posts completion or controlled reversal
  -> Audit and Reconciliation matches provider, payment, and ledger records
```

A provider timeout produces an uncertain payment state and status enquiry. It must not cause blind resubmission or a second customer debit.

### Configuration publication

```text
Administrator
  -> owning service creates draft configuration
  -> Operations and Workflow obtains maker-checker approval
  -> owning service publishes effective-dated version
  -> configuration event is emitted
  -> affected services refresh approved local views
  -> Audit and Reconciliation records evidence
```

### USSD request

```text
USSD provider
  -> Integration Hub verifies provider request and maps canonical envelope
  -> Channels and Engagement loads the versioned menu and session state
  -> Identity and Access resolves the customer/channel identity and permissions
  -> Channels and Engagement invokes the owning business service
  -> owning business service validates permission, scope, limits and business rules
  -> Channels and Engagement formats a masked, session-safe response
  -> Integration Hub returns the provider-specific response
  -> Audit and Reconciliation receives trace evidence for sensitive actions
```

USSD session state is not a transaction outcome. A timed-out session must not automatically retry a financial command without its original idempotency identity and an authoritative status check.

### WhatsApp chatbot request

```text
WhatsApp provider
  -> Integration Hub verifies webhook signature and replay identity
  -> Channels and Engagement resolves the conversation and approved bot intent
  -> Identity and Access evaluates channel identity, permission and step-up requirements
  -> chatbot either answers from approved CMS/knowledge content,
     requests human handoff, or invokes a controlled business API
  -> owning service validates permission, ownership, limits and business rules
  -> Channels and Engagement stores the conversation outcome and queues response
  -> Integration Hub sends the provider-specific message
```

The chatbot must not translate unrestricted natural-language output directly into privileged commands. Financial or sensitive actions use allow-listed intents, explicit confirmation, idempotency and step-up authentication.

### CMS publication

```text
Content Author with cms.content.create
  -> Channels and Engagement creates a draft version
  -> Content Approver with cms.content.approve reviews the draft
  -> Content Publisher with cms.content.publish schedules or publishes it
  -> publication event invalidates channel caches
  -> USSD, WhatsApp, web and notification modules consume the approved version
  -> Audit and Reconciliation records author, approver, publisher and version
```

## Shared infrastructure

| Capability | Purpose | Authoritative? |
| --- | --- | --- |
| PostgreSQL | Transactional state owned by each service | Yes, within the owning service |
| Kafka-compatible broker | Durable business events and asynchronous integration | Events are evidence; ownership remains with producer state |
| Redis | Cache, rate limit and short-lived coordination | No |
| Object storage | Documents, CMS assets, statements, settlement files and evidence | Authoritative only for the owned artifact, not balances |
| Secrets/key management | Credential references, rotation and cryptographic keys | Yes for managed secret material |
| OpenTelemetry pipeline | Logs, metrics and traces | No |
| Search platform | Customer and operational search | No |
| Analytical store | Reporting and governed analytics | No |

## Security boundaries

- Internet and partner traffic enters through an approved gateway or backend-for-frontend.
- Customer, staff, corporate, agent, partner, and service identities have distinct policies where risk differs.
- Every protected operation requires an explicit permission and all applicable institution, branch, channel, account, mandate and transaction-limit scopes.
- Service-to-service calls use short-lived, rotated credentials and explicit audience/scope checks.
- Tenant identity comes from a trusted authentication or service context, not only a request body.
- Secrets and private keys remain in approved secret/key management.
- Sensitive data is encrypted in transit and at rest and masked in logs, traces, reports, and ordinary UI responses.
- Privileged actions, approvals, overrides, sensitive-data access, and manual financial work generate immutable audit evidence.
- Production data is prohibited from development and test environments unless formally approved and irreversibly masked.

## Observability

Every request and financial workflow must be traceable across services using correlation and causation identifiers.

Each microservice provides:

- Structured logs with stable event names and no prohibited sensitive data.
- Distributed traces for HTTP, messaging, database, workflow, ledger, and provider operations.
- Technical metrics for latency, errors, saturation, queue depth, retry and dead-letter state.
- Business metrics for financial exceptions, uncertain payments, reconciliation breaks, failed approvals, EOD state, and provider health.
- Separate liveness and readiness endpoints that do not expose sensitive details.
- Alerts and runbooks for actionable failure conditions.

## Testing structure

| Test type | Purpose |
| --- | --- |
| Unit | Domain invariants, calculations, policies and state transitions |
| Integration | EF Core mappings, migrations, PostgreSQL behaviour, broker, Redis and storage adapters |
| Architecture | Project dependencies, bounded-context isolation and forbidden references |
| Contract | OpenAPI, event compatibility and provider-adapter expectations |
| End-to-end | Critical multi-service customer and financial journeys |
| Security | Tenant isolation, authorisation, injection, secret leakage and abuse cases |
| Performance | Posting, payment, batch, reporting and concurrency targets |
| Resilience | Timeouts, retries, replay, failover, recovery and partial failure |

Financial test suites include balanced posting, insufficient funds, concurrent debit, duplicate idempotency keys, reversal traceability, provider uncertainty, replay, outbox failure, cross-tenant denial, currency mismatch, rounding, business-date boundaries, and reconciliation.

## Configuration model

- Configuration is strongly typed and validated at startup.
- Environment-specific values come from approved configuration providers.
- Secrets are referenced from secret management, never committed to source control.
- Financial behaviour configuration is versioned, approved, effective-dated, and auditable.
- Feature flags cannot bypass security, accounting, audit, or tenant isolation.
- Every environment has explicit provider endpoints and credentials.
- Development defaults are safe and use synthetic data.

## Deployment model

Each of the nine microservices is independently buildable and deployable. A service deployment contains its API and only the workers that belong to the same ownership and release boundary.

Deployment expectations:

- Container images are immutable and versioned.
- Database migrations run through a controlled deployment step, not opportunistically from every application replica.
- Services support graceful shutdown, health checks, and safe restart.
- Stateless API replicas scale horizontally.
- Background consumers coordinate ownership without duplicate outcomes.
- Infrastructure and configuration are version-controlled.
- Promotion requires build, tests, security checks, approval, and retained evidence.
- Rollback or forward-fix behaviour is documented for code, configuration, contracts, and migrations.

## Documentation structure

Use these locations as the repository grows:

```text
docs/adr/             Architecture decisions and superseded decisions
docs/implementation/  Phase overviews and executable epic instructions
docs/requirements/    Requirement traceability and release allocation
docs/accounting/      Chart of accounts, posting matrices and financial rules
docs/api/             API conventions and generated/curated contracts
docs/events/          Event catalogue, schemas, ownership and consumers
docs/database/        Per-service ERDs, data dictionaries and retention
docs/security/        Threat models, data classification and security design
docs/runbooks/        Deployment, incident, recovery, reconciliation and EOD procedures
```

Each microservice also owns a local `README.md` and `AGENTS.md` describing its responsibilities, dependencies, commands, contracts, failure modes, data ownership, tests, and domain-specific invariants.

## Requirement traceability

Material behaviour follows this chain:

```text
SRS requirement
  -> user story and acceptance criteria
  -> owning microservice/module
  -> ADR or design
  -> API/event/database change
  -> implementation
  -> automated tests
  -> operational evidence
```

The traceability matrix should record at least requirement ID, story ID, release phase, owning service, API/event, database objects, test cases, approval status, and evidence link.

## Recommended implementation order

1. Repository build foundation and CI.
2. Architecture tests and shared technical building blocks.
3. Local PostgreSQL, broker, Redis, secret-development substitute, and observability environment.
4. Institution configuration and tenant context inside Core Banking and Ledger.
5. Identity and Access foundation.
6. Customer and Onboarding.
7. Core accounts, products, and protected ledger posting.
8. Operations and Workflow approval foundation.
9. Payments and VTU orchestration.
10. Channels and Engagement foundation, then USSD, CMS and WhatsApp modules.
11. Integration Hub provider adapters.
12. Audit and Reconciliation.
13. Risk and Reporting read models and controls.
14. End-to-end, resilience, performance, recovery, and security verification.

Implement a thin walking skeleton before broad feature coverage: one authenticated, tenant-aware request should persist owned data, emit an outbox event, be consumed idempotently, produce audit evidence, and be traceable end to end.

## Required ADRs before production implementation

Create and approve at least these ADRs:

1. C#/.NET-only backend and SDK support policy.
2. Nine initial microservices and progressive decomposition.
3. Monorepo and solution/project organisation.
4. Database-per-service and production tenant-isolation model.
5. Ledger consistency, balance representation, concurrency, and posting transaction boundary.
6. API versioning and problem-details conventions.
7. Event envelope, broker, outbox/inbox, ordering, and replay.
8. Authentication provider, token model, service identity, and authorisation enforcement.
9. Role, permission, contextual scope, entitlement invalidation, maker-checker and segregation-of-duties model.
10. USSD, WhatsApp chatbot, CMS, human handoff and channel-security boundaries.
11. Secret/key management and cryptographic boundaries.
12. Observability, audit evidence, PII redaction, and retention.
13. Deployment platform, migration execution, backup, restore, RTO, and RPO.
14. Reporting/read-model architecture and regulatory data lineage.

## Decisions still requiring stakeholder approval

The following must be resolved rather than guessed during implementation:

- Exact supported .NET SDK version and upgrade policy.
- Hosting platform and production environment topology.
- Physical PostgreSQL isolation and tenant-isolation model.
- Identity provider and licensing requirements.
- Kafka-compatible broker implementation and managed-service choice.
- Service-to-service authentication mechanism.
- Institution-specific chart of accounts and accounting-event mappings.
- Interest, fee, tax, rounding, accrual, EOD, and reversal policies.
- NIBSS and provider message specifications, credentials, certification, and SLAs.
- Production load, capacity, availability, retention, RTO, and RPO targets.
- Country-pack and regulatory reporting rules.
- Data migration and go-live cutover strategy.

Until approved, represent these as explicit open decisions in ADRs, work items, and tests. Do not hide assumptions inside code.

ADR identifiers, statuses, and the mapping of the required decisions above to `ADR-0001` to `ADR-0014` are maintained in [`docs/adr/README.md`](../adr/README.md). Epic documents reference ADRs by identifier or by topic name until an identifier is assigned.
