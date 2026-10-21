---
phase: "Phase 1 - Banking Foundation"
epic: "Institution and Tenancy"
owning_service: "Core Banking and Ledger"
participating_services:
  - Identity and Access
  - Integration Hub
  - Operations and Workflow
  - Audit and Reconciliation
modules:
  - Institution Configuration
  - Legal Entities
  - Branches and Regions
  - Business Calendar
  - Configuration Lifecycle
  - Tenant Provisioning
status: In progress
requirements: [CFG-001, CFG-002, CFG-003, CFG-004, CFG-005, CFG-006, CFG-007, CFG-008, ARC-004, ARC-006, ARC-007, DAT-005, DAT-007, DAT-008, AUD-001, AUD-002]
stories: [US-001, US-002, US-003, US-004]
dependencies:
  - Phase 0 - Repository and Build Foundation
  - Phase 0 - Local Infrastructure
  - Phase 0 - Identity and Permission Foundation
  - Phase 0 - Messaging, Outbox, Inbox and Idempotency
  - Phase 0 - Observability, Audit and Continuous Integration
adrs: [ADR-0002, ADR-0004, ADR-0007, ADR-0008, ADR-0009]
last_updated: 2026-10-19
---

# Epic: Institution and Tenancy

## 1. Objective

Create the reusable multi-institution foundation that provisions a financial institution, isolates its data and operational configuration, manages legal entities and branches, establishes its calendar and business date, and publishes approved configuration without source-code changes.

The epic must produce an institution that is either safely provisioned and active or visibly incomplete and unusable. It must not leave a partially provisioned institution able to process customer or financial requests.

## 2. Success measures

- A Platform Super Administrator can create an institution through an idempotent, audited workflow.
- The institution receives a stable identifier, legal profile, country-pack assignment, base currency, branding reference and isolated configuration boundary.
- All mandatory microservices initialise their local institution boundary from a durable event without cross-database writes.
- Branches, regions, currencies, holidays, working days and processing dates are configurable under explicit permissions.
- Business date remains distinct from transaction timestamp, posting date and value date.
- Configuration uses draft, simulation, approval, effective-date, publication and rollback lifecycles.
- Provider credentials, certificates and secrets are represented by managed references and remain isolated by institution and environment.
- Cross-institution access tests pass at API, application and persistence boundaries.
- Every privileged or configuration-changing action produces immutable audit evidence.

## 3. Scope

### Included

- Institution tenant creation and lifecycle.
- Institution legal name, type, licence profile, country pack, base currency and branding references.
- Legal entities owned by an institution.
- Regions and branches with activation/suspension lifecycle.
- Supported currencies and institution defaults.
- Holidays, working days, processing dates and business-date management.
- Versioned, effective-dated configuration framework.
- Feature flags, product-eligibility references, fee/tax/limit/policy configuration envelopes.
- Configuration simulation metadata and impact report contracts.
- Maker-checker approval, scheduling, publication and rollback.
- Per-environment API client, network restriction, certificate, webhook-secret and provider-credential references.
- Institution retention, residency, archival and legal-hold policy references.
- Tenant-provisioning events and local initialisation acknowledgements.
- Audit, metrics, traces, alerts and operational repair for provisioning/configuration.

### Excluded

- Detailed deposit-product, fee, tax, interest and accounting-rule semantics; owned by later product and ledger epics.
- Final country-pack regulatory content; this epic stores assignment, version and effective date.
- Production provider onboarding and certification; owned by Integration Hub and provider epics.
- Production role catalogue; owned by Identity, Roles and Permissions.
- Customer, account or payment data.
- Automated database-per-tenant physical provisioning unless approved by the tenant-isolation ADR.
- Institution deletion after activation. Decommissioning requires a separate retention, legal-hold and migration design.

## 4. Ownership boundaries

### Core Banking and Ledger

Owns the authoritative institution identity and banking configuration:

- Institution status and legal profile.
- Legal entities, regions and branches.
- Country-pack assignment and base currency.
- Business calendar and business date.
- Institution configuration versions and publication state.
- Banking feature flags and references to product/policy configuration.
- Provisioning workflow state and required-service acknowledgements.

### Integration Hub

Owns environment-specific integration configuration:

- Provider definitions and enabled services.
- API clients and approved scopes.
- Network restrictions.
- Certificate and key references.
- Webhook secret references.
- Provider credential references and expiry metadata.
- Provider endpoint/environment mappings.

Secret material remains in approved secret/key management. Neither service stores plaintext private keys, passwords or provider secrets.

### Identity and Access

