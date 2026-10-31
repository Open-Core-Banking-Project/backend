---
phase: "Phase 1 - Banking Foundation"
epic: "Customer and Onboarding"
owning_service: "Customer and Onboarding"
participating_services:
  - Identity and Access
  - Core Banking and Ledger
  - Operations and Workflow
  - Risk and Reporting
  - Integration Hub
  - Audit and Reconciliation
modules:
  - Party Registry
  - Individual Onboarding
  - Duplicate Detection and Merge
  - KYC Cases and Requirements
  - Identity Verification
  - Customer Classification
  - Customer Lifecycle
  - Customer 360
  - Preferences and Relationships
status: Implemented
requirements: [CUS-001, CUS-002, CUS-003, CUS-004, CUS-005, CUS-006, CUS-007, CUS-008, CUS-009, CUS-010, API-001, API-002, API-004, API-005, DAT-001, DAT-005, DAT-006, DAT-008, RPT-002, RPT-007, SEC-008, SEC-009, SEC-010, SEC-011, SEC-013, SEC-014, SEC-015, SEC-017, SEC-018, AUD-001, AUD-002, AUD-003, NFR-OBS-001, NFR-OBS-002, NFR-RES-001, NFR-DR-001, NFR-MNT-001]
stories: [US-011, US-013, US-014, US-015]
dependencies:
  - Phase 0 - Messaging, Outbox, Inbox and Idempotency
  - Phase 0 - Observability and CI
  - Phase 1 - Institution and Tenancy
  - Phase 1 - Identity, Roles and Permissions
last_updated: 2026-10-30
---

# Epic: Customer and Onboarding

## 1. Objective

Deliver a secure, tenant-isolated source of truth for individual parties and customers, from onboarding capture through duplicate review, KYC verification, compliance classification, approval and controlled customer lifecycle management.

The epic also delivers an authorised Customer 360 composition that helps staff serve a customer without copying account balances, payments, cases, documents or alerts into a new source of truth.

Phase 1 supports individual onboarding. The data model may reserve explicit extension points for organisations, but corporate onboarding, ownership structures, signatory mandates and corporate approvals from US-012 remain Phase 2 scope.

## 2. Success measures

- Required onboarding fields and evidence are selected from the exact effective country-pack, customer-type, KYC-tier and risk-policy versions.
- Duplicate detection runs before activation and produces explainable candidates; no record is merged automatically.
- BVN, NIN, address, document and approved liveness checks use canonical provider contracts through Integration Hub.
- Every provider attempt has an unambiguous state, protected raw evidence reference and canonical result.
- A customer cannot become Active until required checks, reviews and approvals succeed.
- KYC tier, customer segment, risk class, review date and enhanced-due-diligence status are versioned and approved when policy requires.
- Sensitive customer fields are encrypted or tokenised at rest, masked by default and disclosed only with explicit permission, purpose and audit evidence.
- Cross-tenant, wrong-branch, missing-permission and maker-self-approval tests deny access without revealing resource existence.
- Customer 360 identifies source, freshness and unavailable sections and never represents a read model as an authoritative balance or transaction source.
- Provider failure, event replay, concurrent duplicate submissions and recovery do not create duplicate customers or erase evidence.

Production targets for onboarding completion time, provider response, duplicate-review turnaround, Customer 360 latency, availability, recovery and data retention require institution approval. Do not encode examples as service-level objectives.

## 3. Scope

### Included

- Individual party and customer profiles.
- Names, birth details, contact points, addresses, next-of-kin and non-payment personal-beneficiary relationships required by approved policy.
- Onboarding applications with resumable steps, validation snapshots, decisions and expiry.
- Country-pack and KYC-tier-driven requirement evaluation.
- Exact and probabilistic duplicate-candidate generation with human review.
- Approved customer merge with aliases, redirects, history and downstream notification.
- KYC cases, requirements, document references and evidence status.
- BVN, NIN, tax, address, document and approved liveness-verification orchestration through Integration Hub.
- Canonical verification outcomes and secure raw-evidence references.
- Customer segment, KYC tier, risk class, enhanced-due-diligence status and review schedule.
- Customer states including Pending, Active, Restricted, Dormant, Deceased and Closed.
- Relationship-manager assignment and service/communication preferences.
- Authorised and masked Customer 360 composition.
- Consent and lawful-processing evidence needed by onboarding and preferences.
- Data correction with previous values and audit evidence.

### Excluded

- Corporate customer onboarding, directors, shareholders, beneficial owners, signatories and mandates from US-012.
- Account opening, balance, holds, liens and account restrictions, owned by Core Banking and Ledger.
- Saved transfer/payment beneficiaries and beneficiary verification, owned by Payments and VTU. A personal beneficiary relationship captured for onboarding, legal or next-of-kin purposes does not authorise payment.
- Authentication credentials, sessions and roles, owned by Identity and Access.
- Provider-specific KYC protocols, credentials, routing and webhook verification, owned by Integration Hub.
- Sanctions/PEP/watchlist engines, fraud models and alert investigations, owned by Risk and Reporting. Phase 1 consumes canonical results where approved.
- Workflow queues, approver routing, escalation and SLA management, owned by Operations and Workflow.
- Document binary storage and document rendering. This service owns metadata and references; the approved document/object-storage capability owns content.
- Regulatory policy, required documents, accepted providers, risk thresholds, retention periods and KYC limits not explicitly approved in the country pack.
- Direct balance, transaction or payment aggregation into the customer database.

## 4. Ownership and trust boundaries

| Capability | Authoritative owner | Collaboration |
| --- | --- | --- |
| Party, customer profile and customer status | Customer and Onboarding | Identity link and institution/branch references |
| Onboarding application and requirement snapshot | Customer and Onboarding | Institution country-pack/configuration versions |
| Duplicate candidates and merge decision | Customer and Onboarding | Workflow approval and downstream consumers |
| KYC case and canonical verification outcome | Customer and Onboarding | Integration Hub executes provider protocol |
| Provider credentials, routing, timeout, retry and webhook evidence | Integration Hub | Customer supplies verification request and consumes outcome |
| Customer risk signals/scores and screening outcomes | Risk and Reporting | Customer stores approved classification/reference |
| KYC tier, segment and customer classification | Customer and Onboarding | Compliance approval through Workflow |
| Approval orchestration | Operations and Workflow | Customer applies exact approved version/decision |
| Accounts and balances | Core Banking and Ledger | Customer 360 consumes authorised API/read contract only |
| Authentication identity and entitlements | Identity and Access | Customer links by stable identity reference |
| Document binary and malware-scan lifecycle | Approved document storage component | Customer owns purpose and metadata references |
| Immutable enterprise audit | Audit and Reconciliation | Source service commits evidence/outbox first |

Trust rules:

1. Request bodies cannot choose institution, approval result, KYC outcome, provider outcome, risk class or customer status.
2. Provider responses are evidence, not commands. Customer and Onboarding maps them using the exact approved policy version.
3. A verified identity number does not itself activate a customer; every required rule and approval must pass.
4. Customer 360 is a composition, not a master copy of every domain.
5. Only Core Banking and Ledger can authoritatively report or alter balances.
6. A frontend-visible duplicate score, provider result or masked field is not sufficient authorisation for an action.

## 5. Actors, roles and permissions

Role names are illustrative. Application code enforces stable permissions plus scope.

| Actor | Permission | Contextual scope | Step-up | Approval |
| --- | --- | --- | --- | --- |
| Onboarding officer | `customer.application.create` | Institution/branch/channel | For sensitive capture by policy | None |
| Onboarding officer | `customer.application.update` | Institution/branch/assigned application | According to field | None |
| Onboarding officer | `customer.application.submit` | Institution/branch/assigned application | Required by policy | None |
| Onboarding supervisor | `customer.application.review` | Institution/branch/queue | Required | Cannot approve own restricted work |
| KYC officer | `customer.kyc.verification.request` | Institution/case/check type | For high-risk checks | None |
| KYC officer | `customer.kyc.view` | Institution/branch/case/classification | Sensitive view audited | None |
| Compliance officer | `customer.kyc.classification.propose` | Institution/risk tier | Required | Approval by policy |
| Compliance approver | `customer.kyc.classification.approve` | Institution/risk tier | Required | Cannot approve own proposal |
| Duplicate reviewer | `customer.duplicate.review` | Institution/branch/candidate | Required | None |
| Merge proposer | `customer.merge.propose` | Institution/customer pair | Required | Always approved |
| Merge approver | `customer.merge.approve` | Institution/customer pair | Required | Cannot be proposer |
| Customer service officer | `customer.profile.view` | Institution/branch/relationship | Sensitive fields masked | None |
| Authorised investigator | `customer.profile.sensitive.view` | Institution/case/purpose | Required | Reason and audit required |
| Customer data steward | `customer.profile.correct` | Institution/field classification | Required | Approval for protected fields |
| Operations officer | `customer.status.change` | Institution/customer/transition | Required for sensitive states | Approval by transition policy |
| Relationship manager admin | `customer.relationship_manager.assign` | Institution/branch | Required for reassignment | Policy-based |
| Customer/channel identity | `customer.preference.self.manage` | Own customer/allowed preference | According to channel | None |
| Service identity | Explicit `service.customer.<action>` | Audience/institution/purpose | Service authentication | No human role |

