---
phase: "Phase 1 - Banking Foundation"
epic: "Integration Hub and Providers"
owning_service: "Integration Hub"
modules:
  - Provider Registry
  - Connector Runtime
  - Canonical Contracts and Mappings
  - Credential and Certificate References
  - Webhook Intake and Dispatch
  - Provider Health and Certification
  - Outbound Delivery
status: Draft
requirements:
  - API-001
  - API-002
  - API-003
  - API-004
  - API-005
  - API-006
  - API-007
  - API-008
  - NIB-001
  - NIB-002
  - NIB-003
  - NIB-004
  - NIB-005
  - NIB-006
  - PSP-001
  - PSP-002
  - PSP-003
  - PSP-004
  - PSP-005
  - PSP-006
stories: [US-004, US-068, US-069]
dependencies:
  - Phase 0 architecture, security, secrets-management and provider decisions
  - 01 Institution and Tenancy
  - 02 Identity, Roles and Permissions
  - 03 Customer and Onboarding
  - 06 Workflow and Maker-Checker
  - 07 Payments, Collections and VTU
adrs:
  - Provider adapter and canonical contract strategy
  - Secret manager, key management and certificate lifecycle
  - Provider invocation durability and uncertainty model
  - Webhook evidence retention, replay and dead-letter model
last_updated: 2026-10-15
---

# Epic: Integration Hub and Providers

## 1. Objective

Deliver the C#/.NET Integration Hub that provides governed, tenant-isolated connectivity between internal banking services and approved external providers without leaking provider protocols, credentials or status vocabularies into business domains.

The epic establishes provider registration, environment-specific credential and certificate references, versioned connector adapters, canonical request and result contracts, durable webhook processing, outbound delivery, status enquiry, provider health, certification evidence and safe operational repair. It enables Phase 1 NIBSS, payment-service-provider, KYC, VTU/biller and communication-provider journeys while leaving provider-specific business decisions with the service that owns the journey.

The Integration Hub is not a ledger, payment engine, customer system, workflow engine, notification-template owner or reconciliation authority. It reports verified external facts and transport evidence; owning services decide what those facts mean for their domain.

## 2. Success measures

- Every enabled external integration executes through a versioned canonical connector contract; provider DTOs and SDK types do not escape the adapter boundary.
- Credentials and private keys remain in an approved secret/key-management system. The database stores only opaque references, metadata and lifecycle evidence.
- NIBSS timeouts and any ambiguous post-submission outcomes return `Uncertain` plus status-enquiry capability, never an invented failure or automatic duplicate submission.
- Webhook signatures, source restrictions where applicable, timestamps, event identities and replay state are checked before dispatch to a business consumer.
- Accepted webhook events are durably stored before asynchronous acknowledgement/handling and are processed at most once per consumer effect through inbox/idempotency controls.
- Connector health distinguishes transport availability, credential/certificate condition and business-result quality; stale health cannot be presented as current.
- Operators can see latency, success rate, uncertainty, circuit state, expiry and certification status without seeing secrets or unrestricted payloads.
- Replay and repair are authorised, reasoned, bounded and auditable; replaying evidence cannot create a duplicate financial or customer outcome.
- Each production-enabled provider/environment/service combination has an approved certification run, current credentials, monitored expiry and an operations runbook.
- Contract, security, resilience, concurrency, replay, restore and provider sandbox tests produce traceable evidence for API-001 through API-008, NIB-001 through NIB-006, PSP-001 through PSP-006 and US-004/068/069.

Targets such as provider latency, availability, webhook acknowledgement time, retention and recovery objectives remain release decisions. Agents must not invent numeric targets.

## 3. Scope and ownership boundaries

### Included

- Institution- and environment-scoped provider registry, supported services and enabled-state management.
- Opaque secret references, certificate metadata, network restrictions, endpoint references, rotation state and expiry monitoring.
- Versioned C# connector adapters and canonical contracts for enabled Phase 1 provider capabilities.
- Phase 1 NIBSS NIP name enquiry, transfer submission, status enquiry and approved reversal/recall transport operations.
- Phase 1 PSP collection initialisation, server-side verification and verified webhook normalisation.
- Canonical KYC/identity verification, VTU/biller and SMS/email transport foundations required by Phase 1 owning services.
- Safe foundations for USSD and WhatsApp provider protocols used by Epic 09; conversation/session/content logic remains outside this service.
- Provider request signing, encryption, authentication, secure response verification and protocol transformation.
- Durable external invocation records with attempt, submission, response, uncertainty and status-enquiry evidence.
- Webhook receipt, validation, deduplication, durable dispatch, dead-letter and authorised manual replay.
- File- and event-based connector foundations where an approved Phase 1 provider requires them.
- Provider health snapshots, circuit state, technical and business indicators, certificate/credential expiry alerts and capability availability projection.
- Synthetic sandbox adapters, conformance fixtures, certification runs and production enablement gates.
- Outbound delivery tracking for provider-facing webhooks/messages where the Integration Hub owns transport.
- Versioned internal APIs and events, tenant isolation, service authentication, rate controls, audit and observability.

### Excluded

- Customer balances, journals, holds and financial reversals; Core Banking and Ledger owns them.
- Payment instructions, beneficiaries, limits, financial route selection, pricing and payment status; Payments and VTU owns them.
- Selecting a second financial provider after an uncertain attempt. Payments decides eligibility only after Integration reports verified original-attempt status.
- KYC case decisions, customer risk tier and onboarding state; Customer and Onboarding owns them.
- Notification templates, preferences, campaigns, CMS content, USSD menus, WhatsApp conversations and human handoff; Epic 09 owns them.
- Approval policy and maker-checker execution; Operations and Workflow owns them.
- Provider settlement matching, reconciliation breaks, suspense and certification of financial reconciliation; Audit and Reconciliation owns them.
- Phase 3 public partner APIs, SDKs, corporate API clients/webhooks, open banking and production developer portal, except for foundations that do not expose the capability.
- Phase 3 virtual-account, bulk/payroll, card/acquiring and corporate host-to-host connector execution.
- Invented live provider endpoints, message fields, status meanings, crypto algorithms, retry semantics, SLAs or certification outcomes.

### Boundary rules

1. A business service submits a canonical operation and receives a canonical transport/result status plus stable references.
2. Integration Hub owns external protocol correctness, provider authentication, request/response evidence and provider status mapping.
3. The calling service owns validation of business eligibility and interpretation of the result in its aggregate.
4. Integration Hub never posts a Ledger entry, changes a payment/customer/channel state directly or reads another service's private database.
5. Provider routing health is an input. Payments or Channels owns the route decision for its journey and records the policy/version used.
6. A provider callback is evidence, not an authoritative financial outcome. The owning service applies it idempotently and may require server-side status verification.
7. Secrets are resolved only at the adapter execution boundary and are never returned through APIs/events or persisted in Integration Hub tables.

## 4. Actors, roles and permissions

| Actor | Permission | Contextual scope | Step-up | Approval |
| --- | --- | --- | --- | --- |
| Integration administrator | `integration.provider.read` | Institution/environment/provider/service | No | None |
| Integration administrator | `integration.provider.configure` | Institution/environment/provider/service | Yes | Maker-checker before enablement |
| Credential custodian | `integration.credential.reference.register` | Institution/environment/provider | Yes | Dual control |
| Credential custodian | `integration.credential.rotate` | Institution/environment/provider/reference | Yes | Dual control for production |
| Certificate administrator | `integration.certificate.manage` | Institution/environment/provider | Yes | Dual control for production |
| Connector engineer | `integration.connector.version.manage` | Connector/provider/service/non-production | Yes for publication | Approval before production certification |
| Mapping administrator | `integration.mapping.manage` | Institution/provider/service/version | Yes | Maker-checker |
| Certification operator | `integration.certification.execute` | Provider/environment/service/version | Yes | Result approval separate from executor |
| Certification approver | `integration.certification.approve` | Provider/environment/service/version | Yes | Cannot approve own run |
| Operations engineer | `integration.health.read` | Institution/environment/provider/service | No | None |
| Operations engineer | `integration.circuit.manage` | Institution/environment/provider/service | Yes | Reason required; approval where policy requires |
| Support operator | `integration.invocation.read` | Institution/provider/service/reference | Step-up for sensitive evidence | None |
| Repair operator | `integration.webhook.replay` | Institution/provider/subscription/event | Yes | Reason plus approval for financial consumers |
| Repair operator | `integration.delivery.replay` | Institution/provider/delivery | Yes | Reason plus approval where configured |
| Audit/reconciliation reader | `integration.evidence.read` | Institution/provider/reference/date range | Step-up for protected data | None |
| Calling service identity | `integration.operation.execute` | Allow-listed service, operation, institution, environment | Service authentication | No human role |
| Webhook endpoint identity | Endpoint-specific verification | Exact provider/subscription/environment | Signature/source/time/replay proof | Not RBAC |