- Authenticates platform and institution administrators.
- Owns roles, permissions, assignments, contextual scopes and entitlement versions.
- Initialises institution-scoped administrative boundaries after provisioning events.

### Operations and Workflow

- Owns maker-checker tasks and approval decisions for restricted configuration actions.
- Does not directly publish or mutate authoritative institution configuration.

### Audit and Reconciliation

- Stores immutable evidence and trace links.
- Does not become the authoritative configuration store.

## 5. Actors, roles and permissions

Role names are examples. Application code checks permissions.

| Actor | Permission | Scope | Step-up | Approval |
| --- | --- | --- | --- | --- |
| Platform Super Administrator | `platform.institution.create` | Platform | Required | Optional launch policy |
| Platform Super Administrator | `platform.institution.activate` | Institution | Required | Maker-checker required |
| Institution Administrator | `institution.profile.view` | Institution | No | None |
| Institution Administrator | `institution.profile.update` | Institution | Conditional | Maker-checker for restricted fields |
| Institution Administrator | `institution.legal_entity.manage` | Institution | Conditional | Policy-driven |
| Institution Administrator | `institution.region.manage` | Institution | No | Policy-driven |
| Institution Administrator | `institution.branch.create` | Institution/region | Conditional | Policy-driven |
| Institution Administrator | `institution.branch.activate` | Institution/branch | Required | Maker-checker required |
| Institution Administrator | `institution.branch.suspend` | Institution/branch | Required | Maker-checker required |
| Calendar Administrator | `institution.calendar.manage` | Institution | Conditional | Maker-checker required for publication |
| Operations Supervisor | `institution.business_date.advance` | Institution | Required | Maker-checker required |
| Product Administrator | `institution.configuration.create` | Institution/configuration area | Conditional | None for draft |
| Product Administrator | `institution.configuration.simulate` | Institution/configuration area | Conditional | None |
| Configuration Approver | `institution.configuration.approve` | Institution/configuration area | Required | Cannot approve own draft |
| Configuration Publisher | `institution.configuration.publish` | Institution/configuration area | Required | Approved version only |
| Configuration Administrator | `institution.configuration.rollback` | Institution/configuration area | Required | Maker-checker required |
| Integration Administrator | `integration.provider.configure` | Institution/environment/provider | Required | Maker-checker required |
| Integration Administrator | `integration.api_client.register` | Institution/environment | Required | Maker-checker required |
| Security Administrator | `integration.credential_reference.manage` | Institution/environment/provider | Required | Segregated from ordinary viewing |
| Records Administrator | `institution.retention_policy.manage` | Institution/record class | Required | Compliance approval required |
| Auditor | `institution.configuration.audit.view` | Institution | Required for sensitive evidence | Read-only |

Rules:

- Platform roles do not automatically receive institution banking permissions.
- Institution administrators cannot access another institution.
- Credential-reference permissions do not grant access to secret material.
- A maker cannot approve or publish their own restricted configuration.
- Temporary grants require expiry, reason and approval.
- Restricted fields and permission combinations are defined by approved policy, not hard-coded role names.

## 6. Domain model

### Aggregates

| Aggregate | Responsibility | Key invariants |
| --- | --- | --- |
| Institution | Stable tenant identity, legal profile, status and primary configuration references | Unique ID; immutable identity; valid lifecycle; one base currency at activation |
| LegalEntity | Licensed or operational legal entity within institution | Belongs to one institution; unique approved code within institution |
| Region | Organisational grouping for branches | Institution-scoped; cannot be removed while active branches depend on it |
| Branch | Branch identity, region, status and operating metadata | Unique code within institution; legal state transitions |
| BusinessCalendar | Working days, holidays, timezone and calendar versions | Versioned; non-overlapping effective periods; institution-scoped |
| BusinessDateControl | Current and pending business dates with controlled advancement | Monotonic except approved recovery; distinct from timestamps/value dates |
| ConfigurationSet | Versioned collection of configuration values for one area | Draft is mutable; approved/published version is immutable |
| ConfigurationPublication | Approval, effective date, publication and rollback evidence | Approved version only; one effective version per scope/time |
| TenantProvisioning | Required-service initialisation and activation state | Institution cannot activate until mandatory steps succeed |

### Integration Hub aggregates

| Aggregate | Responsibility | Key invariants |
| --- | --- | --- |
| ProviderConfiguration | Provider/service/environment settings | Institution and environment scoped; versioned; no plaintext secret |
| ApiClientRegistration | Partner/service client metadata, scopes and restrictions | Unique client ID; approved scopes; revocable; environment scoped |
| CredentialReference | Reference and lifecycle metadata for managed secret/certificate | No secret value; owner and expiry required |

