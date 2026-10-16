---
phase: "Phase 2 - Regulated Operations"
epic: "Corporate Party Onboarding"
owning_service: "Customer and Onboarding"
modules: [Corporate Parties, Ownership, Signatories, Corporate KYC]
status: Draft
requirements: [CUS-001, CUS-002, CUS-003, CUS-004, CUS-005, CUS-006, CUS-007, SEC-009, DAT-006]
stories: [US-012]
dependencies: [Phase 1 Customer and Onboarding, Epic 06 Risk Fraud AML and Sanctions, Phase 1 Documents]
adrs: [Corporate party and relationship model, Corporate KYC and screening, Mandate boundary]
last_updated: 2026-10-16
---

# Epic 02: Corporate Party Onboarding

## 1. Objective

Create verified corporate customers with versioned organisation details, directors, shareholders, beneficial owners, signatories, documents and approval rules while preserving ownership evidence and country-pack KYC controls.

## 2. Success measures

- Corporate structures and ownership percentages are validated and historically reproducible.
- Required parties, documents, verifications and screening complete before activation.
- Signatory and mandate references are configurable and approved without enabling Phase 3 corporate payments; all documents and verification results retain version history.
- Duplicate organisations and related parties are detected and routed to controlled review.

## 3. Scope

Included: organisation profile, registration/tax references, ownership graph, directors, shareholders, beneficial owners, signatories, mandate metadata, KYC requirements, documents, screening references, verification and approval. Excluded: corporate channel administration, payment initiation, file upload/payroll and corporate APIs from Phase 3.

## 4. Actors, roles and permissions

| Actor | Permission | Scope | Step-up | Approval |
| --- | --- | --- | --- | --- |
| Onboarding officer | `customer.corporate.create` | Institution/branch | Sensitive submission | Maker |
| KYC officer | `customer.corporate.verify` | Assigned case | Required | Separate from maker |
| Corporate approver | `customer.corporate.approve` | Institution/risk/branch | Required | Maker-checker |
| Sensitive viewer | `customer.corporate.sensitive.view` | Need-to-know party | Required | None |

Backend services enforce permission and institution/branch/customer scope. A signatory is a party relationship, not automatically an authenticated corporate user.

## 5. Domain model

| Concept | Responsibility | Key rules |
| --- | --- | --- |
| `CorporateOnboardingApplication` | Orchestrates required steps | Versioned requirement set |
| `OrganisationParty` | Legal organisation identity | Unique approved identifiers per institution/country |
| `OwnershipInterest` | Owner, percentage, effective dates | No invalid ranges; totals follow approved policy |
| `RelatedPartyRole` | Director/shareholder/beneficial owner/signatory | Effective-dated and evidenced |
| `CorporateMandateProfile` | Signatory groups/approval metadata | Versioned; no payment authority by itself |

State: `Draft -> Submitted -> VerificationPending -> ReviewPending -> Approved -> Active`, with `Rejected`, `MoreInformationRequired` and `Withdrawn` branches.

Invariants: activation requires the approved KYC set; ownership history is never overwritten; document/verification evidence is referenced and versioned; circular/complex ownership is represented explicitly; screening potential matches cannot auto-approve.

## 6. Use cases

- Create and amend a draft application with explicit accepted fields.
- Add/replace related parties and ownership interests with effective dates.
- submit required documents and canonical provider verifications.
- Resolve duplicate candidates and screening cases through approved workflows.
- Approve/activate the corporate customer and publish safe party facts.

## 7. API contracts

| Method | Route | Permission | Idempotency |
| --- | --- | --- | --- |
| POST | `/api/v1/corporate-onboarding/applications` | `customer.corporate.create` | Request key |
| PUT | `/api/v1/corporate-onboarding/applications/{id}/organisation` | `customer.corporate.update` | Version token |
| POST | `/api/v1/corporate-onboarding/applications/{id}/related-parties` | `customer.corporate.update` | Client item ID |
| POST | `/api/v1/corporate-onboarding/applications/{id}/submit` | `customer.corporate.submit` | Application/version |
| POST | `/api/v1/corporate-onboarding/applications/{id}/decisions` | `customer.corporate.approve` | Decision key |
| GET | `/api/v1/corporate-customers/{id}` | `customer.corporate.view` | Query |

Use stable errors for missing requirement, invalid ownership, duplicate candidate, screening hold, stale version and scope denial. Sensitive fields are masked by default.