Identity and Access owns grants and entitlement versions. Integration Hub declares and enforces its capability permissions, trusted service audience, provider/environment scope and institution scope. Public provider callbacks do not receive human permissions; their authentication policy is the approved provider-specific verification profile.

No permission permits reading plaintext credentials or private keys. Production changes separate maker, approver and certification approver. Emergency circuit changes are time-bound, reasoned and reviewed; they do not bypass webhook verification, idempotency or uncertainty rules.

## 5. Domain model

### Aggregates and entities

| Concept | Responsibility | Identity | Important relationships |
| --- | --- | --- | --- |
| Provider | Provider identity, type and institution availability | `ProviderId` | Has services and environment configurations |
| ProviderService | Canonical capability and provider support state | `ProviderServiceId` | Provider, operation type, connector version |
| EnvironmentConfiguration | Endpoint references, network rules and environment enablement | `ProviderEnvironmentId` | Institution, provider, environment, credentials, certificates |
| CredentialReference | Opaque secret-manager reference and lifecycle metadata | `CredentialReferenceId` | Never contains secret value |
| CertificateRecord | Public metadata, key reference, validity and usage | `CertificateId` | Environment configuration and rotation chain |
| ConnectorDefinition | Stable canonical connector family | `ConnectorDefinitionId` | Has immutable versions |
| ConnectorVersion | Adapter version, capabilities, mappings and certification state | `ConnectorVersionId` | Provider services and certification runs |
| CanonicalMapping | Versioned provider status/field to canonical mapping | `MappingVersionId` | Connector version and operation |
| ExternalInvocation | Durable execution and protocol-result evidence | `InvocationId` | Canonical request ref, attempts and original business ref |
| InvocationAttempt | One controlled network submission/enquiry | `InvocationAttemptId` | Invocation, credential/certificate/connector versions |
| WebhookReceipt | Immutable inbound event envelope/evidence | `WebhookReceiptId` | Provider event ID, validation and dispatch records |
| WebhookDispatch | Delivery to one internal consumer | `WebhookDispatchId` | Receipt, consumer and deduplication state |
| OutboundDelivery | Provider-facing message/webhook delivery | `DeliveryId` | Attempts, subscription and content digest |
| HealthSnapshot | Time-bounded technical/business/certificate signals | `HealthSnapshotId` | Provider service/environment |
| CertificationRun | Immutable test plan execution and evidence refs | `CertificationRunId` | Connector/environment/service/version |
| RepairRequest | Authorised replay or controlled repair | `RepairRequestId` | Target receipt/delivery/invocation and workflow ref |

Cross-service identifiers are references only. There are no database foreign keys to Payments, Customer, Channels, Workflow, Audit/Reconciliation or Ledger databases.

### Value objects

- `InstitutionId`, `ProviderCode`, `EnvironmentCode`, `ServiceCode`, `OperationCode` and `ConnectorVersion`.
- `CanonicalRequestId`, `BusinessReference`, `CorrelationId`, `CausationId` and `IdempotencyKey`.
- `ProviderRequestReference`, `ProviderTransactionReference`, `ProviderEventId` and `ProviderSessionReference`.
- `EndpointReference`, `SecretReference`, `KeyReference`, `CertificateThumbprint` and `NetworkRestriction`.
- `RequestDigest`, `ResponseDigest`, `PayloadEvidenceReference`, `SignatureProfileVersion` and `MappingVersion`.
- `SubmissionState`, `CanonicalProviderStatus`, `UncertaintyReason`, `CircuitState` and `CertificationStatus`.
- `ObservedAt`, `ValidFrom`, `ValidUntil`, `FreshUntil`, `OccurredAt` and `ReceivedAt` using `DateTimeOffset`.
- `HealthSignal`, `LatencyMeasurement`, `BusinessFailureRate`, `ExpiryWindow` and `EvidenceClassification`.
- Money, where a protocol requires it, uses `decimal` plus explicit ISO currency and is never used here as an authoritative balance.

### Invariants

1. Every tenant-owned provider configuration, invocation, webhook, delivery and evidence record has a trusted `InstitutionId`.
2. Provider/environment/service codes and canonical operations are allow-listed and versioned; callers cannot supply arbitrary URLs, credential references or adapter types.
3. Secret values and private keys never enter domain objects, database columns, logs, traces, events, errors or client responses.
4. A credential/certificate can be used only for its approved institution, provider, environment, service, purpose and validity window.
5. Connector and mapping versions used by an invocation are recorded immutably.
6. The canonical request digest is bound to the idempotency scope. Reusing a key with a different digest is a conflict.
7. At most one submission attempt may be active for the same canonical financial command unless an approved, verified retry decision authorises another attempt.
8. A timeout after possible provider submission becomes `Uncertain`; it is never mapped to definite failure merely to permit retry.
9. Status enquiry uses the original stable references and cannot silently create a new financial submission.
10. Provider-specific statuses map to documented canonical statuses. Unknown statuses remain `Unknown`/`Uncertain` and create operational evidence; they are not guessed.
11. A webhook event is dispatched only after endpoint identification, signature/source/timestamp validation, replay check and durable receipt.
12. Provider event identity is unique within provider/subscription/environment scope; duplicate content returns the recorded receipt and does not redispatch effects.
13. Manual replay creates a new replay/dispatch record linked to immutable original evidence; it never edits the receipt.
14. Health data records observation time, source and freshness. Stale health is `Unknown`, not healthy.
15. Production enablement requires an approved connector version, mapping, configuration, credential/certificate validity, certification evidence and runbook reference.
16. Integration Hub never treats an external result as permission to mutate an owning service's state directly.
17. No network call occurs while holding a database transaction open.
18. Raw provider evidence, when retention is approved, is encrypted, access-controlled, integrity-protected and separate from ordinary telemetry.

Database unique constraints, concurrency tokens and compare-and-swap transitions enforce invariants 1, 5, 6, 7, 12, 13 and 15 under concurrent execution.

### State transitions

Provider environment configuration:

```text
Draft -> PendingApproval -> Approved -> Certified -> Enabled
   |           |              |           |          |
   +--------> Rejected        +--------> Superseded  +-> Suspended -> Enabled
                                                       -> Retired
```

Connector version:

```text
Draft -> PublishedForTest -> CertificationPending -> Certified -> ProductionApproved
   |             |                    |                 |              |
   +----------> Withdrawn <-----------+-----------------+--------------+
```

Credential/certificate reference:

```text
Registered -> Validated -> Active -> Expiring -> Rotated -> Retired
                 |          |           |           |
                 +------> Invalid       +--------> Expired
                            +---------------------> Revoked
```

External invocation:

```text
Registered -> Dispatching -> FailedBeforeSubmission
                        \-> Submitted -> Succeeded
                                      -> FailedFinal
                                      -> Uncertain -> EnquiryPending -> Succeeded
                                                                       -> FailedFinal
                                                                       -> Uncertain
                                                                       -> InvestigationRequired
```

Webhook receipt and dispatch:

```text
Received -> RejectedAuthentication
        -> Validated -> Persisted -> DispatchPending -> Dispatched
                                                \-> RetryScheduled -> Dispatched
                                                                  -> DeadLettered
DeadLettered -> ReplayApproved -> DispatchPending
```

Only typed commands may transition state. Financial-service retry/failover eligibility is not a transition in this service; Integration exposes verified invocation facts to the owner.

## 6. Canonical connector model

Every provider adapter implements explicit operation-specific ports rather than a single untyped `Execute(object)` method. Examples include:

- `INameEnquiryConnector`
- `IInterbankTransferConnector`
- `IProviderStatusEnquiryConnector`
- `ICollectionConnector`
- `IIdentityVerificationConnector`
- `IValueAddedServiceConnector`
- `IBillValidationConnector`
- `IMessageTransportConnector`
- `IChannelGatewayConnector`

Canonical requests contain only fields required by the approved business contract. Provider adapters map them to provider DTOs in Infrastructure, sign/encrypt/authenticate the request, invoke the configured endpoint and map the response to a canonical result.