### Value objects

- `InstitutionId`, `LegalEntityId`, `RegionId`, `BranchId`.
- `InstitutionCode`, `BranchCode`, `CountryPackCode`.
- `CurrencyCode` using approved ISO currency values.
- `BusinessDate`, `EffectivePeriod`, `ConfigurationVersion`.
- `EnvironmentName`, `ProviderCode`, `SecretReference`, `CertificateReference`.
- `NetworkRestriction`, `BrandingReference`, `LicenceProfile`.

## 7. Institution state transitions

```text
Draft
  -> Provisioning
      -> ProvisioningFailed
          -> Provisioning
      -> PendingActivation
          -> Active
Active
  -> Suspended
      -> Active
Active or Suspended
  -> Decommissioning     # future approved epic only
```

Rules:

- `Draft` may be corrected before provisioning.
- `Provisioning` emits durable requests/events to mandatory services.
- `ProvisioningFailed` remains unusable and exposes repair status.
- `PendingActivation` requires all mandatory initialisation acknowledgements.
- Activation is a maker-checker action and emits `InstitutionActivated`.
- Suspended institutions cannot initiate ordinary customer/financial operations, but authorised recovery, audit and support paths remain available.
- Hard deletion is prohibited after provisioning begins.

## 8. Tenant provisioning workflow

```text
Platform Super Administrator
  -> submit idempotent CreateInstitution command
  -> Core Banking and Ledger validates uniqueness and country/base-currency inputs
  -> local transaction creates Draft/Provisioning institution and outbox event
  -> InstitutionProvisioningStarted is published
  -> each mandatory service creates its local institution boundary idempotently
  -> each service publishes InstitutionBoundaryInitialised or failure evidence
  -> Core Banking and Ledger records acknowledgements
  -> institution reaches PendingActivation when all required acknowledgements exist
  -> independent approver activates institution
  -> InstitutionActivated is published
```

Do not use a distributed database transaction. Partial provisioning is represented explicitly and repaired through idempotent retry. No partially provisioned institution may process ordinary banking requests.

Required initialisation by service must be configurable and versioned for the deployment. At minimum, every service creates the institution reference/read model needed to reject unknown or inactive tenants.

## 9. Branch lifecycle

```text
Draft -> Active -> Suspended -> Active
                  -> Closed
```

- Branch creation requires institution and region ownership validation.
- Activation may require approved calendar, currency and operational configuration.
- Suspension blocks new branch-scoped operations according to policy but preserves history.
- Closure is terminal and requires dependency checks and approval.
- Branch code reuse is prohibited unless an explicit archival policy permits it.

## 10. Business calendar and date model

Store and expose these concepts separately:

- Current UTC/offset-aware transaction timestamp.
- Institution business date.
- Posting date assigned by the ledger.
- Value date used for financial effect.
- Calendar timezone.
- Working day and holiday classification.
- Processing-day status and EOD/BOD checkpoint.

Business-date advancement:

1. Requires an idempotent command.
2. Validates the approved calendar version and processing prerequisites.
3. Requires maker-checker approval.
4. Records old date, new date, reason, actor and workflow evidence.
5. Publishes `BusinessDateAdvanced` after commit.
6. Cannot silently skip or move backwards; exceptional correction requires a separate approved recovery action.

## 11. Configuration lifecycle

```text
Draft
  -> Simulated
  -> PendingApproval
      -> Rejected -> Draft
      -> Approved
          -> Scheduled
          -> Published
              -> Superseded
              -> RolledBackByNewVersion
```

Rules:

- Draft values may change and carry an explicit version.
- Simulation is non-authoritative and records inputs, assumptions, output and warnings.
- Submission freezes the reviewed content hash/version.
- Approval applies to that exact immutable version.
- Publication may be immediate or scheduled by effective date.
- Published configuration is immutable.
- Rollback creates and approves a new version based on a prior version; it does not delete history.
- Only one version is effective for a configuration area and scope at a point in time.
- Consumers use version identifiers and effective periods, not an unversioned mutable configuration blob.

Configuration areas include feature flags, product-eligibility references, fee/tax/limit/policy envelopes, calendars, retention/residency rules and other institution-owned configuration. Detailed business semantics remain in the owning later epic.

## 12. API contracts

Routes are illustrative and require API ADR approval.