Rules:

- `customer.profile.view` never implies unmasking. Sensitive disclosure needs the specific permission, valid purpose and resource scope.
- Branch scope is enforced from trusted entitlements and current customer relationship, not a body/query parameter.
- Proposer and approver separation uses stable actor ID across roles, sessions and channels.
- Service identities receive only canonical integration permissions and cannot use human permissions.
- Bulk export is a separate permission and is not included in Customer 360 access.

## 6. Domain model

### Aggregates and entities

| Concept | Responsibility | Identity | Important relationships |
| --- | --- | --- | --- |
| `Party` | Stable person identity and protected core attributes | `PartyId` | Owns individual, names, contacts, addresses and relationships |
| `Individual` | Individual-specific demographic data | `PartyId` | One-to-one with Party in Phase 1 |
| `Customer` | Institution-specific banking relationship and lifecycle | `CustomerId` | References Party, institution, branch and identity |
| `OnboardingApplication` | Resumable capture and approval lifecycle | `ApplicationId` | Produces/links Party and Customer |
| `ApplicationRequirementSnapshot` | Exact required fields/evidence/policies evaluated | `RequirementSnapshotId` | Country pack, tier and policy versions |
| `DuplicateCandidateSet` | Explainable possible matches and review result | `CandidateSetId` | Application/party and candidate parties |
| `CustomerMergeRequest` | Controlled survivor/source merge | `MergeRequestId` | Workflow approval, aliases and downstream convergence |
| `KycCase` | Required verification and compliance work | `KycCaseId` | Customer/application and requirement snapshot |
| `KycRequirement` | One required evidence/check item and status | `KycRequirementId` | Case, type, policy source and evidence refs |
| `IdentityVerification` | Canonical check request/result | `VerificationId` | Has immutable provider attempts |
| `VerificationAttempt` | One provider submission and outcome | `VerificationAttemptId` | Provider route ref, external ref and raw evidence ref |
| `DocumentReference` | Purpose, version, hash and storage reference metadata | `DocumentReferenceId` | Application/case/requirement; no binary data |
| `CustomerClassification` | Versioned segment, KYC tier, risk and EDD status | `ClassificationVersionId` | Customer, effective period and approval |
| `CustomerStatusHistory` | Append-only lifecycle transitions | `CustomerStatusHistoryId` | Customer and transition reason |
| `RelationshipManagerAssignment` | Effective-dated staff relationship | `AssignmentId` | Customer and external staff identity ref |
| `ServicePreference` | Versioned permitted customer preference | `PreferenceId` | Customer, purpose, source and consent evidence |
| `CustomerAlias` | Redirect from merged or legacy identity | `CustomerAliasId` | Alias to surviving customer |

### Value objects

- `PersonName`, `BirthDetails`, `CountryCode`, `Nationality` and approved demographic classifications.
- `ContactPoint` and `Address` with type, verification and effective period.
- `ProtectedIdentifier`: type, encrypted/tokenised value reference, masked display, lookup token and verification state.
- `CustomerNumber`, `ApplicationReference`, `ProviderReference` and `CorrelationId`.
- `KycTier`, `RiskClass`, `CustomerSegment`, `DueDiligenceStatus` and `ReviewDate`.
- `VerificationType`, `CanonicalVerificationOutcome`, `MatchStrength` and `EvidenceReference`.
- `ConsentPurpose`, `LawfulBasis`, `PreferenceChannel` and `EffectivePeriod`.
- `DataClassification`, `MaskingPolicy`, `DisclosurePurpose` and `RetentionClass`.

### Invariants

1. Every tenant-owned aggregate carries a trusted `InstitutionId` and cannot reference a tenant-owned row from another institution.
2. A Party is not automatically a Customer; a Customer represents one institution-specific relationship.
3. An application uses an immutable requirement snapshot after submission. Policy changes create re-evaluation work, not silent rule changes.
4. Required fields and evidence must come from approved country-pack/KYC policy; code must not hard-code Nigerian or other jurisdictional rules as universal.
5. Duplicate checks run before activation. A score creates a candidate, never an automatic merge or rejection.
6. Merge requires an approved survivor, reason and immutable mapping. Source history is retained and cannot be reused as a new customer.
7. Raw BVN, NIN, tax identifiers and similarly sensitive values never appear in logs, events, URLs or general search indexes.
8. Provider results are immutable evidence. Corrections create a new attempt or decision linked to the original.
9. A provider success maps to a canonical result only through a versioned mapping. It does not bypass remaining KYC requirements.
10. A verification retry/failover cannot create two accepted outcomes without reconciliation.
11. Active customer status requires completed mandatory checks, current approved classification and required workflow approval.
12. Classification changes are versioned, effective-dated and preserve the prior version.
13. High-risk/EDD policy creates explicit outstanding requirements and cannot be cleared by UI-only state.
14. Sensitive-data disclosure is masked by default and always records purpose, permission, actor and correlation.
15. Customer correction preserves before/after evidence; verified identifiers require stricter workflow.
16. Customer 360 cannot mutate source systems and cannot label stale/unavailable balance data as current.
17. Closing, death marking, restriction or merge never deletes financial, KYC or audit history.
18. No Customer and Onboarding operation alters an account balance.

### Onboarding application lifecycle

```text
Draft -> Submitted -> DuplicateReview -> VerificationPending -> ComplianceReview
   |         |                |                    |                  |
   +-> Expired               Rejected             Rejected           |
   +-> Withdrawn                                                       v
                                              PendingApproval -> Approved -> Completed
                                                               -> Rejected
```

- A step may be skipped only when the immutable requirement snapshot proves it is not required.
- Resubmission creates a new revision linked to prior decisions; rejected history remains visible.
- `Completed` means the onboarding workflow finished, not that every downstream service has created its read model.

### KYC requirement and verification lifecycle

```text
Required -> EvidencePending -> Ready -> InProgress -> Passed
                                      |            -> Failed
                                      |            -> Inconclusive -> ManualReview
                                      +-> Waived (explicit authority only)

VerificationAttempt:
Created -> Submitted -> Pending -> Succeeded
                    |           -> Failed
                    |           -> Uncertain -> StatusEnquiry -> Succeeded/Failed/ManualReview
                    +-> CancelledBeforeSubmission
```

Timeout after submission is `Uncertain`; it is not proof of failure and does not permit blind duplicate submission or provider failover.

### Customer lifecycle

```text
Pending -> Active -> Restricted -> Active
                  -> Dormant -> Active
                  -> Deceased
                  -> Closed
Pending -> Rejected
```

- `Deceased` and `Closed` transitions and any reactivation require approved policy; do not infer reversibility.
- Customer status does not directly mutate account status. Core Banking consumes the event and applies its own approved rules.

### Merge lifecycle

```text
Proposed -> Analysed -> PendingApproval -> Approved -> Applying -> Completed
                     |                 -> Rejected
                     +-> Withdrawn
Applying -> ReconciliationRequired -> Completed
```

The survivor and source cannot change after approval. Each consumer acknowledges the merge idempotently; unresolved consumers remain visible.

## 7. Core use cases

### CUS-UC-01 - Start and submit individual onboarding

- Actor: Onboarding officer or approved customer channel.
- Permission: `customer.application.create`, `customer.application.update`, `customer.application.submit`.
- Flow: Resolve institution/country-pack policy, create application, capture allowed fields explicitly, upload document references, evaluate requirements, run duplicate checks and submit the exact revision.
- Idempotency: Institution, channel/actor, operation and client request ID plus payload hash.
- Transaction: Application revision, requirement snapshot, audit and outbox commit locally.
- Failures: `onboarding_policy_unavailable`, `onboarding_required_field_missing`, `onboarding_revision_conflict`, `onboarding_duplicate_review_required`.