A canonical result contains:

- Invocation and attempt IDs.
- Submission state: `NotSubmitted`, `Submitted`, `SubmissionUnknown`.
- Canonical status: `Succeeded`, `FailedFinal`, `Pending`, `Uncertain`, `Rejected`, `Unknown`.
- Stable provider references that are safe to disclose internally.
- Provider status code plus approved normalised reason code; raw descriptions remain protected evidence.
- Observation timestamp, connector/mapping/configuration versions and evidence digest/reference.
- Whether status enquiry is supported and the original reference required.
- Explicit response authenticity/verification outcome.

Canonical contracts never claim a Ledger or customer-facing final state. `Succeeded` means the provider operation was verified successful under the connector specification; the owner still applies its own state and financial rules.

## 7. Use cases

### UC-01 - Register and approve provider configuration

- Actor: Integration administrator and approver.
- Preconditions: Institution/environment exists; provider/service type is approved; secret references already exist in the secret manager.
- Permission and scope: `integration.provider.configure` for exact institution/environment/provider.
- Command: Register provider, services, endpoint references, network restrictions and credential/certificate references.
- Main flow: Validate allow-listed scheme/host/reference metadata; create Draft version; simulate capability impact; submit to Workflow; publish approved immutable version; keep disabled until certification.
- Failure outcomes: Invalid endpoint/reference, cross-tenant reference, missing approval, expired certificate, unsupported service or duplicate version.
- Idempotency scope: Institution + environment + configuration command ID.
- Transaction boundary: Configuration version, audit link and outbox record commit locally.
- Audit evidence: Actor, permission, before/after metadata, approval/workflow ref, effective dates, no secret value.
- Events: `ProviderConfigurationPublished` or `ProviderConfigurationRejected`.

### UC-02 - Rotate a credential or certificate

- Actor: Credential/certificate custodian plus approver.
- Preconditions: New secret/key reference validates for exact scope; overlap/activation policy is approved.
- Permission and scope: `integration.credential.rotate` or `integration.certificate.manage`.
- Main flow: Register successor reference; validate metadata/proof without exposing value; approve; activate at effective time; drain prior reference; monitor expiry; retain immutable lineage.
- Alternative flows: Emergency revoke suspends affected provider services until a valid approved reference exists.
- Failure outcomes: Secret resolution denied, thumbprint mismatch, invalid chain, expired material, wrong environment/purpose or self-approval.
- Idempotency scope: Institution + reference lineage + rotation command ID.
- Audit evidence: References/thumbprints, validity, reason, actor/approver and affected services.
- Events: `ProviderCredentialReferenceActivated`, `ProviderCertificateExpiring`, `ProviderCredentialRevoked`.

### UC-03 - Execute a canonical provider operation

- Actor: Authorised internal service identity.
- Preconditions: Caller/operation/audience allow-listed; provider service enabled/certified; request valid; idempotency present when required.
- Permission and scope: `integration.operation.execute` constrained by service identity, institution and operation.
- Main flow: Validate canonical request and configuration freshness; reserve idempotency record; persist invocation; resolve secret only inside adapter; perform network call outside database transaction; verify/map response; persist attempt and result; return/publish canonical outcome.
- Alternative flows: Safe pre-submission transport failure may be returned as `FailedBeforeSubmission`; calling owner decides whether to retry or choose another route.
- Failure outcomes: Disabled/stale configuration, circuit open, authentication failure, unknown provider status, invalid signature or timeout.
- Idempotency scope: Institution + caller + canonical operation + business/idempotency key.
- Audit evidence: Safe request digest, versions, attempt/submission/result, references and correlation.
- Events: `CanonicalProviderOutcomeRecorded`, `ProviderInvocationBecameUncertain`.

### UC-04 - Resolve an uncertain invocation

- Actor: Owning service or authorised repair operator.
- Preconditions: Original invocation is `Uncertain`; connector supports status enquiry; original references exist.
- Permission and scope: Calling service operation or `integration.invocation.requery` if granted.
- Main flow: Create linked enquiry using original refs; execute enquiry idempotently; map verified status; append evidence; notify owner.
- Alternative flows: Repeated inconclusive results remain Uncertain and escalate to investigation after approved policy thresholds.
- Failure outcomes: Provider has no safe enquiry, reference mismatch, expired evidence or conflicting final results.
- Idempotency scope: Original invocation + enquiry sequence/window + request key.
- Audit evidence: Original/enquiry refs, observations, mapping version, operator reason if manual.
- Events: `ProviderInvocationStatusVerified` or `ProviderInvocationInvestigationRequired`.

### UC-05 - Receive and dispatch a provider webhook

- Actor: External provider system.
- Preconditions: Known endpoint/subscription/environment; bounded request; verification profile available.
- Authentication and scope: Exact subscription; approved signature/source/timestamp/replay controls.
- Main flow: Capture raw bytes safely; identify endpoint; validate authenticity; calculate digest; persist immutable receipt and validation evidence; acknowledge per approved provider contract; asynchronously map to canonical event; dispatch to allow-listed consumers through outbox/inbox.
- Alternative flows: A valid duplicate returns the prior acknowledgement semantics and produces no duplicate dispatch.
- Failure outcomes: Unknown endpoint, excessive body, unsupported content type, invalid signature/source/time, replay, malformed schema or mapping failure.
- Idempotency scope: Provider + environment + subscription + provider event ID; digest conflict is a security event.
- Transaction boundary: Receipt/validation/outbox commit atomically before dispatch.
- Audit evidence: Safe headers, digest, validation profile/result, source metadata and receipt status; secrets/raw sensitive data excluded from logs.
- Events: `CanonicalProviderWebhookReceived`, `ProviderWebhookRejected`, `WebhookDispatchDeadLettered`.

### UC-06 - Replay a dead-lettered webhook or delivery

- Actor: Repair operator and approver where required.
- Preconditions: Immutable original evidence exists; target consumer supports idempotency; replay policy permits action.
- Permission and scope: `integration.webhook.replay` or `integration.delivery.replay`.
- Main flow: Preview target and prior attempts; enter reason; obtain approval for financial/sensitive consumers; create replay request; issue a new dispatch with original event identity plus replay ID; record consumer outcome.
- Failure outcomes: Original evidence invalid/expired, target already applied effect, consumer contract incompatible, approval missing or cross-tenant target.
- Idempotency scope: Target + approved replay request ID.
- Audit evidence: Requester, approver, reason, original/new dispatch refs and result.
- Events: `IntegrationReplayRequested`, `IntegrationReplayCompleted`.

### UC-07 - Monitor provider health and readiness

- Actor: Operations engineer and consuming route owner.
- Preconditions: Approved health policy and probes; no financial side effect in health check.
- Permission and scope: `integration.health.read` or trusted service projection access.
- Main flow: Collect technical probe, real traffic aggregates, uncertainty/business failure signal, circuit state, wallet/capability signal when approved and expiry/certification state; persist timestamped snapshot; publish readiness change.
- Failure outcomes: Probe timeout, stale sample, missing credential, expired certificate or inconsistent signals.
- Idempotency scope: Provider service + environment + observation window/source.
- Audit evidence: Policy version and operator circuit overrides.
- Events: `ProviderReadinessChanged`, `ProviderCertificateExpiring`.

### UC-08 - Certify and enable a connector version

- Actor: Certification operator and separate approver.
- Preconditions: Published test version, sandbox configuration, synthetic fixtures and approved test plan.
- Permission and scope: `integration.certification.execute` / `integration.certification.approve`.
- Main flow: Run conformance cases; retain redacted evidence and hashes; record provider/environment/spec versions; obtain independent approval; enable only exact certified combination.
- Failure outcomes: Test failure, unapproved deviation, missing evidence, production credential used in sandbox or version drift.
- Idempotency scope: Connector version + environment + certification plan/run key.
- Events: `ConnectorCertificationCompleted`, `ProviderServiceEnabled`.

### UC-09 - Send provider-facing delivery

- Actor: Channels/engagement or another authorised owner.
- Preconditions: Approved subscription/transport/template-rendered content; consent/business checks already performed by owner.
- Permission and scope: Service identity constrained to transport and institution.
- Main flow: Persist delivery/idempotency; choose the owner-selected or policy-approved transport; resolve credential; submit; capture provider ID; process delivery receipt; publish canonical delivery status.
- Failure outcomes: Pre-submit failure, provider rejection, timeout, invalid receipt or exhausted retry policy.
- Idempotency scope: Institution + owner + message/delivery key + provider route.
- Boundary: Integration Hub transports content but does not create templates, decide consent or infer marketing eligibility.