| Method | Route | Permission | Purpose | Idempotent |
| --- | --- | --- | --- | --- |
| POST | `/api/v1/institutions` | `platform.institution.create` | Create and begin provisioning | Required |
| GET | `/api/v1/institutions/{institutionId}` | `institution.profile.view` | View permitted institution profile | N/A |
| PATCH | `/api/v1/institutions/{institutionId}` | `institution.profile.update` | Create controlled profile change | Required |
| POST | `/api/v1/institutions/{institutionId}/activate` | `platform.institution.activate` | Activate fully provisioned institution | Required |
| POST | `/api/v1/institutions/{institutionId}/suspend` | `platform.institution.suspend` | Suspend institution | Required |
| POST | `/api/v1/institutions/{institutionId}/regions` | `institution.region.manage` | Create region | Required |
| POST | `/api/v1/institutions/{institutionId}/branches` | `institution.branch.create` | Create branch | Required |
| POST | `/api/v1/branches/{branchId}/activate` | `institution.branch.activate` | Activate approved branch | Required |
| POST | `/api/v1/branches/{branchId}/suspend` | `institution.branch.suspend` | Suspend branch | Required |
| POST | `/api/v1/institutions/{institutionId}/calendars/versions` | `institution.calendar.manage` | Create calendar version | Required |
| POST | `/api/v1/institutions/{institutionId}/business-date/advance` | `institution.business_date.advance` | Request controlled advancement | Required |
| POST | `/api/v1/institutions/{institutionId}/configurations/{area}/versions` | `institution.configuration.create` | Create configuration draft | Required |
| POST | `/api/v1/configurations/{versionId}/simulate` | `institution.configuration.simulate` | Produce non-authoritative simulation | Required |
| POST | `/api/v1/configurations/{versionId}/submit` | `institution.configuration.submit` | Submit exact version for approval | Required |
| POST | `/api/v1/configurations/{versionId}/publish` | `institution.configuration.publish` | Publish approved version | Required |
| POST | `/api/v1/configurations/{versionId}/rollback` | `institution.configuration.rollback` | Start controlled rollback version | Required |

Integration Hub exposes separate versioned administration APIs for provider configurations, credential references and API clients. Core Banking must not proxy secret material.

### Stable error codes

At minimum:

```text
institution_code_conflict
institution_not_provisioned
institution_inactive
cross_tenant_access_denied
branch_code_conflict
branch_transition_invalid
calendar_version_conflict
business_date_prerequisite_failed
business_date_transition_invalid
configuration_version_conflict
configuration_not_approved
configuration_effective_period_overlap
maker_cannot_approve_own_change
credential_reference_invalid
idempotency_key_reused_with_different_request
```

## 13. Event contracts

### Produced by Core Banking and Ledger

| Event | Trigger | Ordering key | Primary consumers |
| --- | --- | --- | --- |
| `InstitutionProvisioningStarted.v1` | Institution committed in provisioning state | Institution ID | All services |
| `InstitutionActivated.v1` | Approved activation | Institution ID | All services |
| `InstitutionSuspended.v1` | Approved suspension | Institution ID | All services |
| `InstitutionProfileChanged.v1` | Published profile version | Institution ID | Identity, Integration, Audit, channels |
| `RegionCreated.v1` | Region committed | Institution ID | Relevant read models |
| `BranchCreated.v1` | Branch committed | Institution ID | Identity, Customer, Operations, Audit |
| `BranchStatusChanged.v1` | Approved transition | Branch ID | All branch-aware consumers |
| `BusinessCalendarPublished.v1` | Calendar version becomes effective | Institution ID | Ledger, Payments, Workflow, channels |
| `BusinessDateAdvanced.v1` | Approved date advancement commits | Institution ID | All date-aware services |
| `InstitutionConfigurationPublished.v1` | Approved configuration becomes effective | Institution/configuration area | Affected consumers |

### Consumed by Core Banking and Ledger

| Event | Producer | Behaviour |
| --- | --- | --- |
| `InstitutionBoundaryInitialised.v1` | Each mandatory service | Record idempotent provisioning acknowledgement |
| `InstitutionBoundaryInitialisationFailed.v1` | Each mandatory service | Record failure and expose repair state |
| `ApprovalDecided.v1` | Operations and Workflow | Apply decision only to matching immutable request/version |
| `CredentialReferenceValidated.v1` | Integration Hub | Record validation metadata, never secret value |

All consumers use inbox deduplication. Event replay must not create duplicate branches, configuration publication or provisioning acknowledgements.

## 14. Database design

### Core Banking and Ledger ownership