### CUS-UC-02 - Review duplicate candidates

- Actor: Duplicate reviewer.
- Flow: View masked candidates and explainable match factors, mark distinct/same/insufficient evidence, or propose merge.
- Guard: Reviewer cannot see unrelated full profiles; no automatic merge; negative decision preserves candidates and model/rule version.
- Concurrency: Candidate set version and current party/customer states are rechecked at decision.

### CUS-UC-03 - Request identity verification

- Actor: KYC officer or onboarding automation service.
- Flow: Validate required check and protected subject data, create canonical verification, emit request to Integration Hub, receive attempt/status/result events, map provider response, attach evidence and update the requirement.
- Idempotency: One logical verification ID; each provider attempt has a unique attempt ID and provider idempotency reference where supported.
- Failure: Pre-submit technical failure may retry safely; post-submit timeout becomes `Uncertain` and requires status enquiry.
- Data: Integration event contains only the minimum protected/token reference or encrypted provider payload contract approved for the adapter.

### CUS-UC-04 - Assign KYC tier and risk classification

- Actor: Compliance officer and separate approver.
- Flow: Combine completed requirements and approved risk/screening references, propose classification with reason/review date, submit to Workflow, then apply the exact approved version/hash.
- Guard: High-risk classification creates EDD state/requirements; maker cannot approve; provider or model recommendation cannot self-publish.
- Events: `CustomerClassificationChanged.v1` after commit.

### CUS-UC-05 - Activate or change customer status

- Actor: Authorised operations/compliance actor through Workflow when required.
- Flow: Validate current state, mandatory KYC/classification, decision and reason; apply transition; publish status event.
- Guard: No direct account mutation. Consumers decide account/channel consequences within their own policies.
- Concurrency: Expected customer version; duplicate approval decision returns original outcome.

### CUS-UC-06 - Correct protected customer data

- Actor: Customer data steward and approver where required.
- Flow: Capture correction reason/evidence, validate field policy, create new value version, invalidate affected verifications/classification if policy requires, publish minimal change event.
- Guard: Never overwrite evidence or silently retain an invalid verification as current.

### CUS-UC-07 - Merge duplicate customers

- Actor: Merge proposer and separate approver.
- Flow: Analyse references, select survivor, freeze conflicting mutations, approve exact plan, create aliases, migrate locally owned relationships with history, publish merge event and track consumer acknowledgements.
- Guard: No cross-tenant merge; no deletion; no direct edits to accounts/ledger; no merge while unresolved legal/financial decision forbids it.
- Recovery: `ReconciliationRequired` remains visible until all required consumers converge or an authorised remediation is recorded.

### CUS-UC-08 - View Customer 360

- Actor: Customer service officer or other specifically authorised actor.
- Flow: Authorise customer and section scopes, return authoritative local profile, compose permitted remote summaries through versioned APIs/read models, mask by default and record sensitive access.
- Output: Each section states source service, observed-at time, classification and availability/freshness status.
- Failure: Partial response may be returned only with explicit unavailable/stale sections; never invent zero balances or empty history.

### CUS-UC-09 - Manage preferences and relationship manager

- Actor: Customer for allowed self-service preferences; authorised staff for assignments.
- Flow: Validate purpose/consent/lawful basis, version preference or effective assignment, and publish minimal event.
- Guard: Operational/legally required messages cannot be disabled through a marketing preference unless approved policy says so.

## 8. API contracts

| Method | Route | Permission | Request | Response | Idempotent |
| --- | --- | --- | --- | --- | --- |
| POST | `/api/v1/onboarding/applications` | `customer.application.create` | `CreateIndividualApplicationRequest` | `ApplicationResponse` | By request ID |
| PATCH | `/api/v1/onboarding/applications/{id}` | `customer.application.update` | `UpdateApplicationRequest` plus expected version | `ApplicationResponse` | By version/request ID |
| POST | `/api/v1/onboarding/applications/{id}/documents` | `customer.application.update` | `RegisterDocumentReferenceRequest` | `DocumentReferenceResponse` | By request ID |
| POST | `/api/v1/onboarding/applications/{id}/submit` | `customer.application.submit` | Revision and confirmation | `ApplicationResponse` | Yes |
| GET | `/api/v1/onboarding/applications/{id}` | `customer.application.view` | Section selection | `ApplicationResponse` | Yes |
| GET | `/api/v1/onboarding/applications/{id}/duplicate-candidates` | `customer.duplicate.review` | Bounded pagination | `DuplicateCandidatePage` | Yes |
| POST | `/api/v1/onboarding/duplicate-candidates/{id}/decisions` | `customer.duplicate.review` | `DuplicateDecisionRequest` | `DuplicateDecisionResponse` | By decision ID |
| POST | `/api/v1/customers/merge-requests` | `customer.merge.propose` | `CreateMergeRequest` | `MergeRequestResponse` | By request ID |
| GET | `/api/v1/kyc/cases/{id}` | `customer.kyc.view` | Masking context | `KycCaseResponse` | Yes |
| POST | `/api/v1/kyc/cases/{id}/verifications` | `customer.kyc.verification.request` | `RequestVerificationRequest` | `VerificationResponse` | By request ID |
| POST | `/api/v1/customers/{id}/classification-proposals` | `customer.kyc.classification.propose` | `ClassificationProposalRequest` | `ClassificationResponse` | By request ID |
| POST | `/api/v1/customers/{id}/status-proposals` | `customer.status.change` | `StatusProposalRequest` | `StatusProposalResponse` | By request ID |
| GET | `/api/v1/customers/{id}` | `customer.profile.view` | Field set | `CustomerProfileResponse` | Yes |
| GET | `/api/v1/customers/{id}/360` | `customer.profile.view` plus section permissions | Requested sections | `Customer360Response` | Yes |
| POST | `/api/v1/customers/{id}/sensitive-disclosures` | `customer.profile.sensitive.view` | Fields and purpose | `SensitiveDisclosureResponse` | No; always audited |
| POST | `/api/v1/customers/{id}/corrections` | `customer.profile.correct` | `CustomerCorrectionRequest` | `CorrectionResponse` | By request ID |
| PUT | `/api/v1/customers/{id}/preferences/{purpose}` | Self or `customer.preference.manage` | `SetPreferenceRequest` | `PreferenceResponse` | By version/request ID |
| POST | `/api/v1/customers/{id}/relationship-manager-assignments` | `customer.relationship_manager.assign` | `AssignmentRequest` | `AssignmentResponse` | By request ID |
| POST | `/internal/v1/kyc/verifications/{id}/attempt-results` | Integration Hub service permission | Canonical attempt result/evidence ref | `VerificationResponse` | By attempt event ID |
| POST | `/internal/v1/customer-changes/{id}/approval-decisions` | Workflow service permission | Exact decision/version/hash | Change-specific response | By decision ID |

Contract rules:

- Institution, branch authority, actor, permissions and approval status come from trusted context or service contracts, not request fields.
- Map accepted fields explicitly to prevent mass assignment.
- Raw protected identifiers are allowed only in approved encrypted body fields, never paths, query strings, logs or error details.
- Expected failures use RFC 7807 with stable codes. Authentication/duplicate errors must not enable enumeration.
- Collection endpoints use bounded pagination and allow-listed filters; unrestricted PII search/export is prohibited.
- Customer 360 field/section permissions are evaluated independently and response metadata identifies source and freshness.
- Public APIs are versioned HTTPS contracts. Examples contain synthetic data only.

## 9. Event contracts

### Produced events