## 8. API contracts

### Administrative APIs

| Method | Route | Permission | Request | Response | Idempotent |
| --- | --- | --- | --- | --- | --- |
| GET | `/api/v1/integration/providers` | `integration.provider.read` | Filters/page | Provider summary page | Query |
| POST | `/api/v1/integration/providers` | `integration.provider.configure` | `RegisterProviderRequest` | Provider draft | Yes |
| POST | `/api/v1/integration/providers/{providerId}/configurations` | `integration.provider.configure` | `CreateEnvironmentConfigurationRequest` | Configuration version | Yes |
| POST | `/api/v1/integration/configurations/{configurationId}/submit` | `integration.provider.configure` | Version/reason | Workflow reference | Yes |
| POST | `/api/v1/integration/credentials/references` | `integration.credential.reference.register` | Opaque reference metadata | Reference summary | Yes |
| POST | `/api/v1/integration/credentials/{referenceId}/rotations` | `integration.credential.rotate` | Successor reference/effective time | Rotation record | Yes |
| POST | `/api/v1/integration/certificates` | `integration.certificate.manage` | Public metadata/key reference | Certificate record | Yes |
| POST | `/api/v1/integration/connectors/{connectorId}/versions` | `integration.connector.version.manage` | Version/capabilities/artifact digest | Connector version | Yes |
| POST | `/api/v1/integration/mappings` | `integration.mapping.manage` | Provider/canonical mapping version | Mapping draft | Yes |
| POST | `/api/v1/integration/certification-runs` | `integration.certification.execute` | Exact version/environment/plan | Run reference | Yes |
| GET | `/api/v1/integration/health` | `integration.health.read` | Bounded filters/page | Freshness-labelled health page | Query |
| GET | `/api/v1/integration/invocations/{invocationId}` | `integration.invocation.read` | None | Redacted invocation evidence | Query |
| POST | `/api/v1/integration/webhooks/{receiptId}/replays` | `integration.webhook.replay` | Reason/approval ref/consumer | Replay reference | Yes |
| POST | `/api/v1/integration/deliveries/{deliveryId}/replays` | `integration.delivery.replay` | Reason/approval ref | Replay reference | Yes |

### Internal canonical APIs

| Method | Route | Service permission | Request | Response | Idempotent |
| --- | --- | --- | --- | --- | --- |
| POST | `/internal/v1/invocations` | `integration.operation.execute` | `ExecuteCanonicalOperationRequest` | Invocation/result envelope | Yes |
| GET | `/internal/v1/invocations/{invocationId}` | Scoped invocation read | None | Canonical invocation status | Query |
| POST | `/internal/v1/invocations/{invocationId}/status-enquiries` | Scoped enquiry permission | `StatusEnquiryRequest` | Linked enquiry/result | Yes |
| GET | `/internal/v1/provider-readiness` | Scoped readiness read | Provider/service filters | Freshness-labelled projection | Query |
| POST | `/internal/v1/deliveries` | Scoped transport permission | `CreateDeliveryRequest` | Delivery reference/status | Yes |

`ExecuteCanonicalOperationRequest` uses a tagged, versioned allow-list of operation contracts; it does not accept arbitrary endpoint, HTTP method, headers, provider payload or credential reference. Financial operations require idempotency and correlation identifiers.

### Provider ingress APIs

| Method | Route | Authentication | Behaviour |
| --- | --- | --- | --- |
| POST | `/webhooks/v1/{providerCode}/{subscriptionKey}` | Provider-specific signature/source/time/replay policy | Validate, persist, acknowledge and dispatch asynchronously |
| POST | `/callbacks/v1/{providerCode}/{operation}` | Approved provider callback policy | Record evidence and trigger server verification where required |

Stable errors use RFC 7807 for trusted APIs, including `integration.configuration_stale`, `integration.provider_disabled`, `integration.connector_not_certified`, `integration.idempotency_conflict`, `integration.circuit_open`, `integration.invocation_uncertain`, `integration.status_enquiry_unsupported` and `integration.cross_tenant_denied`. Provider ingress responses follow the approved provider protocol without exposing internal errors.

All list/search APIs are paginated and bounded. Evidence APIs mask payloads by default, require step-up for approved sensitive access and never return credentials/private keys. Breaking canonical contract changes require a new version plus migration/certification plan.

## 9. Event contracts

### Produced events

| Event | Trigger | Key fields | Ordering key | Consumers |
| --- | --- | --- | --- | --- |
| `ProviderConfigurationPublished` | Approved version effective | Institution, provider, service, environment, version | Provider environment | Payments, Customer, Channels, Audit |
| `ProviderServiceEnabled` | Certified configuration enabled | Provider/service/version/certification ref | Provider service | Owning services |
| `ProviderReadinessChanged` | Material fresh readiness transition | Provider/service/environment, signals, freshness, version | Provider service | Payments, Channels, Operations |
| `ProviderCertificateExpiring` | Approved threshold reached | Provider/environment/certificate ref/expiry | Certificate | Operations, Audit |
| `CanonicalProviderOutcomeRecorded` | Invocation result durably mapped | Invocation/business refs, operation, status, submission state, provider refs, versions | Invocation | Calling owner, Audit/Reconciliation |
| `ProviderInvocationBecameUncertain` | Possible submission without final status | Invocation/attempt/original refs, reason, enquiry support | Invocation | Calling owner, Operations |
| `ProviderInvocationStatusVerified` | Enquiry obtains verified status | Original/enquiry refs, status, observed time, versions | Invocation | Calling owner, Audit/Reconciliation |
| `CanonicalProviderWebhookReceived` | Valid receipt mapped | Receipt/event/provider refs, canonical type, digest, occurred/received time | Provider event | Allow-listed owner |
| `WebhookDispatchDeadLettered` | Dispatch policy exhausted | Receipt/dispatch/consumer refs, safe reason | Receipt/consumer | Operations, Audit |
| `OutboundDeliveryStatusRecorded` | Delivery/receipt mapped | Delivery/owner/provider refs, canonical status | Delivery | Channels/owner |
| `ConnectorCertificationCompleted` | Run finalised | Connector/version/environment/plan/result/evidence refs | Connector version | Operations, Audit |
| `IntegrationReplayCompleted` | Approved replay completes | Repair/original/new dispatch refs/result | Repair request | Owner, Audit |

### Consumed events

| Event | Producer | Consumer behaviour | Deduplication key | Failure handling |
| --- | --- | --- | --- | --- |
| `InstitutionActivated` / `InstitutionSuspended` | Core/Configuration | Permit or suspend institution integration execution | Event ID | Inbox/DLQ; deny execution if state uncertain |
| `ConfigurationApproved` | Workflow | Publish exact provider/mapping configuration version | Event ID + config version | Inbox; no partial enablement |
| `ConfigurationRejected` | Workflow | Mark exact draft rejected | Event ID + config version | Inbox |
| `IntegrationRepairApproved` | Workflow | Activate exact replay/repair request | Event ID + repair ID | Inbox; reject mismatch |
| `ProviderSettlementEvidenceRequested` | Audit/Reconciliation | Produce bounded evidence export/ref | Event ID + request ID | Inbox and controlled retry |

Every event includes event ID/type/schema version, institution ID, occurred time, correlation/causation IDs and aggregate/business reference. Events exclude secrets, raw signatures, unrestricted provider bodies and unnecessary PII. Outbox publishing and inbox deduplication are mandatory; replay must preserve the original provider event identity.

## 10. Database design

### Ownership

- Database: `integration_hub_db`.
- Schemas: `provider`, `connector`, `partner`, `webhook`, `delivery`, `integration`.
- Migration assembly: `CoreBanking.IntegrationHub.Infrastructure`.
- Database principal: Integration Hub runtime/migration identities only, separated by privilege.
- Secret/key values: external approved secret/key manager only; database stores references and metadata.

### Tables