Database: `core_ledger_db`  
Schemas: `institution`, `integration`  
Migration assembly: Core Banking and Ledger Infrastructure

| Table | Purpose | Important constraints |
| --- | --- | --- |
| `institution.institutions` | Authoritative tenant identity and lifecycle | Unique institution code; status check; stable ID |
| `institution.legal_entities` | Licensed/operating legal entities | Institution-scoped unique code |
| `institution.licence_profiles` | Licence type, number/reference and validity metadata | Sensitive fields classified; versioned where required |
| `institution.country_pack_assignments` | Country-pack version and effective period | Non-overlapping periods |
| `institution.regions` | Branch grouping | Institution-scoped unique code |
| `institution.branches` | Branch identity and status | Institution-scoped unique code; concurrency version |
| `institution.branch_status_history` | Append-only branch transitions | Previous/new status, actor, reason, workflow reference |
| `institution.supported_currencies` | Enabled currencies and defaults | One base currency; approved currency codes |
| `institution.business_calendars` | Calendar identity | Institution-scoped |
| `institution.calendar_versions` | Immutable working-day and holiday versions | Unique version; effective-period checks |
| `institution.calendar_days` | Day classification | Unique calendar version/date |
| `institution.business_date_controls` | Current/pending business date | One current record per institution; concurrency version |
| `institution.business_date_history` | Append-only changes | Old/new date, reason and approval reference |
| `institution.configuration_sets` | Configuration area and scope | Institution/area/scope uniqueness |
| `institution.configuration_versions` | Draft and immutable submitted/published content | Unique version; content hash; concurrency version |
| `institution.configuration_simulations` | Non-authoritative impact results | Input version/hash and outcome |
| `institution.configuration_publications` | Approval/effective/publication evidence | Prevent overlapping effective periods |
| `institution.retention_policies` | Record-class retention/residency references | Versioned/effective-dated |
| `institution.provisioning_runs` | Provisioning lifecycle | Unique active run per institution |
| `institution.provisioning_steps` | Required-service acknowledgements | Unique institution/run/service/step |
| `integration.idempotency_records` | Command idempotency | Unique institution/operation/key |
| `integration.outbox_messages` | Durable events | Unique event ID |
| `integration.inbox_messages` | Consumed-event deduplication | Unique producer/event ID |

### Integration Hub ownership

Database: `integration_hub_db`  
Schemas: `provider`, `partner`, `integration`

| Table | Purpose | Important constraints |
| --- | --- | --- |
| `provider.environment_configurations` | Institution/provider/environment settings | Unique institution/provider/service/environment/version |
| `provider.credential_references` | Managed-secret reference metadata | No plaintext secret; expiry/owner required |
| `provider.certificates` | Certificate reference and lifecycle metadata | No private key; thumbprint/version/expiry |
| `provider.network_restrictions` | IP/network allow rules | Institution/environment scoped |
| `partner.api_clients` | API client metadata | Unique client ID; revocable; environment scoped |
| `partner.client_scopes` | Approved client permissions/scopes | No unapproved wildcard |
| `partner.webhook_registrations` | Endpoint and secret reference | Versioned; verified ownership |

## 15. Consistency, concurrency and idempotency

- Institution creation is locally atomic with its outbox record.
- Cross-service provisioning is eventually consistent and explicitly stateful.
- Every modifying API requires idempotency where indicated.
- Same idempotency key and equivalent request returns the original result.
- Same key with different request content returns a conflict.
- Concurrent branch-code or institution-code creation is resolved by database uniqueness, not pre-check alone.
- Configuration version transitions use optimistic concurrency.
- Publication and business-date advancement use database constraints/locking sufficient to prevent two effective outcomes.
- Approval decisions reference immutable version and content hash.
- Integration Hub setup is coordinated by events/workflow, never a cross-database transaction.

## 16. Tenant isolation

- All institution-owned rows include non-null `institution_id` and tenant-aware indexes.
- Application queries require trusted tenant context unless executing an explicitly authorised platform operation.
- Platform operations must state the target institution and require a platform permission.
- Institution routes do not permit caller-controlled tenant switching without re-authorisation.
- Cache keys, event partition keys, idempotency scopes, document paths and audit evidence include institution scope where applicable.
- Error responses must not reveal another institution's resource existence.
- Cross-tenant negative tests cover reads, writes, updates, events, caches and exports.
- PostgreSQL row-level security, if adopted, is defence in depth rather than the only enforcement.

## 17. Security and privacy