| Event | Trigger | Key fields | Ordering key | Consumers |
| --- | --- | --- | --- | --- |
| `OnboardingApplicationSubmitted.v1` | Exact revision submitted | Application ID, institution/branch, requirement snapshot ID | Application ID | Workflow, Audit |
| `KycVerificationRequested.v1` | Verification committed | Verification/check/attempt IDs, institution, protected subject ref | Verification ID | Integration Hub |
| `KycVerificationResolved.v1` | Canonical result finalised | Verification ID, type, canonical outcome, evidence ref, policy version | Verification ID | Risk, Workflow, Audit |
| `CustomerCreated.v1` | Customer record established | Customer/party IDs, institution, branch, status | Customer ID | Identity, Core, Risk, Audit |
| `CustomerActivated.v1` | Customer becomes Active | Customer ID, institution, classification version | Customer ID | Core, Payments, Channels, Risk, Audit |
| `CustomerStatusChanged.v1` | Controlled status transition | Customer ID, old/new status, reason category | Customer ID | Core, Payments, Channels, Risk, Audit |
| `CustomerClassificationChanged.v1` | Approved version effective | Customer ID, tier/segment/risk/EDD flags, review date, version | Customer ID | Core, Payments, Risk, Audit |
| `CustomerProfileChanged.v1` | Approved material profile version | Customer ID, changed field categories/version only | Customer ID | Approved consumers, Audit |
| `CustomersMerged.v1` | Merge plan commits | Survivor/source customer IDs, merge ID, effective time | Survivor customer ID | All customer-reference consumers |
| `CustomerPreferenceChanged.v1` | Preference version effective | Customer ID, purpose/channel/state/version | Customer ID | Channels, Audit |
| `RelationshipManagerAssigned.v1` | Assignment effective | Customer ID, staff identity ref, effective period | Customer ID | Channels/CRM read model, Audit |
| `customer_audit_event` (topic `corebanking.customer-onboarding.audit.v1`, family `audit`) | Any audited action commits | Audit event ID, action, subject type/reference, actor, correlation, code-only detail | Customer or case reference | SIEM collector, Audit and Reconciliation |

### Consumed events

| Event | Producer | Consumer behaviour | Deduplication key | Failure handling |
| --- | --- | --- | --- | --- |
| `InstitutionActivated.v1` | Core Banking and Ledger | Initialise customer tenant partition and policy references | Event ID | Retry; onboarding unavailable until complete |
| `BranchStatusChanged.v1` | Core Banking and Ledger | Update branch reference and prevent invalid new assignment | Event ID | Fail closed for stale branch scope |
| `EntitlementVersionAdvanced.v1` | Identity and Access | Invalidate local access cache | Event ID/version | Deny sensitive access until fresh |
| `VerificationAttemptUpdated.v1` | Integration Hub | Apply exact attempt transition and evidence reference | Attempt event ID | Inbox retry; gaps require status enquiry |
| `CustomerRiskAssessed.v1` | Risk and Reporting | Store assessment reference/recommendation for compliance decision | Assessment ID | Keep classification pending |
| `ScreeningResultUpdated.v1` | Risk and Reporting | Update KYC requirement/reference without self-activating customer | Result event ID | Retry/manual review |
| `ApprovalDecided.v1` | Operations and Workflow | Apply exact pending change/version/hash once | Decision ID | Remain pending on mismatch/failure |
| `AccountOpened.v1` | Core Banking and Ledger | Update authorised Customer 360 account summary/read reference | Event ID | Mark section stale; resynchronise |
| `CaseSummaryChanged.v1` | Operations and Workflow | Update permitted Customer 360 case summary | Event ID | Mark section stale/unavailable |

Events never contain raw BVN/NIN, full addresses, document content, biometrics, unrestricted provider payloads, authentication secrets or unnecessary PII. Consumers obtain sensitive details through authorised APIs when needed.

Outbox and source state commit atomically. Inbox processing is idempotent. Version gaps trigger reconciliation; replay cannot duplicate a customer, reopen a completed verification, reactivate a closed customer or repeat a merge.

## 10. Database design

### Ownership

- Database: `customer_onboarding_db`.
- Schemas: `party`, `customer`, `kyc`, `onboarding`, `privacy`, `platform`, `integration`, and append-only `audit`.
- Migration assembly: Customer and Onboarding Infrastructure project only.
- Principals: Separate migration and runtime principals; no direct access for other services.
- Binary evidence: Approved encrypted object/document storage; database retains protected immutable references, hashes and metadata.

### Party and customer tables

| Table | Purpose | Key constraints | Tenant-owned | Retention |
| --- | --- | --- | --- | --- |
| `party.parties` | Stable party lifecycle/type | Institution/type/status checks; concurrency | Yes | Policy/legal hold |
| `party.individuals` | Protected individual attributes | One per party; encrypted field refs | Yes | Policy/legal hold |
| `party.names` | Versioned legal/preferred/former names | Effective periods; no destructive overwrite | Yes | Historical |
| `party.contacts` | Versioned contact points | Type/value-token/effective indexes | Yes | Policy |
| `party.addresses` | Versioned protected addresses | Type/effective period/verification status | Yes | Policy |
| `party.relationships` | Next-of-kin and allowed individual relationships | Relationship type and period | Yes | Policy |
| `customer.customers` | Institution banking relationship | Unique institution/customer number; status | Yes | Policy/legal hold |
| `customer.customer_identifiers` | Protected identifiers and lookup tokens | Institution/type/token indexes; verified uniqueness policy | Yes | Strict policy/legal hold |
| `customer.customer_status_history` | Append-only status transitions | Unique customer/sequence | Yes | Permanent evidence |
| `customer.customer_aliases` | Legacy/merged redirects | Alias unique per institution | Yes | Permanent |
| `customer.customer_segments` | Segment/classification references | Effective period/version | Yes | Historical |
| `customer.service_preferences` | Purpose/channel preference versions | Customer/purpose/channel/version | Yes | Consent policy |
| `customer.relationship_managers` | Effective assignments | No overlapping active assignment where prohibited | Yes | Historical |

### Onboarding, KYC and privacy tables

| Table | Purpose | Key constraints | Tenant-owned | Retention |
| --- | --- | --- | --- | --- |
| `onboarding.applications` | Application identity/current state | Unique institution/reference; concurrency | Yes | Policy |
| `onboarding.application_revisions` | Immutable captured revisions | Unique application/revision; payload hash | Yes | Policy/legal hold |
| `onboarding.requirement_snapshots` | Exact policy/requirement inputs | Immutable version/hash | Yes | With application/KYC evidence |
| `onboarding.application_steps` | Step state and completion evidence | Unique application/revision/step | Yes | Policy |
| `onboarding.application_decisions` | Append-only decisions/reasons | Decision identity unique | Yes | Permanent evidence |
| `customer.duplicate_candidate_sets` | Detection run/rule version/status | One current set per application revision | Yes | Evidence policy |
| `customer.duplicate_candidates` | Candidate, score band and factors | No raw sensitive match data | Yes | Evidence policy |
| `customer.merge_requests` | Survivor/source and lifecycle | Unique active source; exact plan hash | Yes | Permanent evidence |
| `customer.merge_consumer_status` | Downstream convergence acknowledgements | Merge/consumer unique | Yes | Operational plus audit |
| `kyc.kyc_cases` | KYC lifecycle/current state | One current case per customer/purpose policy | Yes | Regulatory/legal hold |
| `kyc.kyc_requirements` | Required evidence/check state | Case/type/policy reference unique as defined | Yes | With case |
| `kyc.identity_verifications` | Canonical logical verification | Institution/type/subject/idempotency unique | Yes | Regulatory/legal hold |
| `kyc.verification_attempts` | Immutable provider attempts/status | Unique attempt and external ref scope | Yes | Regulatory/legal hold |
| `kyc.verification_status_history` | Append-only transitions | Verification/attempt/sequence unique | Yes | Regulatory/legal hold |
| `kyc.document_references` | Storage ref, purpose, version, hash, scan status | No binary; immutable evidence versions | Yes | Policy/legal hold |
| `kyc.risk_classifications` | Proposed/approved classification versions | Customer/version unique; one effective version | Yes | Permanent history |
| `kyc.review_schedules` | Due review and completion reference | Customer/review date/status indexes | Yes | Regulatory |
| `privacy.consents` | Consent/lawful-processing evidence | Purpose/version/source/effective period | Yes | Privacy/legal hold |
| `privacy.disclosure_events` | Sensitive-data access evidence | Append-only; purpose and permission required | Yes | Audit/privacy policy |
| `privacy.data_subject_requests` | Reference for export/correction/restriction/deletion workflow | No automatic deletion | Yes | Privacy policy |
| `integration.outbox_messages` | Durable events | Event ID unique | Yes | Replay horizon |
| `integration.inbox_messages` | Deduplication | Consumer/event ID unique | Yes | Replay horizon |
| `integration.idempotency_records` | Command replay outcomes | Institution/operation/key unique; request hash | Yes | Approved horizon |
| `audit.customer_audit_events` | Append-only action evidence and SIEM source | Runtime principal holds SELECT/INSERT only; codes and references in detail | Yes | Regulatory/legal hold |

### Constraints and indexes