| Table | Purpose | Key constraints | Tenant-owned | Retention |
| --- | --- | --- | --- | --- |
| `provider.providers` | Stable provider registry | Unique institution + provider code | Yes | Life + policy |
| `provider.provider_services` | Supported canonical operations | Unique provider + environment + service + version | Yes | Life + policy |
| `provider.environment_configurations` | Versioned endpoint/network/environment metadata | Unique scope + version; state checks | Yes | Configuration retention |
| `provider.credential_references` | Opaque secret references/lifecycle | Unique scoped reference/version; no secret columns | Yes | Security policy |
| `provider.certificates` | Certificate/key-reference metadata and lineage | Unique thumbprint/scope; validity checks | Yes | Security/evidence policy |
| `provider.health_status` | Timestamped health/readiness observations | Unique scope + source + observation window | Yes | Operational policy |
| `provider.routing_policies` | Integration transport/readiness policy only | Versioned/effective-dated/approved | Yes | Policy retention |
| `connector.connector_definitions` | Connector families/capabilities | Unique connector code | Platform/scoped | Life |
| `connector.connector_versions` | Immutable adapter release metadata | Unique connector + semantic version + digest | Platform/scoped | Permanent evidence |
| `connector.canonical_mappings` | Field/status mapping versions | Unique connector version + operation + mapping version | Yes where overridden | Evidence policy |
| `connector.status_code_mappings` | Provider-to-canonical status mapping | Unique mapping + provider status/code | Yes where overridden | Evidence policy |
| `connector.external_invocations` | Durable canonical invocation state | Unique idempotency scope; digest/state checks | Yes | Transaction/evidence policy |
| `connector.invocation_attempts` | Append-only network attempts/enquiries | Unique invocation + attempt ordinal/type | Yes | Transaction/evidence policy |
| `partner.api_clients` | Phase 1 internal/provider client metadata foundations | Unique institution/environment/client code | Yes | Security policy |
| `partner.client_scopes` | Explicit client scopes | Unique client + scope | Yes | Security policy |
| `partner.network_restrictions` | Approved CIDR/source restrictions | Unique client/subscription + network/version | Yes | Security policy |
| `partner.webhook_subscriptions` | Provider endpoint/verification/consumer mapping | Unique provider/environment/subscription key | Yes | Life + evidence |
| `partner.certification_runs` | Immutable run/result/evidence refs | Unique connector/env/plan/run key | Yes | Permanent/release evidence |
| `webhook.received_events` | Immutable inbound receipt/envelope/digest | Unique provider/subscription/event ID | Yes | Approved evidence policy |
| `webhook.signature_validation_results` | Validation profile/result evidence | One or more versioned checks per receipt | Yes | Security evidence policy |
| `webhook.webhook_dispatches` | Per-consumer dispatch state | Unique receipt + consumer + schema version | Yes | Evidence policy |
| `webhook.replay_records` | Append-only authorised replay lineage | Unique repair request/target | Yes | Audit policy |
| `delivery.outbound_deliveries` | Provider-facing delivery state | Unique owner + idempotency key + digest | Yes | Communication/evidence policy |
| `delivery.delivery_attempts` | Append-only attempt/result evidence | Unique delivery + ordinal | Yes | Evidence policy |
| `delivery.dead_letters` | Exhausted dispatch/delivery repair queue | Unique target + failure generation | Yes | Until resolved + policy |
| `integration.outbox_messages` | Durable event publication | Unique event ID | Yes | Operational policy |
| `integration.inbox_messages` | Consumer deduplication | Unique producer + event ID + consumer | Yes | Replay-window policy |
| `integration.replay_requests` | Authorised repair command state | Unique target + active request constraint | Yes | Audit policy |

The architecture baseline lists the core tables. `connector.external_invocations`, `connector.invocation_attempts` and `webhook.webhook_dispatches` make the required durability and consumer-state boundary explicit and require an ADR/migration review if naming differs in implementation.

### Indexes and constraints

- Every tenant query index begins with `institution_id` where the table is tenant-owned.
- Partial unique index prevents two active configurations for one institution/provider/environment/service/effective instant.
- Unique idempotency index includes institution, caller, operation and key; stored request digest detects conflicts.
- Partial unique index prevents more than one active submission attempt for a financial invocation.
- Provider event uniqueness is provider + environment + subscription + provider event ID; an additional digest conflict check detects reused IDs with changed payload.
- Connector versions and certification evidence are immutable after approval.
- State columns have check constraints and optimistic concurrency versions.
- Endpoint data is normalised/validated against allow-lists; no free-form URL is accepted during execution.
- Payload/evidence columns use approved encryption and size limits; routine query indexes use safe metadata/digests only.
- Monetary fields, if required by canonical evidence, use approved PostgreSQL precision/scale plus required ISO currency.
- Expiry, freshness, state, provider refs, correlation and business refs have bounded operational indexes.

### Migration and backfill

- Create schemas/tables/constraints before any connector becomes enabled.
- Seed only canonical provider/service definitions and synthetic sandbox metadata; never seed live credentials or customer/provider payloads.
- Import an existing configuration only through a reviewed mapping that creates version, approval, secret reference and certification evidence.
- Released migrations are append-only. Correct defects with forward migrations and a documented verification/forward-fix plan.
- Production schema changes that touch invocation/webhook evidence require compatibility, retention, replay and restore analysis.
- Verification proves no plaintext secret-shaped fields, duplicate event identities, overlapping active versions, orphan attempts or enabled uncertified configurations.

## 11. Consistency, concurrency and idempotency

- Configuration/version publication commits state plus outbox atomically; consumers activate only the exact published version.
- Invocation registration/idempotency commits before the network call. Attempt/result updates use optimistic concurrency and append-only evidence.
- No database lock/transaction is held during provider I/O.
- Repeated identical canonical commands return the stored invocation/current result. A different digest with the same key returns conflict and audit evidence.
- Request cancellation before submission may stop safely. Cancellation after submission cannot erase the attempt or cause a misleading failure; result remains pending/uncertain until observed.
- Status enquiries are separate linked attempts with their own idempotency keys and cannot reuse the submission operation.
- Webhook receipt, validation result and outbox dispatch record commit before asynchronous processing. Consumer effects use their own inbox/idempotency boundary.
- Multiple webhook deliveries or operator replays converge on one business event identity. Replay adds lineage; it does not manufacture a new provider event.
- Health writes are append/update-by-window under concurrency; freshness is calculated from approved policy and observation time, never from last-write arrival alone.
- Credential rotation uses version/effective windows and compare-and-swap activation. In-flight invocations retain the credential-reference version used without storing the secret.
- Cross-service consistency uses APIs/events, idempotency and reconciliation; no distributed transaction is required.
- If event publication fails after local commit, outbox retry continues. If a consumer is unavailable, dispatch retry/dead-letter preserves the receipt.

## 12. Provider invocation and uncertainty rules

| Observation | Submission state | Canonical status | Retry/failover fact exposed |
| --- | --- | --- | --- |
| Validation/configuration rejects before network | `NotSubmitted` | `Rejected` | No external submission; owner may correct/new request |
| Connection cannot begin and connector proves no bytes/request accepted | `NotSubmitted` | `FailedFinal` or pre-submit failure | Owner may evaluate safe retry policy |
| Provider gives verified final success | `Submitted` | `Succeeded` | Final provider fact; owner applies domain rules |
| Provider gives verified final rejection/failure | `Submitted` | `FailedFinal` | Final provider fact; owner applies compensation/retry policy |
| Timeout/connection loss after possible submission | `SubmissionUnknown` | `Uncertain` | Status enquiry required; failover unsafe |
| Async accepted/pending result | `Submitted` | `Pending` | Poll/callback/enquiry per approved semantics |
| Unknown/unmapped/conflicting provider status | `Submitted` or unknown | `Unknown`/`Uncertain` | Investigation/enquiry; no guessed retry |

Retries inside a connector are permitted only when the exact operation is proven idempotent under the provider specification and the attempt uses the same stable provider/idempotency reference. Generic HTTP retry libraries must not retry unsafe POSTs by default. Circuit breakers stop new eligible calls; they do not rewrite existing invocation outcomes.

## 13. Webhook and callback processing

1. Terminate TLS at an approved boundary while preserving the exact raw body needed for signature verification.
2. Resolve the endpoint to one provider/environment/subscription without trusting request-body tenant identifiers.
3. Enforce body/header/content-type/time/network limits before expensive parsing.
4. Verify signature/MAC/certificate using the exact provider profile and raw bytes.
5. Validate timestamp/nonce/event ID/source policy and constant-time comparison where applicable.
6. Persist immutable receipt, digest and validation result before asynchronous dispatch; retain raw evidence only under approved encrypted policy.
7. Return only the provider-approved acknowledgement; never leak validation internals.
8. Parse with a hardened provider-specific schema and map to a versioned canonical event.
9. If finality requires server verification, create a linked verification/status invocation instead of emitting final success directly.
10. Dispatch through outbox to exact allow-listed consumers; record each consumer delivery separately.
11. Retry only transport dispatch, not the business effect; exhausted dispatch becomes visible dead letter.
12. Manual replay requires permission, reason, original evidence and idempotent consumer proof/approval.