- Licence identifiers, network restrictions, certificate metadata and configuration content are classified.
- Secret and private-key values remain in approved secret/key management.
- Only public certificates/keys intended for distribution may be returned to clients.
- Provider and API-client changes require step-up and immutable audit evidence.
- Configuration payloads use explicit allowed-field mappings to prevent mass assignment.
- Branding assets and document references use controlled object-storage paths and content validation.
- Retention/residency configuration cannot bypass legal hold.
- Rate-limit platform and integration administration endpoints.
- Validate country/currency codes against approved reference data.

## 18. Audit and observability

### Audit events

Record at minimum:

- Institution creation, provisioning step and activation/suspension.
- Legal entity, branch and region changes.
- Calendar publication and business-date advancement.
- Configuration draft submission, simulation, approval, rejection, publication and rollback.
- Provider/API-client/credential-reference creation, rotation, revocation and expiry changes.
- Retention/residency policy changes.
- Cross-tenant and permission denials for sensitive operations.

Evidence includes actor, role/service context, permission, institution, branch if applicable, action, reason, before/after reference, version/hash, workflow decision, timestamp and correlation ID.

### Metrics

- Provisioning duration, failure count and oldest incomplete run.
- Configuration drafts pending approval and scheduled publication failures.
- Business-date advancement success/failure and processing delay.
- Credential/certificate expiry windows.
- Cross-tenant denial and sensitive permission-denial count.
- Outbox backlog and consumer failures for configuration events.

### Alerts

- Institution stuck in provisioning.
- Mandatory service initialisation failure.
- Scheduled configuration failed to publish.
- Conflicting or missing effective configuration.
- Business-date advancement failure.
- Credential/certificate approaching expiry.
- Repeated cross-tenant access attempts.

## 19. Failure, retry and recovery

- Provisioning steps are independently retryable and idempotent.
- Failed provisioning remains visible with stable reason and last-attempt evidence.
- An unavailable service does not cause institution activation; repair resumes from incomplete steps.
- Scheduled publication retries safely and alerts before/after its effective time.
- If a consumer misses an event, replay rebuilds its local institution reference without duplicate state.
- Business-date failures preserve the last authoritative date and checkpoint.
- Rollback is a new approved version; never rewrite published history.
- Backup/restore verification includes institution, branches, calendars, configuration versions, provisioning evidence and outbox/inbox state.

## 20. Configuration

| Setting | Owner | Secret | Validation | Reload |
| --- | --- | --- | --- | --- |
| Required provisioning services | Platform deployment | No | Known service identifiers | Deployment controlled |
| Default country-pack catalogue | Core Banking | No | Approved versions | Event/version based |
| Supported currency catalogue | Core Banking | No | Approved ISO list | Version based |
| Provisioning retry policy | Core Banking | No | Bounded attempts/backoff | Restart safe |
| Secret-manager reference prefix | Integration Hub | No | Environment scoped | Deployment controlled |
| Provider credentials | Secret manager | Yes | Provider/environment policy | Rotation workflow |
| Certificate references | Secret/key manager | Reference only in app DB | Expiry/usage validation | Versioned rotation |

## 21. Implementation slices