- Tenant-owned uniqueness begins with `institution_id` unless deliberately platform-wide.
- Use protected deterministic lookup tokens only after cryptographic/privacy review; never index plaintext high-risk identifiers.
- A token collision or duplicate match creates review work and does not expose the matched record.
- Check constraints enforce legal status values, chronological effective periods and required reasons/approvals.
- Partial unique indexes enforce one effective classification, one active current application revision and merge-source rules where applicable.
- Optimistic concurrency protects application, candidate-set, case, customer, merge and classification transitions.
- History, attempt, evidence and disclosure tables are append-only through runtime permissions.
- External service IDs are references, not cross-database foreign keys.
- Query indexes are designed for bounded institution/status/review-date/work-queue access; no unrestricted PII search index.

### Migration and backfill

- Create schemas and classifications before business tables; verify runtime principal cannot update append-only evidence.
- Seed only approved reference types and synthetic development data, never real identifiers or provider evidence.
- Existing customer import requires source reconciliation, tenant mapping, duplicate detection, identifier tokenisation, evidence quality flags and explicit cutover approval.
- A migrated record with insufficient evidence remains restricted/pending according to approved policy; migration does not manufacture verification.
- Released migrations are append-only. Destructive changes require export, validation, legal/privacy approval and forward-fix strategy.

## 11. Duplicate detection, consistency and idempotency

- Exact match uses approved normalised protected tokens; probabilistic matching uses approved feature categories and explainable rule/model versions.
- Duplicate scoring never logs or emits raw feature values. Candidate responses show only authorised masked comparison data and reason categories.
- Concurrent applications for the same identifier are serialised or constrained at the smallest protected token scope that preserves correctness.
- A customer number is generated once under institution rules and remains stable across retries.
- Application submit, verification request, classification proposal, status proposal and merge proposal use institution/operation/request ID plus request hash.
- Reuse of a key with a different hash returns a conflict and creates security/audit evidence.
- Provider attempts have separate transport retry and business retry semantics. A network retry is allowed only if the adapter proves it cannot duplicate the provider operation or uses provider idempotency.
- Status callbacks and polls use the same logical attempt and monotonic transition rules.
- Merge applies locally in one transaction for owned data; downstream convergence is event-driven and reconciled. No distributed transaction is used.
- Customer 360 is read-only and tolerates explicit partial availability. It never writes back to source services as part of a read.

## 12. Security, privacy and data handling

### Data classification

- Restricted: government identifiers, biometrics/liveness evidence, provider raw evidence, document content, detailed screening data.
- Confidential: full profile, addresses, contacts, next of kin, KYC tier, risk class, cases and relationship details.
- Internal: operational state and non-sensitive references.
- Public: none of the individual customer record by default.

### Required controls

- Encrypt restricted/confidential fields at rest with managed keys; use field-level protection or tokenisation where the threat model requires it.
- Keep key material and provider credentials in approved secret/KMS/HSM systems; store references only.
- Mask sensitive fields in ordinary APIs, UI models, logs, traces, metrics, events, reports and test snapshots.
- Sensitive unmasking is field-specific, purpose-bound, time-bounded where appropriate and immutably audited.
- Never send raw identifiers in URLs. Approved frontend envelope encryption supplements mandatory TLS for selected fields.
- Document uploads use pre-authorised bounded flows, content-type/size validation, malware scanning, quarantine and cryptographic hash verification before evidence becomes usable.
- Provider raw payloads are encrypted, access-controlled and retained only when approved. Ordinary code consumes canonical mapped results.
- Non-production uses synthetic data. Production data requires formally approved irreversible masking before any permitted lower-environment use.
- Consent does not replace another lawful basis, and withdrawal does not erase records subject to legal/regulatory retention.
- Data correction, restriction, export and deletion/anonymisation are controlled workflows; deletion occurs only when legally permitted and approved.

### Abuse cases requiring negative tests

- Cross-tenant enumeration by customer, application, verification, document or merge ID.
- Branch-scoped officer accessing another branch without permitted relationship.
- Mass assignment of customer status, KYC result, risk class, approval or institution.
- Search/export used to harvest PII.
- Duplicate API used to discover whether a named person is a customer.
- Malicious document, content-type spoofing, oversized upload or post-scan substitution.
- Provider callback replay, forged result or external-reference collision.
- Unmask permission used without a valid purpose or against a disallowed field.
- Customer merge used to take over identity, account references or communication details.
- Stale Customer 360 data presented as current or authoritative.

## 13. Customer 360 composition

Customer 360 is a backend composition/read-model capability with section-level authorisation:

| Section | Authoritative source | Minimum permission | Failure representation |
| --- | --- | --- | --- |
| Profile and contacts | Customer and Onboarding | `customer.profile.view` | Local error; no profile response |
| KYC/classification | Customer and Onboarding | `customer.kyc.view` | Section unavailable/restricted |
| Accounts and balance summaries | Core Banking and Ledger | `account.summary.view` | Unavailable/stale; never zero-filled |
| Documents | Document owner/storage metadata | `document.customer.view` | Unavailable/restricted |
| Cases/complaints | Operations and Workflow | `case.customer.view` | Unavailable/restricted |
| Alerts | Risk and Reporting | `risk.alert.customer.view` | Unavailable/restricted |
| Recent interactions | Channels and Engagement | `channel.interaction.customer.view` | Unavailable/restricted |

Rules:

- Resolve the Customer first under trusted institution context, then authorise every requested section.
- Return masked projections by default. Section access does not imply sensitive-field disclosure.
- Include `source`, `observedAt`, `freshnessStatus` and `availabilityStatus` per remote section.
- Apply strict deadlines and bulkheads so one dependency cannot exhaust Customer service resources.
- Cache only approved, minimal, non-authoritative projections with bounded TTL and entitlement-aware keys.
- Log sensitive section access without copying returned PII into logs.

## 14. Audit and observability

Immutable evidence is required for application submission, duplicate decisions, merge proposal/approval/application, verification request/result/override/waiver, classification proposal/approval, customer activation/status change, protected correction, sensitive disclosure, document access, relationship-manager assignment and consent/preference change.

Evidence includes actor/service ID, role/service context, permission, institution, branch, customer/application/case reference, purpose/reason, source channel, policy and data versions, maker/approver identities, before/after protected metadata, timestamp and correlation ID. Do not copy raw identifiers, full documents or provider payloads into audit summaries.

Minimum metrics:

- Applications by state, age, branch/channel and safe rejection category.
- Duplicate-candidate rate, review age, merge state and unresolved consumer acknowledgements.
- KYC requirements and verifications by type, canonical status, provider route category and age.
- Uncertain attempts, status-enquiry backlog, mapping failure and manual-review queue.
- Classification/approval age, EDD outstanding and review overdue count.
- Customer activations, restrictions, dormancy, deceased and closure transitions.
- Sensitive disclosure counts/denials by field category and purpose.
- Customer 360 latency, dependency timeout, partial response and stale-section rates.
- Outbox/inbox backlog, dead letters, audit failure and reconciliation discrepancies.

Traces propagate correlation through channel, Customer service, Workflow, Integration Hub, provider adapter, Risk and downstream events. Logs use stable event names and exclude request bodies or sensitive response objects.

Alerts and runbooks cover verification backlog, provider outage, uncertain-attempt threshold, overdue KYC review, merge convergence failure, unusual disclosure/search volume, document scan failure, event-version gap and restore inconsistency.

## 15. Failure, retry and recovery

| Failure | Safe state | Recovery |
| --- | --- | --- |
| Country-pack/policy unavailable | Application cannot submit | Restore approved version; no permissive default |
| Duplicate service/rule failure | Application remains pending review | Retry exact revision; manual path by approved policy |
| Provider failure before submission | Attempt records safe technical failure | Bounded retry or approved route selection |
| Provider timeout after submission | Attempt becomes Uncertain | Status enquiry before retry/failover |
| Conflicting provider outcomes | Verification becomes ManualReview | Reconcile evidence; authorised decision |
| Workflow unavailable | Change remains PendingApproval | Retry request/consume decision idempotently |
| Risk/screening unavailable | Classification/activation remains pending | Retry/resume; no fabricated low risk |
| Document scan unavailable | Evidence remains quarantined/unusable | Resume scan; do not bypass |
| Customer 360 dependency unavailable | Explicit partial/unavailable section | Bounded retry on later request; no fabricated data |
| Merge consumer failure | Merge stays Applying/ReconciliationRequired | Retry event/resync and track acknowledgement |
| Event version gap | Local consumer projection marked stale | Authenticated resynchronisation |
| Restore | Service closed to writes until reconciled | Verify identifiers, statuses, outbox/inbox and downstream versions |

Retry only operations proven safe. Manual repair uses authorised commands with reason and audit; operators never edit database rows directly as a normal recovery path.