Redirect/browser callback parameters are never treated as final payment evidence. They may return a generic customer experience while server-side verification continues.

## 14. Provider health, circuits and readiness

Health is multidimensional:

- Technical: DNS/TLS/connectivity/authentication/protocol availability and latency.
- Business: recent canonical success/failure/uncertainty rates by operation, excluding client/business validation failures where appropriate.
- Security: credential/certificate validity, rotation/expiry and signature failures.
- Capability: certified connector/configuration/mapping version and supported operations.
- Capacity: provider quotas/rate limits/wallet availability only when an approved safe API and freshness policy exist.
- Operational: circuit state, dead-letter backlog, status-enquiry backlog and last successful evidence exchange.

Readiness projections include `Healthy`, `Degraded`, `Unavailable` or `Unknown`, contributing signals, policy version, observed time and freshness deadline. Integration Hub may automatically open a circuit or mark a route unavailable under an approved policy. It does not choose a financial replacement provider or resubmit an existing instruction.

Synthetic probes must be non-financial or explicitly approved sandbox probes. Production probes must not create customer, payment, bill, message or Ledger side effects.

## 15. Security and privacy

Threats include credential exfiltration, tenant/provider confusion, SSRF, endpoint substitution, forged/replayed callbacks, request tampering, response spoofing, unsafe deserialisation, XML/file attacks, log leakage, certificate expiry, downgrade, dependency compromise, confused-deputy service calls, replay abuse and operator misuse.

Controls:

- Resolve institution from trusted identity/context and bind it to all configurations and invocations.
- Authenticate internal callers with short-lived service credentials and strict audience/operation scopes.
- Store secret/key material only in approved secret/KMS/HSM facilities; use workload identity and least-privilege retrieval.
- Never offer a reveal-secret endpoint. Validate references through proof/use metadata, not by returning the value.
- Restrict endpoints to approved HTTPS hosts, ports, paths, DNS/IP/network policies and certificate rules; block link-local/private destinations unless explicitly approved.
- Pin or validate provider certificates/signatures according to approved specifications; do not invent custom crypto.
- Apply egress controls, timeouts, bounded payloads, content-type/schema validation and safe parsers.
- Encrypt approved raw evidence, separate its access role and record every sensitive access.
- Redact authorization headers, API keys, cookies, tokens, signatures, full account/identity numbers, payloads and private provider descriptions from telemetry.
- Protect webhook endpoints with body limits, rate controls, timestamp windows, event uniqueness and constant-time signature checks.
- Separate sandbox and production endpoints, identities, credentials, data and certification evidence. Sandbox uses synthetic or irreversibly masked data.
- Scan connector/provider dependencies and monitor certificate/credential expiry.

Security tests cover SSRF and DNS rebinding, cross-tenant reference substitution, secret-manager permission denial, expired/revoked certificates, forged signature, replay, event-ID/digest conflict, oversized/malformed/XML payload, deserialisation abuse, service-audience confusion, endpoint override, header injection, evidence access and redaction.

## 16. Audit and observability

Audit provider/configuration/mapping creation and publication, credential reference registration/rotation/revocation, certificate lifecycle, connector publication/certification/enablement, service/circuit change, invocation transitions, webhook validation/dispatch/replay, delivery repair and sensitive evidence access.

Evidence includes actor/service, permission/audience, institution/environment/provider/service/operation, configuration/connector/mapping/signature-policy versions, secret reference ID or thumbprint metadata, old/new state, reason, approval/certification refs, invocation/provider/business refs, safe digests, correlation/causation and timestamp. Never audit plaintext secrets or unrestricted bodies.

Minimum metrics:

- Calls by provider/service/operation/canonical outcome/submission state.
- Latency distributions separated by provider and operation.
- Pre-submit failures, pending, Uncertain, unknown status and enquiry age/backlog.
- Circuit state/transitions, health freshness and technical versus business failure signals.
- Credential/certificate days-to-expiry, validation failures and rotation status without secret details.
- Webhook accepted/rejected/duplicate/digest-conflict/dispatch/dead-letter/replay counts and lag.
- Delivery attempts, receipts, retry/dead-letter and age.
- Idempotent replay/conflict and blocked unsafe retry counts.
- Outbox/inbox lag and poison-message age.
- Certification result/version drift and enabled-but-not-ready detection.

Traces span the calling service, Integration Hub, provider invocation, callback, owner consumer and reconciliation/audit link. Use stable references and safe status tags only; do not record payload bodies or secret-bearing headers. Alerts and runbooks cover provider outage, uncertainty backlog, callback attack/replay, certificate/credential expiry, configuration drift, dead letters, failed certification and secret-manager outage.

## 17. Failure, retry and recovery

| Failure | Safe state | Recovery |
| --- | --- | --- |
| Missing/stale configuration | Invocation rejected before submission | Publish approved version; new/replayed owner request |
| Secret manager unavailable | No credential value cached beyond approved policy; no call | Retry safe resolution; alert; do not log secret ref internals |
| Certificate expired/revoked | Provider service unavailable | Approved rotation/re-enable flow |
| Pre-submit transport failure proven | `FailedBeforeSubmission` | Owner evaluates same-route/failover policy |
| Timeout after possible submission | `Uncertain` | Status enquiry using original refs; no blind retry |
| Unknown/conflicting response | `Unknown`/`Uncertain` | Preserve evidence, enquire/escalate |
| Webhook invalid/replayed | Rejected and security evidence retained | No dispatch; investigate/rotate if attack suspected |
| Valid webhook mapping fails | Persisted receipt, dispatch not applied | Fix versioned mapping and authorised replay |
| Internal consumer unavailable | Retry/dead-letter per policy | Consumer recovers; idempotent replay |
| Outbound delivery timeout | Pending/Uncertain per provider semantics | Receipt/status query or controlled retry if proven safe |
| Health worker fails | Readiness becomes stale/Unknown | Restart/backfill observation; route owners deny/degrade safely |
| Process crash after submission | Durable attempt remains Submitted/Uncertain | Resume status enquiry from invocation evidence |
| Outbox publish failure | Local state remains authoritative | Publisher retries with same event ID |
| Restore/PITR | External execution paused until consistency checks | Reconcile invocation/webhook/delivery/outbox state before resume |

Repair uses typed commands and immutable lineage. Operators cannot edit an invocation result, provider reference, receipt validation or delivery status directly. Restore exercises verify configuration versions, secret references, certificate lineage, idempotency records, active-attempt uniqueness, webhook identities, dispatch state, certification evidence and outbox/inbox continuity.

## 18. Configuration

| Setting | Owner | Secret | Validation | Reload behaviour |
| --- | --- | --- | --- | --- |
| Provider/service enabled state | Integration/Product owner | No | Approval, certification and dependencies | Versioned/effective-dated |
| Endpoint/network policy | Integration/Security | Endpoint may be sensitive | Allow-list, TLS, egress and environment | Versioned; drain old calls |
| Credential/key reference | Credential custodian | Reference is sensitive metadata | Exact scope/purpose/secret-manager proof | Versioned rotation |
| Certificate/key reference | Certificate administrator | Private key stays external | Chain/thumbprint/usage/validity | Versioned overlap/rotation |
| Timeout/retry/enquiry policy | Provider owner/Risk | No | Operation semantics and duplicate analysis | Versioned; certification required |
| Signature/replay profile | Security/Provider owner | Secret/key ref only | Algorithm/profile/timestamp/source rules | Versioned; overlap only if approved |
| Canonical/status mapping | Connector owner/Domain owner | No | Exhaustiveness/unknown handling/contracts | Versioned; recertify |
| Circuit/readiness policy | Operations/Route owner | No | Signal windows/freshness/thresholds | Versioned/audited |
| Webhook dispatch policy | Integration/Consumer owner | No | Retry/DLQ/idempotent consumer proof | Versioned |
| Evidence retention/classification | Security/Compliance | No | Data class/legal policy | Forward policy plus controlled purge |
| Certification plan | Provider owner/Security/QA | No | Exact spec/connector/environment cases | Immutable per run |

Missing, stale, expired or unapproved critical configuration disables the affected operation. Feature flags cannot bypass authentication, signature verification, idempotency, uncertainty, certification, audit or evidence controls.