Implementation progress as of 2026-10-21: the initial slice-3 institution and tenant-provisioning lifecycle is implemented in the Core Ledger domain with a versioned `InstitutionProvisioningStarted.v1` payload and focused domain/contract tests. The configured mandatory-service list is validated at API startup. Slice 4 includes the initial `institution.institutions`, `institution.provisioning_runs` and `institution.provisioning_steps` migration, institution-scoped keys, fail-closed query filters and database readiness wiring. Slice 5 includes the application command boundary and relational service that atomically commits the institution, provisioning run/steps and `InstitutionProvisioningStarted.v1` outbox record, with durable scoped idempotency and stable conflicts. Slice 6 covers every configured mandatory service: Identity and Access, Audit Reconciliation, Customer Onboarding, Payments and VTU, Channels and Engagement, Operations Workflow, Risk and Reporting, and Integration Hub each create a non-active local institution boundary through inbox deduplication and emit durable success or controlled-failure evidence through their outbox. Core Ledger initialises its own local step atomically, listens to every configured remote-service acknowledgement topic, records exact step evidence, marks controlled failures and advances a fully acknowledged run only to `pending_activation`. The shared Kafka inbox worker supports multiple explicit topic subscriptions so services can retain one durable routing boundary. Slice 7 exposes versioned institution creation, activation and suspension HTTP commands with scoped idempotency, tenant matching, step-up capability permissions, persisted actor/reason/correlation evidence, optimistic lifecycle concurrency and durable `InstitutionActivated.v1`/`InstitutionSuspended.v1` outbox events. Every mandatory service consumes activation and suspension idempotently, transitions only its matching local boundary, and accepts a later activation as reactivation. Slice 8 provides institution-scoped regions and branches, unique codes, immutable status history, maker-checker protected transitions, active-parent dependency checks and durable creation/status events. Region and branch changes fail closed unless their institution is active. Slice 9 now includes the initial institution-scoped calendar, immutable calendar-version/day, business-date control, pending advance request and append-only history model. Calendar publication and business-date execution are separate maker-checker transitions, use durable scoped idempotency and optimistic concurrency, and emit `BusinessCalendarPublished.v1` and `BusinessDateAdvanced.v1` through the outbox. Business-date advancement accepts only the next explicitly classified processing day and records skipped non-processing dates, checkpoint, reason, actors and approval reference. Slice 10 now includes canonical JSON hashing, recursive secret-bearing field rejection, exact-content simulation evidence, immutable draft/submitted/approved/published transitions, maker-checker separation and rollback as a linked new draft. Tenant-scoped configuration sets, versions, simulations and publication evidence have EF Core mappings, fail-closed query filters, concurrency/uniqueness constraints and an append-only migration. Savings eligibility is the first approved typed area under ADR-0015. Institution-defined product codes and deterministic half-open effective periods are implemented in the domain; ambiguous or overlapping schedules fail closed, and persistence carries effective end times plus a configuration-set concurrency token for publication serialization. The source-owned continuation persists a stable Savings product identity and exposes idempotent draft creation, exact-hash simulation, Workflow submission and approved-version publication through institution-scoped capability-protected APIs with strict typed-rule rehydration. ADR-0016's initial one-level Workflow profile now has request, authenticated decision, entitlement-validation, decision-delivery and exact source-application services, making the Savings configuration lifecycle callable end to end in Development. Automatic timezone/cut-off scheduling remains intentionally inaccessible until Operations and Finance approve that policy. Non-development authentication remains fail-closed until the ADR-0008 identity-provider choice and ADR-0009 entitlement-projection delivery are configured; the synthetic actor adapter is Development-only. Downstream service-specific recovery paths beyond boundary status remain inaccessible pending the Product/Operations decision documented below.

1. Approve tenant-isolation, configuration-lifecycle and service-ownership decisions.
2. Define institution, branch, calendar and configuration contracts plus failing acceptance tests.
3. Implement Institution aggregate, states and domain invariants.
4. Add EF Core mappings, constraints and migrations for institution/branch/calendar/provisioning.
5. Implement idempotent institution creation and provisioning outbox event.
6. Implement one reference service's idempotent boundary initialisation, then apply the approved pattern to all mandatory services.
7. Implement activation and suspension with permission and maker-checker enforcement.
8. Implement regions, branches and lifecycle transitions.
9. Implement calendar versions and controlled business-date advancement.
10. Implement generic configuration version, simulation, approval, publication and rollback lifecycle.
11. Implement Integration Hub API-client and credential-reference administration.
12. Add audit, telemetry, repair operations, runbooks and full acceptance tests.

Each slice must keep inactive/unfinished features inaccessible. Do not activate an institution before all mandatory provisioning and approval checks are present.

## 22. Test plan

| Test ID | Requirement/story | Level | Scenario | Expected evidence |
| --- | --- | --- | --- | --- |
| INS-001 | CFG-001 / US-001 | Integration | Create valid institution | Unique ID, Provisioning state, outbox and audit evidence |
| INS-002 | CFG-001 / US-001 | Concurrency | Concurrent duplicate institution code | One institution, stable conflict/original result |
| INS-003 | CFG-002 / US-001 | Security | Read/update from another tenant | Denied without resource disclosure |
| INS-004 | CFG-002 | Integration | Initialise all service boundaries | One idempotent acknowledgement per service |
| INS-005 | CFG-002 | Resilience | Service unavailable during provisioning | Institution remains unusable; retry completes later |
| INS-006 | CFG-003 / US-002 | Integration | Create and activate branch | Correct region ownership, status history and audit |
| INS-007 | CFG-003 / US-002 | Security | Branch created under foreign institution region | Denied |
| INS-008 | CFG-003 / US-002 | Domain | Publish holiday/working-day version | Effective version and immutable history |
| INS-009 | CFG-004 / US-002 | Domain | Advance business date | Separate date concepts and approval evidence |
| INS-010 | CFG-004 | Concurrency | Two simultaneous date advancements | One authoritative transition |
| INS-011 | CFG-005 / US-003 | Domain | Create and simulate configuration | Non-authoritative report tied to exact version/hash |
| INS-012 | CFG-007 / US-003 | Security | Maker attempts self-approval | Denied and audited |
| INS-013 | CFG-007 / US-003 | Integration | Scheduled publication | Exact approved version becomes effective once |
| INS-014 | CFG-007 | Integration | Rollback | New approved version; prior history preserved |
| INS-015 | CFG-006 / US-004 | Security | Register credential reference | No plaintext secret stored or logged |
| INS-016 | CFG-006 / US-004 | Integration | Rotate certificate/credential reference | Version changes, expiry alert and audit evidence |
| INS-017 | CFG-008 | Domain | Apply retention/residency version | Effective-dated policy and legal-hold protection |
| INS-018 | ARC-003 | Resilience | Replay provisioning/configuration event | No duplicate state or publication |