Backup/restore exercises verify encrypted data readability through managed keys, document references/hashes, application and KYC history, current classification/status, identifier-token consistency, merge aliases, consent evidence, audit/outbox continuity and replay safety.

## 16. Configuration

| Setting | Owner | Secret | Validation | Reload behaviour |
| --- | --- | --- | --- | --- |
| Country-pack onboarding requirements | Institution/Product/Compliance | No | Approved schema/version/effective dates | New application snapshot only |
| KYC tier and evidence rules | Compliance | No | Complete requirement graph; approval | Versioned/effective-dated |
| Duplicate rules/model version | Data/Compliance | No | Bias/privacy/quality review and thresholds | New detection run only |
| Provider route/check mapping | Integration/Compliance | Credential refs only | Supported check, environment and mapping | Controlled version switch |
| Canonical provider-result mapping | Compliance/Integration | No | Exhaustive known statuses and unknown handling | Versioned; no retroactive rewrite |
| Classification/EDD rules | Compliance/Risk | No | Explainable inputs, approval and review schedule | Versioned/effective-dated |
| Customer status transition policy | Operations/Compliance | No | Legal transitions and approval levels | Versioned/effective-dated |
| Masking/disclosure policy | Privacy/Security | No | Field classification, purpose and permission | Controlled; cache invalidation |
| Document limits and allowed types | Security/Compliance | No | Size/type/scan/retention constraints | New upload only |
| Retention/legal hold policy | Compliance/Privacy | No | Record class/country/institution basis | Controlled lifecycle jobs |
| Customer 360 section timeout/cache | Platform/Domain owner | No | Security/freshness bounds | Controlled dynamic reload |
| Reconciliation interval (`CustomerReconciliation:IntervalSeconds`, default 3600) | Operations/Platform | No | Positive seconds; findings are never auto-repaired | Worker restart |
| Encryption/tokenisation key refs | Security | Reference only | KMS purpose/version/access | Rotation with transition |

All policy changes are versioned, approved, effective-dated and audited. Missing configuration denies the affected operation rather than guessing a default.

## 17. Implementation slices

Review pack for human approval: [reviews/03-customer-and-onboarding-review-pack.md](reviews/03-customer-and-onboarding-review-pack.md).

**Approval:** slices 1-15 were approved by the project owner on **2026-10-31** with the named gaps in the review
pack kept as open items. The epic status is `Implemented`: the code and its evidence were accepted, while the
gaps in the review pack and the named external confirmations below still gate production use.

1. Approve customer data classification, country-pack ownership, provider evidence, duplicate and retention decisions; complete threat/privacy model.
2. Define versioned Party, Customer, Application, KYC and event contracts plus architecture tests.
3. Implement tenant/branch reference consumers and institution provisioning for `customer_onboarding_db`.
4. Implement individual Party/Application capture with explicit field mapping, encryption/tokenisation ports and revision tests.
   *Status: implemented — see [customer-onboarding-capture.md](../../operations/customer-onboarding-capture.md).
   The API surface stays unregistered until request contracts and authorisation wiring land.*
5. Implement requirement snapshots, document metadata/quarantine integration and submission validation.
   *Status: in progress — institution-owned versioned requirement packs with a content hash, an immutable
   snapshot per submitted revision, document reference metadata behind a fail-closed quarantine port, and
   submission refused when no approved pack is effective or a registered document failed its scan.
   Decision: [P1-E03-D01/D02/D03](decisions/P1-E03-D01-D02-D03-requirement-packs-numbering-and-classification.md).
   Pack content itself is Product/Compliance data and is not authored in code; the review HTTP surface
   awaits the service authorisation port.*
6. Implement exact duplicate tokens, explainable candidate sets and review workflow; add concurrency and enumeration tests.
   *Status: in progress — exact-match factors, derived bands, bounded candidate sets, reviewer decisions with
   stale-evidence and stale-version refusals, deterministic birth-date and address lookup tokens, and
   `Category=Database` concurrency/enumeration tests are implemented. Decision: [P1-E03-D04/D05](decisions/P1-E03-D04-D05-duplicate-detection-and-identifier-uniqueness.md).
   The review HTTP surface awaits the service authorisation port; verified-identifier matching arrives with
   the identifier slices.*
7. Implement KYC case/requirement model and canonical verification request/result contracts.
   *Status: in progress — KYC case opened from the submitted revision's requirement snapshot, requirement
   lifecycle, canonical verification request/attempt/result contracts, versioned-mapping enforcement,
   reviewer resolution, waivers and append-only requirement and verification history.
   Decision: [P1-E03-D06](decisions/P1-E03-D06-kyc-checks-providers-and-canonical-outcomes.md).
   Check codes, provider routing and mapping tables remain Compliance/Integration data; no provider is
   bound and the internal HTTP contracts await the service authorisation port.*
8. Integrate one approved synthetic KYC adapter through Integration Hub, including uncertain status and evidence references.
   *Status: in progress - `IKycProviderPort` is the canonical boundary and `SyntheticKycProviderAdapter` is the
   only adapter bound in Phase 1. `KycVerificationRunner` retries only when a submission was refused before
   acceptance, turns an accepted-but-late submission into `Uncertain`, resolves it only by bounded status
   enquiry, keeps an unknown submission uncertain instead of resubmitting, and records the provider route,
   mapping version, evidence reference and evidence hash on an accepted outcome.
   Decision: [P1-E03-D07/D08](decisions/P1-E03-D07-D08-provider-retry-evidence-and-status-enquiry.md).
   Real provider routes, check codes and mapping tables remain Compliance/Integration data; the adapter is
   deterministic and uses no network.*
9. Implement classification versions, Risk references, Workflow approval application and customer activation.
   *Status: in progress — versioned tier/segment/risk/EDD classification with Workflow decision references,
   supersession of the earlier approved version, activation gated by a completed KYC case plus an approved
   classification, and customer creation with institution-sequence numbering.
   Decision: [P1-E03-D09/D10](decisions/P1-E03-D09-D10-classification-activation-and-status-transitions.md).
   A proposed version is submitted to Workflow with a frozen SHA-256 content hash
   (`customer.classification.approve.v1`), and `CustomerClassificationApprovalDecidedConsumer` applies the
   decision only to that request, version and hash, idempotently through the inbox, recording the approver and
   the reason as audit evidence. Risk assessment references remain open: they are Risk data and are not
   fabricated here.*
10. Implement customer status transitions, corrections and downstream events without account mutation.
   *Status: in progress — the epic's transition table with reasons, approval references for restricted,
   deceased and closed, terminal states that are never reversed, append-only status history and
   `CustomerCreated.v1` / `CustomerStatusChanged.v1` outbox events. Protected-data corrections append a
   `customer.customer_corrections` row with the superseded and new protected-value references, invalidate the
   verification that satisfied the affected requirement so it is never treated as current, publish
   `CustomerProfileChanged.v1` with field codes only, and require an approval reference for a verified
   attribute. No account state is touched anywhere.*
11. Implement approved merge planning/application, aliases, consumer acknowledgements and reconciliation.
    *Status: in progress — frozen merge plans with a plan hash, Workflow approval reference, one active merge
    per source, source closure through the normal transition with retained history, permanent aliases,
    `CustomersMerged.v1` with the plan hash, required-consumer acknowledgements and a
    `reconciliation_required` state until every consumer answers.
    Decision: [P1-E03-D11](decisions/P1-E03-D11-customer-merge-eligibility-and-reconciliation.md).
    The plan is submitted to Workflow with the merge request identifier as the approval request identifier and
    the frozen plan hash as the subject hash (`customer.merge.approve.v1`); the approval consumer applies a
    decision only to that request and hash, an approval makes the plan applicable, a rejection ends it with the
    reason recorded as audit evidence, and a repeat changes nothing. The consumer acknowledgement surface
    (`AcknowledgeCustomerMergeCommand`) and the reconciliation job (`P1-E03-D14`) close the convergence loop.*
12. Implement preferences, consent evidence and relationship-manager assignments.
    *Status: in progress — versioned per-purpose/per-channel preferences with an effective period, an
    idempotent repeat, a required lawful basis for grants, a recorded withdrawal reason, append-only consent
    evidence in the new `privacy` schema, and a database guarantee of one current version per purpose and
    channel. Decision: [P1-E03-D12](decisions/P1-E03-D12-consent-purposes-lawful-bases-and-preferences.md).
    Effective-dated relationship-manager assignments are implemented in
    `customer.relationship_manager_assignments` (one current per customer, reassignment closing the prior
    period, `RelationshipManagerAssigned.v1`, audit evidence, 8 unit and 6 database tests); the
    purpose/channel/lawful-basis catalogue and data-subject request handling remain open.*