## 19. Implementation slices

1. Approve provider-adapter ADRs, canonical status/submission model, secret/certificate lifecycle, webhook evidence policy and Phase 1 provider catalogue.
2. Create `CoreBanking.IntegrationHub` solution boundaries, architecture tests and versioned canonical contracts without live providers.
3. Implement provider/environment/service configuration, permissions, maker-checker publication and migrations.
4. Implement secret/certificate references, rotation lineage, expiry worker and non-reveal security tests against a synthetic secret store.
5. Implement invocation/idempotency/attempt persistence plus synthetic connector for success, pre-submit failure, timeout, late result and enquiry.
6. Implement NIBSS canonical operations and certified sandbox adapter once exact specifications are approved.
7. Implement PSP initialisation/verification/webhook adapter for each approved provider, one provider/version at a time.
8. Implement webhook ingress, raw-byte verification, durable receipt, deduplication, canonical dispatch and dead-letter handling.
9. Implement authorised replay/repair with Workflow integration and idempotent consumer contract tests.
10. Implement KYC/identity and VTU/biller adapter ports required by approved Phase 1 journeys.
11. Implement SMS/email transport adapters and USSD/WhatsApp protocol foundations needed by Epic 09, without channel business logic.
12. Implement health/readiness/circuit projections, freshness, expiry/configuration drift alerts and owner-facing query/event contracts.
13. Implement connector certification plans/runs/evidence and production enablement gate.
14. Implement audit/reconciliation evidence feed, dashboards and provider outage/uncertainty/webhook/rotation runbooks.
15. Complete performance, security, dependency, chaos, replay, restore and end-to-end evidence for enabled providers.

Each slice starts with traceable failing tests and keeps unapproved provider/environment/service combinations disabled. A synthetic adapter must demonstrate semantics before a live adapter is enabled.

## 20. Test plan

| Test ID | Requirement/story | Level | Scenario | Expected evidence |
| --- | --- | --- | --- | --- |
| INT-001 | US-004 | Integration | Register provider with secret reference | Reference only persisted; no secret exposure |
| INT-002 | US-004 / SEC | Security | Attempt to read secret/private key | Denied; access audited; no value in response/log |
| INT-003 | US-004 | Workflow | Publish production configuration without approval | Denied and remains disabled |
| INT-004 | US-004 | Security | Credential reference belongs to another tenant/environment | Denied without existence disclosure |
| INT-005 | US-004 | Recovery | Rotate active credential with approved overlap | New calls use new version; lineage retained |
| INT-006 | US-004 / US-069 | Operations | Certificate approaches approved threshold | Fresh alert with scoped metadata |
| INT-007 | API-001 | Contract | External/admin endpoint versioning | Versioned HTTPS/gateway contract passes |
| INT-008 | API-002 | Security | Wrong service audience/scope/mTLS identity | Denied before execution |
| INT-009 | API-003 | Idempotency | Concurrent same financial command | One invocation/submission outcome |
| INT-010 | API-003 | Idempotency | Same key, changed request digest | Conflict/audit; no provider call |
| INT-011 | API-004 | Security | Quota/rate/schema violation | Stable bounded rejection and metric |
| INT-012 | API-005 | Resilience | Proven pre-submit connection failure | Explicit not-submitted outcome |
| INT-013 | API-005 / NIB-004 | Resilience | Timeout after possible submission | Uncertain; status enquiry; no blind retry |
| INT-014 | API-005 | Resilience | Circuit opens during traffic | New eligible calls blocked; in-flight state preserved |
| INT-015 | API-005 | Contract | Unknown provider status code | Unknown/Uncertain plus alert; never guessed |
| INT-016 | API-006 / PSP-004 / US-068 | Security | Forged webhook signature | Rejected; no business dispatch |
| INT-017 | API-006 / US-068 | Security | Replayed event ID/same digest | Prior acknowledgement; one dispatch |
| INT-018 | API-006 / US-068 | Security | Same event ID/different digest | Reject/security alert; no overwrite |
| INT-019 | US-068 | Durability | Crash after durable receipt before dispatch | Resume dispatch once from stored state |
| INT-020 | US-068 | Recovery | Consumer repeatedly fails | Dead letter visible with immutable receipt |
| INT-021 | API-007 / US-068 | Recovery | Approved manual replay | New replay lineage; no duplicate consumer effect |
| INT-022 | API-007 | Contract | File/event connector poison input | Quarantined/DLQ with bounded safe evidence |
| INT-023 | API-008 | Security | Sandbox execution uses production credential/data | Blocked by environment controls |
| INT-024 | NIB-001 / US-026 | Contract | Name enquiry, transfer, status and recall ports | Exact canonical operations supported |
| INT-025 | NIB-002 | Contract | Required NIBSS fields and stable refs | Provider contract fixture passes |
| INT-026 | NIB-003 | Security | Invalid certificate/IP/endpoint environment | Execution denied |
| INT-027 | NIB-004 / US-026 | End-to-end | NIP late success after timeout | One original attempt; verified status event |
| INT-028 | NIB-005 | Reconciliation | Export NIBSS invocation evidence | Stable refs link payment and Ledger evidence |
| INT-029 | NIB-006 | Certification | Uncertified connector requested in production | Disabled until approved run |
| INT-030 | PSP-001 | Security | Client/caller attempts amount/fee/settlement override | Canonical contract/owner validation rejects |
| INT-031 | PSP-002 | Security | API response/log inspection | Only approved public ID; no secret key |
| INT-032 | PSP-003 / US-027 | Integration | Redirect/callback claims success | Server verification required before final event |
| INT-033 | PSP-004 / US-027 | Security | PSP callback replay/forgery | No duplicate/final outcome |
| INT-034 | PSP-005 | Scope | Disabled virtual-account/split capability called | Capability unavailable |
| INT-035 | PSP-006 | Reconciliation | Export PSP fee/transaction evidence | Traceable records without direct Ledger write |
| INT-036 | US-069 | Domain | Technical health passes but business failures rise | Signals remain distinct; readiness policy applied |
| INT-037 | US-069 | Domain | Health observation becomes stale | State becomes Unknown, not Healthy |
| INT-038 | US-069 | Integration | Route owner receives unhealthy event | Fresh/versioned signal; no automatic resubmission |
| INT-039 | DAT/SEC | Security | SSRF/private endpoint/DNS rebinding attempt | Rejected/blocked and audited |
| INT-040 | SEC | Security | Oversized/malformed/XML/deserialisation payload | Bounded rejection; no unsafe parsing |
| INT-041 | Messaging | Replay | Replay Integration outbox/events | Consumer/invocation effect remains singular |
| INT-042 | Recovery | Restart during secret rotation/invocation/webhook dispatch | Durable versions/states resume safely |
| INT-043 | Recovery | Restore Integration Hub database | Execution paused, consistency verified, no duplicate call |
| INT-044 | Architecture | Scan domain/contracts for provider SDK/DTO types | No forbidden dependency/leakage |
| INT-045 | End-to-end | Payment -> Integration -> provider -> callback -> owner | Correlated canonical evidence; no direct balance write |
| INT-046 | End-to-end | KYC/VTU/message synthetic adapters | Owner semantics isolated from provider protocol |

Provider contract tests use approved sandbox specifications and synthetic fixtures. Fault injection covers failure before submission, after submission, before response persistence, before outbox publication, during callback dispatch and after consumer acknowledgement.

## 21. Acceptance criteria

- [ ] API-001 through API-008, NIB-001 through NIB-006, PSP-001 through PSP-006 and US-004/068/069 trace to decisions, contracts, code and tests.
- [ ] All backend implementation is C#/.NET and provider SDKs/DTOs remain in Integration Hub Infrastructure adapters.
- [ ] Provider/environment/service configuration is tenant-isolated, versioned, approved, effective-dated and certification-gated.
- [ ] Only opaque credential/key references and approved certificate metadata are stored; secret/private-key values are absent from data, telemetry and contracts.
- [ ] Canonical contracts preserve submission state, uncertainty, original references, mapping/version evidence and unknown status safely.
- [ ] Unsafe retries and failover after possible submission are blocked until status enquiry verifies the original attempt.
- [ ] Webhooks are authenticated, persisted, deduplicated and asynchronously dispatched with dead-letter and approved replay support.
- [ ] Redirect/callback claims cannot create a final provider fact without required server verification.
- [ ] Health separates technical, business, security and capability signals and labels observation freshness.
- [ ] Production enablement proves exact connector/configuration/mapping/certificate/credential/certification versions.
- [ ] Integration Hub does not mutate balances or another service's private state/database.
- [ ] Security, audit, observability, replay, outage and restore evidence is complete for every enabled provider.
- [ ] Phase 3 partner APIs, corporate webhooks, virtual accounts and other deferred capabilities cannot be enabled accidentally.