## 8. Event contracts

Produced: `CorporateOnboardingSubmitted`, `CorporateRelatedPartyChanged`, `CorporateVerificationRequested`, `CorporateCustomerApproved`, `CorporateCustomerActivated`, `CorporateOnboardingRejected`. Consumed: canonical verification/screening results and document evidence events. Partition by corporate application/customer; outbox/inbox and provider-result deduplication are mandatory.

## 9. Database design

Database `customer_onboarding_db`; schemas `party`, `onboarding`, `kyc`, `integration`.

Tables extend the architecture baseline: `party.organisations`, `party.relationships`, `party.directors`, `party.shareholders`, `party.beneficial_owners`, `party.signatories`, plus `onboarding.corporate_applications`, `corporate_application_versions`, `ownership_interests`, `mandate_profiles`, `kyc.corporate_requirements`, `corporate_verification_references`. Tenant IDs, effective dates, constrained percentages, concurrency versions and append-only relationship history are required. No cross-service foreign keys.

## 10. Consistency, concurrency and idempotency

Application changes use optimistic concurrency. Submission freezes the version being assessed. Activation and outbox commit locally; screening/document/workflow convergence is explicit. Repeated provider results use provider/event identity. No distributed transaction: pending external evidence remains a visible state.

## 11. Security and privacy

Classify ownership, tax, identity, screening and document data. Encrypt/tokenise sensitive identifiers, mask reads, audit sensitive access and rate-limit searches. Test cross-tenant/branch access, relationship injection, ownership manipulation, mass assignment, document-reference substitution and enumeration.

## 12. Audit and observability

Audit every relationship/version change, document/verification link, duplicate resolution, decision, sensitive view and export. Metrics cover application age, missing requirements, provider failures, duplicate/screening queues and activation time. Logs/traces contain safe IDs only.

## 13. Failure, retry and recovery

Provider timeout leaves verification pending/uncertain; never silently approve. Duplicate or screening uncertainty routes to review. Event replay cannot create duplicate parties/relationships. Restore verifies ownership history, application state and evidence links.

## 14. Configuration

Version and approve country/customer-type requirements, ownership validation, beneficial-owner rules, document types, verification providers, risk routing and mandate vocabulary. Legal thresholds are stakeholder inputs.

## 15. Implementation slices

1. Contracts, permissions and party/relationship model.
2. Corporate application persistence/migrations.
3. Ownership, directors, beneficial owners and signatories.
4. Documents, verification, screening and duplicate review.
5. Approval/activation events, audit and observability.
6. End-to-end corporate onboarding evidence.

## 16. Test plan

| ID | Scenario | Expected |
| --- | --- | --- |
| COR-01 | Valid corporate onboarding | Activated with versioned evidence |
| COR-02 | Missing beneficial owner/document | Submission/approval blocked |
| COR-03 | Invalid ownership percentage | Stable validation error |
| COR-04 | Potential duplicate | Review required; no duplicate activation |
| COR-05 | Screening potential match | Case/hold; no silent approval |
| COR-06 | Maker approves own application | Denied and audited |
| COR-07 | Cross-tenant relationship injection | Denied |
| COR-08 | Provider result replay | One verification outcome |

## 17. Acceptance criteria

- [ ] Company data, directors, shareholders, beneficial owners and signatories are retained with history.
- [ ] Ownership percentages and mandate metadata are configurable and validated.
- [ ] Documents, verification and screening evidence satisfy the approved country pack.
- [ ] Activation requires independent approval and complete evidence.

## 18. Definition of Done

Repository DoD plus approved corporate model, migrations, privacy review, contracts, screening integration, negative tests, dashboards and runbook.

## 19. Open decisions

Country legal forms/identifiers, ownership-total and beneficial-owner thresholds, circular ownership handling, mandate meaning, KYC evidence, screening policy, duplicate merge and retention require approval.

## 20. Prohibited assumptions

Do not invent legal/KYC thresholds, equate signatory with platform user, auto-approve provider success, flatten ownership history, or implement Phase 3 corporate payments.

## 21. Agent implementation instructions

Read Phase 1 customer/doc/workflow epics and Epic 06 contracts. Keep party ownership in Customer and Onboarding and external adapters in Integration Hub. Run build, migration, contract, privacy, security and end-to-end tests. Stop on unresolved country-pack, ownership or mandate policy.