13. Implement section-authorised, masked Customer 360 with Core/Workflow/Risk/Document/Channel stubs or approved services.
    *Status: in progress — read-only composition with per-section status, source, permission and freshness;
    three owned sections (`profile`, `kyc`, `merges`) answered from local tables with masked values only, and
    five explicit `unavailable` placeholders with a stable reason. Unknown sections are refused and an empty
    request returns nothing.
    Decision: [P1-E03-D13](decisions/P1-E03-D13-customer-360-sections-and-freshness.md).
    Core, Workflow, Risk, Document and Channel read contracts, and API-layer per-section permission
    enforcement, remain open.*
14. Add immutable audit, SIEM events, dashboards, alerts, reconciliation jobs and runbooks.
    *Status: in progress — every audited customer, KYC and requirement action appends an immutable row to
    `audit.customer_audit_events` plus a `customer_audit_event` SIEM event in the same transaction, the
    application principal holds `SELECT`/`INSERT` on `audit` only, and a scheduled reconciliation pass reports
    five integrity codes without repairing state and publishes metrics.
    Decision: [P1-E03-D14](decisions/P1-E03-D14-retention-audit-and-reconciliation-posture.md).
    Runbook: [customer-lifecycle.md](../../operations/customer-lifecycle.md#audit-evidence-and-reconciliation).
    Retention periods, legal-hold and SIEM shipping configuration, dashboards and alert routing remain
    Legal/Operations data.*
15. Complete provider, security, privacy, recovery, performance and end-to-end acceptance evidence.
    *Status: in progress - `Category=Integration` acceptance tests run inside the standard verification pass
    and evidence capture, requirement gating, synthetic verification with evidence, classification approval,
    activation, a masked Customer 360 composition, a replay that creates no duplicate customer, a cross-tenant
    refusal and a data-minimisation check over every produced event and audit row.
    Decision: [P1-E03-D15](decisions/P1-E03-D15-acceptance-evidence-and-measurement-posture.md).
    Capacity, SLO, RTO/RPO targets, a load profile, a real provider route and screenshot-based dashboards remain
    Product/Platform/Operations data and are recorded as gaps, not as approvals.*

Each slice begins with traceable failing tests and leaves the solution buildable. Incomplete security or KYC paths remain unavailable, not permissive.

## 18. Test plan

| Test ID | Requirement/story | Level | Scenario | Expected evidence |
| --- | --- | --- | --- | --- |
| CUS-001 | CUS-001, CUS-002 / US-011 | Integration | Create individual application with required details | Tenant-scoped draft and immutable revision |
| CUS-002 | CUS-004 / US-011 | Domain | Requirements vary by country pack and tier | Exact approved snapshot determines fields/evidence |
| CUS-003 | CUS-004 | Resilience | Requirement policy unavailable at submit | Submission denied; no permissive default |
| CUS-004 | CUS-003 / US-011 | Integration | Exact duplicate identifier detected | Masked candidate and review state; no activation |
| CUS-005 | CUS-003 | Domain | Probabilistic match below/above thresholds | Explainable versioned candidate behaviour |
| CUS-006 | CUS-003 | Concurrency | Two matching applications submit concurrently | No duplicate active customers; controlled review |
| CUS-007 | CUS-003 | Security | Use duplicate endpoint for enumeration | Generic bounded result; no unrelated disclosure |
| CUS-008 | CUS-005 / US-013 | Contract | Request BVN/NIN/address/document checks | Canonical minimal contract; no raw ID in event/log |
| CUS-009 | CUS-005, API-005 | Resilience | Provider fails before submission | Safe bounded retry/route policy |
| CUS-010 | CUS-005, CUS-006 / US-013 | Resilience | Provider times out after submission | Uncertain plus status enquiry; no blind retry |
| CUS-011 | CUS-006 / US-013 | Integration | Provider-specific success maps canonically | Mapping/policy version and evidence ref retained |
| CUS-012 | CUS-006 | Security | Forged/replayed provider result | Rejected/deduplicated and security evidence |
| CUS-013 | CUS-006 | Recovery | Conflicting provider outcomes | ManualReview; no silent winning result |
| CUS-014 | CUS-007 / US-014 | Domain | Propose tier/risk/review date | Immutable classification version and reason |
| CUS-015 | CUS-007 / US-014 | Security | Maker self-approves classification via second role | Denied by stable actor ID |
| CUS-016 | CUS-007 / US-014 | Integration | High-risk classification | EDD requirements created; downstream event after approval |
| CUS-017 | CUS-008 | Domain | Activate without mandatory check/approval | Denied; customer remains Pending |
| CUS-018 | CUS-008 | Contract | Restrict/close/deceased customer | Event emitted; no direct account/balance update |
| CUS-019 | DAT-008 | Integration | Correct verified sensitive field | Prior value/evidence preserved; re-verification triggered by policy |
| CUS-020 | CUS-003 | Integration | Merge approved duplicates | Survivor/alias/history retained; consumers notified once |
| CUS-021 | CUS-003 | Security | Cross-tenant merge attempted | Denied without target disclosure |
| CUS-022 | CUS-003 | Recovery | Consumer fails during merge | ReconciliationRequired until acknowledged |
| CUS-023 | CUS-009 / US-015 | Security | View 360 with branch-limited permission | Only authorised sections/fields; masked by default |
| CUS-024 | CUS-009 / US-015 | Resilience | Account/case dependency unavailable | Explicit partial response; no zero/empty fabrication |
| CUS-025 | CUS-009, AUD-001 | Integration | Sensitive profile/section view | Immutable disclosure evidence with purpose |
| CUS-026 | CUS-010 | Integration | Assign relationship manager effective-dated | Scope validated; prior assignment retained |
| CUS-027 | SEC-014 / CUS-010 | Domain | Change marketing preference | Purpose/version/consent evidence; required messages protected |
| CUS-028 | DAT-006, SEC-013 | Security | Inspect DB/API/log/event/test outputs | Encryption/tokenisation/masking; no prohibited PII |
| CUS-029 | SEC-011 | Security | Mass-assign status, tier, institution or approval | Rejected/ignored; no elevation/state change |
| CUS-030 | RPT-002 | Security | Malicious or unscanned document | Quarantined; cannot satisfy requirement |
| CUS-031 | SEC-018 | Compliance | Seed/test environment inspection | Synthetic data only; no production identifiers |
| CUS-032 | NFR-OBS-001 | End-to-end | Trace application through provider and approval | Correlated, redacted evidence across services |
| CUS-033 | NFR-DR-001 | Recovery | Restore and replay customer database | No duplicate customers, state regression or repeated merge |
| CUS-034 | CUS-001..CUS-010 | End-to-end | Onboard, verify, classify and activate individual | Complete trace and authorised 360 response |

Load and performance tests use an approved data-volume and provider-latency profile with synthetic protected data.

## 19. Acceptance criteria

- [x] CUS-001 through CUS-010 and US-011, US-013 through US-015 trace to contracts, code and tests; Phase 2 corporate scope remains explicit (see the slice ledger and [acceptance evidence](../../operations/phase-1-customer-onboarding-acceptance-evidence.md)).
- [x] Individual application requirements derive from immutable approved policy snapshots (`RequirementSnapshotDatabaseTests`).
- [x] Duplicate detection is explainable and cannot auto-merge or expose customer existence (`DuplicateDetectionDatabaseTests`).
- [x] Verification providers are accessed only through canonical Integration Hub contracts (`IKycProviderPort`, `KycProviderRunDatabaseTests`; one synthetic adapter bound).
- [x] Provider uncertainty, replay, conflict and raw evidence are handled safely (`KycProviderRunDatabaseTests`, `VerificationEvidenceTests`).
- [x] Classification and activation require exact approved versions and maker-checker separation where configured (`CustomerDatabaseTests`; the Workflow `ApprovalDecided.v1` consumer remains open).
- [x] Customer lifecycle transitions preserve history and do not directly mutate accounts or balances (`CustomerDatabaseTests`, `CustomerAuditDatabaseTests`).
- [ ] Protected corrections preserve prior values and trigger required re-verification.
- [x] Merge retains aliases/history and tracks every required consumer to convergence (`CustomerMergeDatabaseTests`).
- [x] Customer 360 enforces section/field scope, masking, source/freshness metadata and sensitive-view audit (`Customer360DatabaseTests`, end-to-end flow; API-layer per-section permission enforcement remains).
- [x] Sensitive data is encrypted/tokenised/masked and excluded from logs, events, URLs and non-production fixtures (`Another_institution_reaches_nothing_and_no_protected_value_reaches_an_event`, protection port tests).
- [ ] APIs/events/migrations, dashboards, alerts, reconciliation and recovery runbooks are reviewed and tested.

## 20. Definition of Done

- Repository build, analyzers, formatting, architecture tests and affected test suites pass.
- EF Core migrations apply to empty and supported upgrade databases; constraints and runtime privileges are verified.
- OpenAPI and event schemas pass compatibility and data-minimisation review.
- Synthetic provider and approved non-production provider flows cover success, failure, timeout, replay and status enquiry.
- Security/privacy review confirms field classification, masking, tokenisation, document handling, disclosure and retention.
- One staff-led individual onboarding flow completes end to end, and one negative cross-tenant flow is demonstrated.
- Customer 360 proves section authorisation and explicit partial failure without becoming a source of financial truth.
- Backup restore, event replay, merge reconciliation and provider recovery are demonstrated.
- Traceability records requirements, stories, decisions, contracts, migrations, code, tests and operational evidence.

## 21. Open decisions

| Decision | Impact | Owner | Required by | Status |
| --- | --- | --- | --- | --- |
| Launch country pack and authoritative policy owner | Required fields/evidence and validation | Product/Compliance | Slice 1 | Approved 2026-10-29: pack is institution data with a content hash; no requirement authored in code; submission denied when no pack is effective; [brief](decisions/P1-E03-D01-D02-D03-requirement-packs-numbering-and-classification.md) |
| Customer/party numbering rules | Stable identifiers and migration | Product/Operations | Slice 2 | Approved 2026-10-29: opaque party identifiers, institution-scoped sequence for customer numbers, format owned by Operations |
| Field classification and encryption/tokenisation design | Storage, search and breach impact | Security/Privacy | Slice 1 | Approved 2026-10-29: epic classification adopted, field-level protection through the protector port, lookup tokens only for exact matching |
| Duplicate rules/model, thresholds and reviewer evidence | False matches, bias and UX | Compliance/Data | Slice 6 | Approved 2026-10-29 for exact-token detection only; [brief](decisions/P1-E03-D04-D05-duplicate-detection-and-identifier-uniqueness.md) |
| Verified-identifier uniqueness policy | Duplicate handling and shared/legal cases | Compliance | Slice 6 | Approved 2026-10-29 for verified-only uniqueness and manual shared-identifier review; named Compliance confirmation outstanding |
| Approved Phase 1 KYC checks and providers | Integration and acceptance | Compliance/Product | Slice 7 | Approved 2026-10-29 for a data-driven check model with no bound provider, canonical outcomes only through a versioned mapping, and uncertain never treated as pass or fail; [brief](decisions/P1-E03-D06-kyc-checks-providers-and-canonical-outcomes.md) |
| Provider retry/failover/status-enquiry semantics | Duplicate checks and uncertain results | Integration/Provider owner | Slice 8 | Approved 2026-10-30 for bounded pre-submission retries, status enquiry before any failover, manual review on conflict and an unknown submission left uncertain; [brief](decisions/P1-E03-D07-D08-provider-retry-evidence-and-status-enquiry.md) |
| Raw evidence format, storage, access and retention | Regulatory evidence/privacy | Compliance/Security | Slice 8 | Approved 2026-10-30 for evidence references with a SHA-256 hash only, access through Integration Hub and retention following `P1-E03-D14`; [brief](decisions/P1-E03-D07-D08-provider-retry-evidence-and-status-enquiry.md) |
| KYC tier, risk class, EDD and review policy | Activation and downstream limits | Compliance/Risk | Slice 9 | Approved 2026-10-29 for data-driven classification with Workflow approval, activation gated by KYC completion plus approved classification, no invented thresholds; [brief](decisions/P1-E03-D09-D10-classification-activation-and-status-transitions.md) |
| Customer status transitions and account consequences | Cross-service operational behaviour | Product/Compliance/Core | Slice 10 | Approved 2026-10-29 for the epic lifecycle, reasons and approval references, terminal states never reversed in code, and no account mutation from this service |
| Merge eligibility, freezes and required consumers | Referential integrity and customer impact | Architecture/Operations | Slice 11 | Approved 2026-10-29 for mergeable states `active`/`restricted`/`dormant`, frozen plans after approval, permanent aliases and required-consumer acknowledgement before completion; [brief](decisions/P1-E03-D11-customer-merge-eligibility-and-reconciliation.md) |
| Consent purposes and lawful bases | Preferences and privacy evidence | Legal/Privacy | Slice 12 | Approved 2026-10-29 for data-driven purposes and lawful bases, versioned preferences, append-only consent evidence and recorded withdrawals; [brief](decisions/P1-E03-D12-consent-purposes-lawful-bases-and-preferences.md) |
| Customer 360 launch sections and freshness limits | Permissions, latency and partial UX | Product/Domain owners | Slice 13 | Approved 2026-10-29 for three owned sections plus explicit unavailable placeholders, per-section permission and freshness metadata, masked values only and read-only composition; [brief](decisions/P1-E03-D13-customer-360-sections-and-freshness.md) |
| Data retention, legal hold and deletion/anonymisation | Compliance and storage | Legal/Compliance/Privacy | Before migration approval | Approved 2026-10-29 for the posture: audit evidence is append-only in the database, the service deletes nothing, SIEM shipping is Integration configuration and pruning is an approved operator procedure; retention periods, legal-hold rules and anonymisation windows remain open; [brief](decisions/P1-E03-D14-retention-audit-and-reconciliation-posture.md) |
| Capacity, SLO, RTO and RPO targets | Tests and operations | Product/Platform | Before release test | Approved 2026-10-30 for the evidence posture only: acceptance evidence is executed by the verification pass and no capacity, SLO, RTO or RPO number is invented; targets remain Product/Platform data; [brief](decisions/P1-E03-D15-acceptance-evidence-and-measurement-posture.md) |

## 22. Prohibited assumptions

- Do not invent KYC, BVN, NIN, tax, address, liveness, sanctions or document rules from general knowledge.
- Do not assume a provider response is legally sufficient or automatically activates a customer.
- Do not treat provider timeout as failure or submit blindly to another provider.
- Do not store raw sensitive identifiers merely to make search or duplicate detection easier.
- Do not log request bodies, documents, provider payloads or unmasked Customer 360 responses.
- Do not merge, reject or block customers solely from an unexplained score.
- Do not delete the source customer, history or references during merge.
- Do not implement corporate onboarding, mandates or beneficial ownership as incidental Phase 1 scope.
- Do not let Customer service write accounts, balances, payment outcomes, risk models, identity grants or workflow decisions.
- Do not show stale or unavailable balances as zero in Customer 360.
- Do not infer consent, lawful basis, retention or deletion eligibility.
- Do not use production customer data in tests or non-production environments.

While a policy decision is open, keep the capability unavailable or pending, preserve evidence and use synthetic adapters/data.

## 23. Agent implementation instructions

- Required reading: root `AGENTS.md`, architecture, Phase 0 evidence, Phase 1 institution and identity epics, this epic, CUS/DAT/SEC/AUD requirements, US-011 and US-013..US-015, and approved ADRs/policies.
- Expected C# boundaries: `CoreBanking.CustomerOnboarding.Domain`, `.Application`, `.Infrastructure`, `.Api`, `.Contracts` and `.Tests`.
- Keep provider SDKs, object storage, encryption/tokenisation and remote service clients in Infrastructure behind application ports.
- Do not share EF entities, access another service database or create cross-database foreign keys.
- Use strongly typed IDs, `TimeProvider`, explicit field mapping, nullable reference types, cancellation tokens and optimistic concurrency.
- Use EF Core migrations owned by Customer and Onboarding; use PostgreSQL/Testcontainers for constraints, encryption metadata, concurrency and replay tests.
- Start each slice with traceable failing tests, including cross-tenant, masking, wrong-scope, replay and audit negatives.
- Publish events only through the transactional outbox; consume idempotently through inbox records and detect version gaps.
- Stop when implementation would invent a country rule, provider semantic, PII protection design, duplicate threshold, KYC outcome, risk tier, merge policy, consent basis, retention period or account consequence.
- Completion report must list requirement/story coverage, decisions, permissions, APIs/events, migrations, data classifications, tests/commands, provider/recovery evidence, assumptions, risks and rollout notes.