## 22. Definition of Done

- Build, analyzers, formatting, architecture tests and affected suites pass using the repository-pinned .NET SDK.
- EF Core migrations apply to empty and supported upgrade databases; tenant, idempotency, event-identity, version/state and one-active-attempt constraints pass PostgreSQL/Testcontainers tests.
- Canonical HTTP/event contracts pass compatibility review with each Phase 1 calling service.
- Secret/certificate threat model, egress/SSRF controls, webhook verification and sensitive-evidence review are approved.
- Each enabled adapter passes synthetic, sandbox, failure, uncertainty, replay, late-result and recovery contract tests against the approved provider specification version.
- Certificate/credential expiry, provider outage, uncertainty backlog, webhook attack, dead-letter and configuration-drift dashboards/alerts are demonstrated.
- Manual replay, credential rotation, connector rollback/forward-fix and database restore runbooks are exercised with retained evidence.
- Traceability records requirement/story, provider/spec, connector/mapping/configuration, permission, API/event, migration, test, certification and operational evidence.

## 23. Open decisions

| Decision | Impact | Owner | Required by | Status |
| --- | --- | --- | --- | --- |
| Phase 1 provider/service catalogue per institution | Scope and certification | Product/Integration | Slice 1 | Open |
| Canonical operation/status/submission taxonomy | All owner contracts | Architecture/Domain owners | Slice 1 | Open |
| Secret manager, KMS/HSM and workload identity | Credential security | Security/Platform | Slice 1 | Open |
| Certificate validation, signing/encryption and rotation profiles | NIBSS/provider security | Security/Provider owner | Slice 4/6 | Open |
| NIBSS specification, endpoints, fields, session refs and enquiry/reversal semantics | Payment correctness | Integration/Payments/NIBSS owner | Slice 6 | Open |
| Paystack/Flutterwave specifications, verification and webhook semantics | Collection correctness | Integration/Payments | Slice 7 | Open |
| KYC/VTU/biller/SMS/email Phase 1 providers and contracts | Enabled journeys | Product/Domain owners | Slices 10/11 | Open |
| Per-operation timeout/retry/idempotency/enquiry policy | Duplicate/uncertainty safety | Provider owner/Risk | Before live adapter |
| Webhook raw evidence retention/encryption/access | Security/audit/privacy | Security/Compliance | Slice 8 | Open |
| Webhook acknowledgement and retry semantics per provider | Availability/duplicates | Provider owner | Slice 8 | Open |
| Health signals, windows, thresholds and freshness | Routing/readiness | Operations/Route owners | Slice 12 | Open |
| Certification plans, evidence and approver authority | Production gate | QA/Security/Provider owner | Slice 13 | Open |
| Connector deployment/rollback and version coexistence | Safe release | Platform/Integration | Before production |
| Provider evidence export and reconciliation keys | Financial traceability | Integration/Reconciliation | Slice 14 | Open |
| Retention, residency and legal-hold rules | Data governance | Compliance/Security | Before production |
| Capacity, latency, availability, RTO/RPO | Release gates | Product/Platform | Before release testing | Open |

## 24. Prohibited assumptions

- Do not invent provider endpoints, credentials, certificates, fields, status codes, signing/encryption algorithms, retries, timeouts, SLAs or certification results.
- Do not store, return, log or event plaintext secrets/private keys, even in non-production.
- Do not accept arbitrary URLs, headers, HTTP methods, provider payloads, adapter names or credential references from a caller.
- Do not let provider SDK or DTO types cross Infrastructure into Domain, Application or public Contracts.
- Do not map timeout or unknown status to failure merely to permit retry/failover.
- Do not submit another financial request while the original may have been accepted.
- Do not treat a webhook, redirect, callback, HTTP 200 or provider text description alone as an authoritative financial outcome.
- Do not allow a webhook body to select its institution, secret, consumer or verification profile.
- Do not overwrite webhook receipts, invocation attempts, certification evidence or replay history.
- Do not treat a successful health probe as proof of business success or a stale observation as healthy.
- Do not let Integration Hub choose a financial provider route or change Payment, Customer, Channel, Workflow or Ledger state directly.
- Do not implement generic retry over unsafe provider POST requests.
- Do not use production credentials or real customer data in sandbox/certification fixtures.
- Do not enable a connector version/configuration combination that was not exactly certified and approved.
- Do not expose Phase 3 partner/corporate/open-banking/virtual-account capabilities as incidental Phase 1 work.

Until exact provider and policy decisions are approved, use synthetic adapters and keep the provider/service/environment disabled.

## 25. Agent implementation instructions

- Required reading: root `AGENTS.md`, architecture, Phase 0 evidence, Epics 01-07, this epic, API/NIB/PSP/SEC/DAT requirements, US-004/068/069 and the relevant owning-service story/contract.
- Expected C# boundaries: `CoreBanking.IntegrationHub.Domain`, `.Application`, `.Infrastructure`, `.Api`, `.Contracts`, `.Workers` and `.Tests`.
- Domain must not reference ASP.NET Core, EF Core, HTTP/provider SDKs, broker clients, secret-store clients or another service implementation.
- Create operation-specific ports and strongly typed canonical contracts; reject a generic object/dictionary connector surface.
- Keep provider request/response DTOs, SDK wrappers, authentication/signing and status mapping inside provider-specific Infrastructure folders.
- Use strongly typed IDs, `DateTimeOffset`, `TimeProvider`, cancellation tokens, request digests, optimistic concurrency and append-only attempt/evidence models.
- Persist invocation/idempotency before network I/O and never hold a database transaction during an external call.
- Treat cancellation/timeout after possible submission as a durable uncertain outcome, not an exception that loses state.
- Resolve secrets at the narrowest execution boundary, dispose sensitive buffers where applicable and test telemetry/redaction explicitly.
- Use transactional outbox/inbox and stable IDs for canonical outcomes, webhook dispatch, health changes, replay and delivery receipts.
- Implement synthetic adapters first. Start each live provider with failing contract fixtures from an approved specification; never reverse-engineer production behaviour into undocumented rules.
- Add architecture tests that prevent provider DTO/SDK leakage, direct references to Ledger/Payments/Channels infrastructure and secret-shaped contract/database fields.
- Add PostgreSQL/Testcontainers concurrency tests for idempotency, one-active-attempt, event identity, replay and configuration activation.
- Stop when work requires an unapproved provider specification, cryptographic profile, retry/enquiry semantic, evidence retention rule, health threshold, certification decision or Phase 3 capability.
- Completion report must list requirements/stories, enabled provider/spec versions, connector/mapping/configuration versions, permissions, APIs/events, migrations, secret/certificate handling, test commands/results, certification, resilience/replay/restore evidence, assumptions, risks and rollout/rollback notes.

## 26. Traceability summary

| Source | Primary implementation evidence |
| --- | --- |
| API-001..API-004 | Versioned controlled APIs, approved authentication, idempotency/correlation, quotas, validation and audit |
| API-005 | Operation-specific timeout, retry, circuit and status-enquiry policies with uncertainty preserved |
| API-006 / US-068 | Raw-byte verification, source/time/event replay controls, durable receipt, DLQ and replay |
| API-007 | API/file/event connector foundations, outbox/inbox, replay and dead-letter repair |
| API-008 | Environment isolation, synthetic/masked sandbox data and non-production credentials |
| NIB-001..NIB-006 | Canonical NIP operations, required refs, secure cert/network config, uncertainty/enquiry, reconciliation evidence and certification |
| PSP-001..PSP-006 | Server-side canonical initialisation, secret isolation, status verification, webhook security, capability gating and reconciliation evidence |
| US-004 | Secret/key references, certificates, clients, rotation, expiry and environment isolation |
| US-068 | Verify, persist, deduplicate, dispatch, dead-letter and manually replay webhook events |
| US-069 | Fresh technical/business/security/capability health, alerts and route-readiness projection |

Supporting owner stories such as US-013, US-026 through US-028, US-032 through US-035 and US-037 consume this epic's connector capabilities but retain their own business outcomes and acceptance authority.