Also test validation, pagination, stable problem details, event compatibility, migration application, backup/restore and telemetry redaction.

## 23. Acceptance criteria

- [ ] CFG-001 through CFG-008 and US-001 through US-004 are traced to implementation and tests.
- [ ] Institution creation is idempotent, audited and cross-tenant safe.
- [ ] All mandatory services initialise local tenant boundaries without shared-database writes.
- [ ] Partial provisioning cannot become active.
- [ ] Branches, regions, calendars and business dates follow controlled lifecycles.
- [ ] Configuration supports version, simulation, approval, effective date, publication and rollback.
- [ ] Provider/API-client settings are institution and environment scoped.
- [ ] Secret material never enters application databases, responses, logs or events.
- [ ] Roles, permissions, scopes and maker-checker rules are enforced server-side.
- [ ] Replay, concurrency, failure and restore tests pass.
- [ ] Runbooks cover stuck provisioning, publication failure and business-date recovery.

## 24. Definition of Done

In addition to the repository Definition of Done:

- The institution service boundary and participating-service responsibilities are documented and enforced.
- OpenAPI and event contracts are versioned.
- ERD/data dictionary and migration evidence are available.
- Provisioning and configuration dashboards/alerts are operational.
- Security review approves tenant isolation and credential-reference handling.
- Product/compliance stakeholders approve institution, calendar and retention semantics.

## 25. Open decisions

| Decision | Impact | Owner | Required before |
| --- | --- | --- | --- |
| Physical tenant-isolation model | Database provisioning, RLS, operations | Architecture/Security | Persistence implementation |
| Required service acknowledgement list | Activation and degraded operation | Architecture/Operations | Provisioning workflow approval |
| Base-currency change policy | Ledger and products | Finance/Product | Institution activation |
| Country-pack catalogue/versioning | Validation and regulatory behaviour | Compliance/Product | Production country assignment |
| Business-date timezone and cut-off | Calendar and EOD | Operations/Finance | Date advancement implementation |
| Configuration simulation semantics by area | Product/fee/limit correctness | Owning epic | Area-specific implementation |
| Secret manager and HSM integration | Provider/API-client administration | Security/Platform | Production credentials |
| Retention, residency and legal-hold policy | Data lifecycle | Legal/Compliance | Production data |
| Institution suspension behaviour by service | Safety and recovery | Product/Operations | Suspension implementation |

## 26. Prohibited assumptions

- Do not invent licence validation, country-pack content or regulatory retention periods.
- Do not assume all institutions share one timezone, currency, calendar or provider account.
- Do not allow an institution administrator to act as a platform administrator.
- Do not store secret values in configuration tables.
- Do not mutate published configuration or reuse version numbers.
- Do not advance business date from wall-clock time without an approved command and prerequisites.
- Do not activate a partially provisioned institution.
- Do not hard-delete an institution after provisioning begins.

## 27. Agent implementation instructions

- Required reading: root `AGENTS.md`, architecture document, Phase 0 evidence, this epic, CFG requirements, US-001..US-004 and applicable ADRs.
- Expected changes: Core Banking institution module, Integration Hub administration module, public contracts, migrations, tests, audit/telemetry and runbooks.
- Protected boundaries: no cross-service database writes; no secret values; no customer/account/ledger feature implementation beyond references.
- Required verification: build, formatting, analyzers, unit, architecture, integration, contract, cross-tenant, concurrency, replay and restore tests.
- Stop for a decision when tenant isolation, country-pack semantics, base-currency policy, secret management, retention, business-date recovery or activation prerequisites are unresolved.
- Final report must list requirement/story coverage, migrations, API/events, permissions, tests run, evidence, assumptions and remaining risks.

