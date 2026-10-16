---
phase: "Phase 1 - Banking Foundation"
epic: "Documents and Statements"
owning_service: "Risk and Reporting with owning banking services"
modules:
  - Document Registry
  - Artifact Storage
  - Document Templates
  - Statement Rendering
  - Receipt and Confirmation Rendering
  - Secure Retrieval
  - Retention and Legal Hold
status: Draft
requirements:
  - RPT-001
  - RPT-002
  - RPT-004
  - LED-013
  - DAT-001
  - DAT-003
  - DAT-005
  - DAT-006
  - DAT-008
  - SEC-008
  - SEC-009
  - SEC-010
  - SEC-011
  - SEC-013
  - SEC-015
  - REG-003
  - NFR-SCAL-001
  - NFR-DR-001
  - NFR-DR-002
  - NFR-OBS-001
  - NFR-OBS-002
  - NFR-I18N-001
  - NFR-MNT-001
stories: [US-023, US-064]
dependencies:
  - Phase 0 architecture, object-storage, privacy and retention decisions
  - 01 Institution and Tenancy
  - 02 Identity, Roles and Permissions
  - 03 Customer and Onboarding
  - 04 Products and Accounts
  - 05 Core Ledger and Financial Posting
  - 06 Workflow and Maker-Checker
  - 07 Payments, Collections and VTU
  - 09 Channels, CMS and Communications
adrs:
  - Document module ownership inside Risk and Reporting
  - Object-storage integrity, encryption and immutability model
  - Statement source snapshot and reproducibility model
  - Secure download and time-limited link model
  - Document retention, disposition and legal-hold model
  - Rendering engine, template governance and supported formats
last_updated: 2026-10-15
---

# Epic: Documents and Statements

## 1. Objective

Deliver the C#/.NET document capability that generates accurate account statements, receipts and confirmations from authoritative owning-service data and securely stores, versions and retrieves KYC and banking documents.

The capability is implemented as explicit `Documents` and `Statements` modules inside the initial Risk and Reporting microservice, with contracts to the services that own the underlying business records. It does not introduce a tenth deployable microservice. Accounts authorises statement requests, Ledger supplies authoritative posted history, Payments/Ledger supply verified receipt facts, Customer and Onboarding owns KYC case/document relationships, and Documents owns artifact bytes, document versions, rendering evidence, retention and controlled retrieval.

Generated artifacts are evidence and customer records, not sources of financial truth. A statement, receipt, report artifact, object-store file or search index can never authorise posting, change a balance or override its source service.

## 2. Success measures

- Authorised statement requests render from an immutable Ledger statement-data snapshot/reference containing posted records, opening/movement/closing totals and source integrity evidence.
- A generated statement can be reproduced or its exact source/template/render versions can be explained without querying mutable projections.
- Receipts and confirmations render only from verified canonical facts supplied by the owning service; pending or uncertain outcomes cannot be presented as final success.
- KYC and banking uploads are quarantined, validated, scanned, classified, encrypted, versioned and linked to the owning business record through opaque references.
- Object-store keys, access tokens and secure links disclose no customer PII or guessable business identifiers.
- Every download/link issuance rechecks current institution, actor, permission, resource relationship, classification and legal/privacy restrictions.
- Secure links are time-limited, narrowly scoped, revocable where the approved design supports it and never treated as durable authorisation.
- Templates are versioned, approved, effective-dated, localised and bound to document type/institution/country/product context.
- Watermark, artifact digest/signature metadata, source manifest and immutable audit evidence support tamper detection.
- Retention, archival, legal hold, deletion/anonymisation and disposition approvals are enforced without silently destroying evidence.
- Malware, unsafe content and unsupported files never enter an Active/retrievable state.
- Backup/restore tests reconcile relational metadata, object versions, source manifests, access state and outbox/inbox without duplicate artifact generation.
- RPT-001, RPT-002, RPT-004, LED-013, US-023 and US-064 have traceable contract, code, security, rendering and recovery evidence.

Performance, file-size, link lifetime, retention, watermarks, supported formats and recovery targets require approved institution/document decisions. Agents must not invent them.

## 3. Scope and ownership

### Included

- Document registry, document types, classifications, owner references and immutable metadata/version history.
- Approved object storage for encrypted artifact bytes with opaque keys and integrity metadata.
- KYC and banking document upload, quarantine, validation, malware/content scanning, activation and versioning.
- Account statement request consumption, Ledger snapshot acquisition, deterministic rendering and artifact publication.
- Receipt and confirmation rendering from verified Ledger/Payments/owning-service facts.
- Document templates, versions, localisation, branding, watermark rules, approval and effective dates.
- PDF statement/receipt output for approved Phase 1 journeys; other formats only if explicitly approved.
- Secure retrieval endpoints and time-limited download-link issuance.
- Document access grants/references, current authorisation checks and sensitive-access audit.
- Retention classification, archive state, legal hold, approved deletion/anonymisation/disposition and evidence.
- Artifact integrity verification and source/template/render manifest retrieval for authorised operations/audit.
- Asynchronous rendering workers with durable checkpoints, idempotency, outbox/inbox and dead-letter repair.
- Object-store/database reconciliation, backup/restore verification, metrics, alerts and runbooks.

### Split ownership

| Concern | Authoritative owner | Documents and Statements responsibility |
| --- | --- | --- |
| Statement requester/account access | Products and Accounts | Consume authorised request and revalidate trusted grant/context |
| Posted transaction history and totals | Core Banking and Ledger | Request stable canonical snapshot; never recompute from reports/cache |
| Payment/transaction final status | Payments and VTU or Ledger | Render only verified facts supplied under versioned contract |
| KYC case and required document relationship | Customer and Onboarding | Store artifact/version and return protected document reference |
| Identity, roles and entitlements | Identity and Access | Enforce document permissions/classification/resource scopes |
| Document bytes and integrity metadata | Documents module/object storage | Authoritative artifact custody and lifecycle |
| Document template/publication | Documents module | Version, approve, publish and bind template |
| General CMS content | Channels and Engagement | Not owned here; document templates have legal/artifact semantics |
| Approval workflows | Operations and Workflow | Submit exact version/disposition/hold action for decision |
| Audit/legal evidence exports | Audit and Reconciliation | Publish trace links and controlled evidence references |

### Excluded

- Journal posting, balance calculation, transaction correction, statement totals or transaction finality decisions.
- Account ownership/statement-period authorisation, except enforcing the trusted access decision and current document access at retrieval.
- KYC case decisions, customer risk state or determining which documents satisfy onboarding policy.
- Payment state changes, provider verification or reconciliation break resolution.
- CMS articles, notification templates and channel conversation content.
- Full operational, management, financial-position and regulatory reporting from RPT-003 and RPT-005 through RPT-008; those belong to later Risk and Reporting epics unless explicitly allocated.
- Scheduled report distribution and US-065 Phase 2 scope.
- Data-governance catalogue/lineage programme and US-066 Phase 5 scope, beyond source-manifest lineage required for these artifacts.
- Electronic signatures, agreements, bulk statements, regulatory submissions and certificate types unless separately approved.
- Arbitrary user-authored HTML/document conversion or execution of document macros/scripts.

### Phase 1 document types

Required: account statements, transaction/payment receipts, confirmations required by approved banking journeys, KYC/onboarding uploads and approved banking-record uploads. RPT-001 names agreements, certificates and regulatory records, but concrete Phase 1 types/templates require explicit allocation. Foundations may exist while unapproved types remain disabled.

## 4. Service and module boundaries

The initial deployment stays within Risk and Reporting:

```text
CoreBanking.RiskReporting.Domain
  Modules/Documents
  Modules/Statements
  Modules/Reporting

CoreBanking.RiskReporting.Application
  Modules/Documents
  Modules/Statements
  Modules/Receipts
  Modules/Retention

CoreBanking.RiskReporting.Infrastructure
  Persistence
  ObjectStorage
  Rendering
  MalwareScanning

CoreBanking.RiskReporting.Api
CoreBanking.RiskReporting.Contracts
CoreBanking.RiskReporting.Workers
```

Rules:

1. Document and Statement modules communicate through application contracts, not EF entities.
2. Reporting read models may help locate/run operational reports but cannot supply authoritative statement balances or posted lines.
3. Object storage is authoritative only for owned artifact bytes; PostgreSQL metadata is authoritative for document lifecycle/access state.
4. Object storage, search and relational state are reconciled; neither silently overwrites the other.
5. A future extraction into a dedicated Document service requires an ADR, contract/data migration and operational justification.

## 5. Actors, roles and permissions

| Actor | Permission | Contextual scope | Step-up | Approval |
| --- | --- | --- | --- | --- |
| Customer | `document.self.view` | Own customer/account and allowed document types | According to channel/classification | None |
| Customer | `document.self.download` | Own active artifact and authorised period | According to channel/classification | None |
| Staff user | `document.view` | Institution/branch/customer/document class/purpose | Step-up for sensitive classes | None |
| Staff user | `document.download` | Same plus explicit business need | Step-up for sensitive classes | None |
| Document uploader | `document.upload` | Institution/customer/case/document type | Step-up where sensitive | None |
| Document classifier | `document.classify` | Institution/document type/classification | Yes for restricted classes | Cannot approve own exceptional downgrade |
| Document version manager | `document.version.create` | Institution/owner/document type | Step-up where sensitive | None |
| Template author | `document.template.create` | Institution/country/type/locale | No | Cannot approve/publish own restricted version |
| Template approver | `document.template.approve` | Institution/country/type/version | Yes | Cannot approve own version |
| Template publisher | `document.template.publish` | Institution/environment/type/version | Yes | Approved version only |
| Retention administrator | `document.retention.manage` | Institution/country/record class | Yes | Maker-checker |
| Legal-hold officer | `document.legal_hold.apply` | Institution/document/case/legal authority | Yes | Reason/evidence required |
| Legal-hold approver | `document.legal_hold.release` | Exact hold and authority scope | Yes | Cannot release own hold where separation applies |
| Disposition operator | `document.disposition.execute` | Approved eligible artifact/version | Yes | Approved disposition request |
| Audit/evidence reader | `document.evidence.view` | Institution/classification/case/reference | Step-up | Purpose and access audit |
| Account service identity | `document.statement.request.consume` | Exact authorised statement request/account/period | Service authentication | No human role |
| Ledger service identity | `document.statement.source.provide` | Exact statement-data request | Service authentication | No human role |
| Customer service identity | `document.owner.link` | Exact KYC/customer case/document requirement | Service authentication | No human role |
| Payments/Ledger identity | `document.receipt.fact.provide` | Exact verified business fact | Service authentication | No human role |

Identity and Access owns grants and entitlement versions. The owning banking service authorises its source resource/request. Documents independently enforces institution, actor, classification, owner-reference, purpose, branch/customer/account and current access scope at upload, metadata view, link issuance and retrieval.

Possession of a `DocumentId`, artifact reference, object key or expired link grants no access. Service identities cannot inherit human download permissions.

## 6. Domain model

### Aggregates and entities

| Concept | Responsibility | Identity | Important relationships |
| --- | --- | --- | --- |
| Document | Stable record identity, type, owner and lifecycle | `DocumentId` | Has immutable versions and holds |
| DocumentVersion | One immutable logical file/version | `DocumentVersionId` | Artifact, scan, classification and source refs |
| Artifact | Stored bytes, media type, length and integrity metadata | `ArtifactId` | Opaque object-store version/reference |
| UploadSession | Controlled direct/proxied upload lifecycle | `UploadSessionId` | Expected type/size/owner and expiry |
| ScanResult | Malware/content-safety result and engine/version evidence | `ScanResultId` | Document version/artifact digest |
| DocumentClassification | Approved data/record/access classification | `ClassificationVersionId` | Document type and institution/country policy |
| DocumentTemplate | Stable generated-document template identity | `DocumentTemplateId` | Has immutable versions/localisations |
| TemplateVersion | Render schema, layout, assets and publication state | `TemplateVersionId` | Document type/institution/country/locale |
| GenerationRequest | One idempotent artifact-generation intent | `GenerationRequestId` | Source manifest, template and result artifact |
| StatementSourceManifest | Exact Ledger snapshot/source lineage | `StatementSourceManifestId` | Account/period/snapshot/totals/hash/version refs |
| ReceiptSourceManifest | Exact verified owner fact lineage | `ReceiptSourceManifestId` | Transaction/payment/status/components refs |
| SecureAccessGrant | Short-lived scoped permission evidence for artifact access | `AccessGrantId` | Actor/audience/artifact/action/expiry |
| DownloadAccessEvent | Immutable issuance/retrieval outcome evidence | `DownloadAccessEventId` | Grant/artifact/actor/correlation |
| RetentionAssignment | Effective policy bound to record/version | `RetentionAssignmentId` | Policy version, trigger/date and disposition |
| LegalHold | Prevents disposition under documented authority | `LegalHoldId` | Document/version/case/scope/status |
| DispositionRequest | Approved archive/delete/anonymise action | `DispositionRequestId` | Eligibility, holds and evidence |

### Value objects

- `InstitutionId`, `DocumentTypeCode`, `RecordClass`, `DataClassification` and `OwnerReference`.
- `CustomerReference`, `AccountReference`, `KycCaseReference`, `TransactionReference` and `StatementRequestReference`.
- `ObjectReference`, `ObjectVersionReference`, `MediaType`, `FileExtension`, `ByteLength` and `ArtifactDigest`.
- `SourceManifestDigest`, `TemplateDigest`, `RenderEngineVersion`, `WatermarkPolicyVersion` and `ArtifactIntegrityStatus`.
- `StatementPeriod`, `StatementSnapshotReference`, `BusinessDate`, `PostingDate`, `ValueDate` and `TransactionTimestamp`.
- `Money` with `decimal` amount and explicit ISO currency for rendered financial facts.
- `Locale`, `TimeZoneId`, `FileFormat`, `PageLayout`, `AccessibilityProfile` and `DocumentStatus`.
- `RetentionPolicyVersion`, `RetentionTrigger`, `DispositionAction`, `LegalHoldAuthorityReference` and `AccessPurpose`.
- `IdempotencyKey`, `CorrelationId`, `CausationId`, `DateTimeOffset` and `DateOnly` where appropriate.

### Invariants

1. Every tenant-owned document, artifact, template, request, grant and lifecycle record has a trusted institution ID.
2. A document version is immutable after upload/generation commits; changes create a successor version.
3. Object keys are opaque, non-guessable and contain no PII, account number, transaction reference or filename supplied by the user.
4. Artifact length, media type, object version and approved cryptographic digest are recorded and verified before activation/download.
5. An uploaded artifact remains Quarantined until required malware/content validation succeeds.
6. Unsupported, malformed, encrypted-without-approved-handling or suspicious content never becomes Active.
7. Statement lines/totals come only from the exact Ledger snapshot/reference; Documents does not query replicas/analytics or recalculate accounting truth.
8. Opening balance + posted movements reconciles to closing balance per currency/accounting scope under the Ledger contract.
9. Pending/failed/disputed display items, if approved for inclusion, are clearly separated and excluded from posted totals.
10. Receipt/confirmation status and monetary components come from the verified source manifest; rendering cannot upgrade or change them.
11. Generation binds exact source, template, branding, locale, watermark and render-engine versions and records their digests.
12. The same generation idempotency scope/request digest produces one logical artifact outcome, including after worker restart.
13. Only approved effective templates may render production artifacts.
14. Secure access is scoped to exact actor/audience, action, artifact/version, purpose and expiry; it cannot be broadened by request fields.
15. Current authorisation is evaluated before metadata disclosure and link/grant issuance. Retrieval uses the approved secure-link/gateway control.
16. An Active legal hold prevents deletion, anonymisation or destructive replacement within its scope.
17. Disposition preserves required tombstone, approval and integrity/audit evidence and never rewrites prior access history.
18. Reports, artifacts and object storage cannot post, reverse, close an account or become a balance source.
19. Cross-service references are opaque; Documents never reads another service's private database.
20. No object-store/network call occurs while a relational database transaction is held open.

Database constraints, object versioning/retention controls, optimistic concurrency and application checks enforce these invariants under concurrent generation, download, hold and disposition operations.

## 7. State transitions

Uploaded document version:

```text
UploadInitiated -> UploadReceived -> Quarantined -> Validating -> Scanning -> Active
         |              |               |             |           |
         +----------> Expired           +----------> Rejected <----+
Active -> Superseded | Archived | Restricted
Active/Archived/Restricted -> DispositionPending -> Disposed
```

Generated artifact:

```text
Requested -> SourcePending -> SourceReady -> Rendering -> IntegrityChecking -> Ready
      |            |              |             |                 |
      +--------> Rejected         +----------> Failed <-----------+
Ready -> Superseded | Archived | Restricted
```

Template version:

```text
Draft -> Submitted -> Approved -> Scheduled -> Published -> Superseded
   |         |           |           |            |
   +------> Withdrawn <- Rejected ----+------------+-> Withdrawn
```

Legal hold:

```text
Proposed -> Active -> ReleaseProposed -> Released
    |          |             |
    +------> Rejected        +------> Rejected
```

Disposition:

```text
Proposed -> EligibilityChecked -> PendingApproval -> Approved -> Executing -> Completed
    |               |                  |               |             |
    +------------> BlockedByHold       +------------> Rejected      +-> Failed
```

Status transitions are typed, concurrency-checked and append-only in history. A relational Ready/Active status is set only after object existence/version/digest verification succeeds.

## 8. Source authority and reproducibility

### Statement source

The Account service publishes or submits an authorised `StatementRequested` reference. Documents requests an exact statement-data snapshot from Ledger using the same institution/account/period/request context. Ledger returns or asynchronously prepares:

- Stable snapshot/reference and schema version.
- Account/currency/period and business/as-of timestamps.
- Opening and closing balances by approved balance concept.
- Posted transaction lines with posting/value/transaction dates kept distinct.
- Explicit debit/credit/amount/currency/running-balance semantics.
- Reversal/correction links and approved display status.
- Totals/counts and source integrity digest.
- Separate optional non-posted items if policy permits them.

Documents validates contract completeness and source digest, but Ledger owns financial correctness. Any mismatch becomes generation failure/investigation, not local correction.

### Receipt source

The owning transaction service supplies a versioned verified receipt fact including status, bank/transaction/provider references where safe, explicit components, currency, source timestamps and verification version. Documents renders exactly that fact. An `Uncertain`, `Pending`, callback-only or unverified provider result cannot use a Success receipt template.

### Reproducibility manifest

Every generated artifact records source contract/schema/version, source reference/digest, template/version/digest, institution branding version, locale/timezone/formatting policy, watermark policy, renderer/version, generation time, artifact digest/object version and correlation/causation IDs. Reproduction permissions and retention follow the document classification.

## 9. Use cases

### UC-01 - Generate an account statement

- Actor: Account service identity after customer/staff authorisation.
- Preconditions: Authorised request, allowed account/period/format, active template/policy and Ledger availability.
- Permission: `document.statement.request.consume` for exact institution/request audience.
- Main flow: Inbox-deduplicate request; create generation record; request stable Ledger snapshot; validate source manifest/totals; select exact template/locale/branding/watermark; render; verify artifact; store object/version; publish Ready reference.
- Failure outcomes: Access/request mismatch, unavailable snapshot, reconciliation mismatch, no approved template, rendering/storage/integrity failure.
- Idempotency: Institution + statement request ID + source snapshot + output variant.
- Audit: Requester/source refs, versions/digests, generation/result, no statement body.
- Events: `StatementDocumentReady` or `DocumentGenerationFailed`.

### UC-02 - Generate a receipt or confirmation

- Actor: Payments/Ledger/owning service identity.
- Preconditions: Verified source fact and allowed artifact type/status.
- Main flow: Validate source service/audience/schema/finality; deduplicate; bind source/template versions; render/store/verify; publish artifact reference to owner.
- Failure outcomes: Unverified/uncertain source, amount/component mismatch, unsupported status/template or storage failure.
- Rule: Rendering never calls a provider to establish transaction truth.
- Idempotency: Source business reference + receipt purpose + source fact version + variant.

### UC-03 - Upload a KYC or banking document

- Actor: Customer/authorised staff through Customer or Document API.
- Preconditions: Trusted institution/owner/document requirement, allowed classification/type/size and upload policy.
- Main flow: Create short-lived upload session; upload to quarantine under opaque key; verify exact byte count/digest/media; scan/inspect; create immutable version; activate and return protected document ref to Customer owner.
- Failure outcomes: Expired session, cross-tenant owner, unsupported/mismatched type, malicious/unsafe file, size breach, storage or scan failure.
- Idempotency: Institution + owner requirement + upload command/file digest under policy.
- Rule: Client filename/MIME does not determine trusted type or object key.

### UC-04 - Add a new document version

- Actor: Document uploader/version manager.
- Preconditions: Existing document/owner relation, permission, reason and allowed version policy.
- Main flow: Upload/scan successor; compare type/classification; activate successor; mark prior current version Superseded without deleting it; notify owner.
- Failure outcomes: Wrong owner/type, legal restriction, downgrade attempt, malicious file or concurrent successor conflict.
- Idempotency: Document + version command ID/digest.
- Audit: Prior/new versions, actor/reason/classification and owner refs.

### UC-05 - Retrieve document metadata

- Actor: Customer/staff/service identity.
- Preconditions: Current identity/permission, institution/owner/classification/purpose scope.
- Main flow: Authorise without revealing existence across scope; return masked metadata/current lifecycle only.
- Failure outcomes: Unknown/cross-tenant/unauthorised/restricted/disposed.
- Rule: Metadata does not expose object key, internal storage path, scan internals or other owner refs.

### UC-06 - Issue and use a secure download

- Actor: Authorised customer/staff/service.
- Preconditions: Current permission/owner/classification/purpose, Active/allowed artifact and no access restriction.
- Main flow: Reauthorise; create narrowly scoped short-lived grant; audit issuance; deliver through approved gateway or time-limited object access; verify artifact/object version/digest; audit success/failure.
- Failure outcomes: Grant expired/replayed beyond policy, actor/audience mismatch, revoked access, legal restriction, integrity mismatch or missing object.
- Idempotency: Link/grant request ID; each access event remains append-only.
- Rule: Link tokens are never logged and object keys remain hidden.

### UC-07 - Publish a document template

- Actors: Template author, approver and publisher.
- Preconditions: Approved document type/schema/assets/locales/watermark/access context.
- Main flow: Create immutable draft; validate placeholders/layout/font/assets/output safety; preview with synthetic data; submit/approve; schedule/publish exact version; supersede prior version at effective time.
- Failure outcomes: Unknown/protected placeholder, unsafe external resource, missing locale/font, self-approval or overlapping effective version.
- Audit: Body/assets digests, participants, decisions and effective window.

### UC-08 - Apply or release legal hold

- Actors: Legal-hold officer and approver.
- Preconditions: Authority/case/evidence scope and exact document/owner/query criteria.
- Main flow: Resolve immutable target set/scope; activate hold; block disposition; publish evidence. Release requires separate authority/approval and never erases hold history.
- Failure outcomes: Invalid authority, cross-tenant scope, ambiguous target, self-release conflict or concurrent disposition.
- Idempotency: Authority/case + target scope + hold request ID.

### UC-09 - Execute retention disposition

- Actor: Scheduled worker plus authorised operator/approver where required.
- Preconditions: Effective retention assignment, eligibility date, no active legal hold, owner/compliance checks and approval.
- Main flow: Freeze target version; recheck holds/concurrency; archive/delete/anonymise under exact action; verify object outcome; retain permitted tombstone/digest/evidence; publish disposition event.
- Failure outcomes: Hold, changed policy/owner state, object lock, partial deletion, storage failure or approval expiry.
- Idempotency: Document/version + policy assignment + disposition occurrence.

### UC-10 - Verify artifact integrity

- Actor: Automated worker or authorised evidence reader.
- Preconditions: Artifact metadata/object reference and permission for manual evidence view.
- Main flow: Fetch/stream safely; recompute approved digest/verify signature/object version; compare manifest; record result; restrict access and alert on mismatch.
- Failure outcomes: Missing object/version, digest mismatch, key failure or corrupted metadata.
- Rule: Verification never mutates source financial/customer data.

## 10. API contracts

### Customer/staff document APIs

| Method | Route | Permission | Request | Response | Idempotent |
| --- | --- | --- | --- | --- | --- |
| POST | `/api/v1/documents/upload-sessions` | `document.upload` | Owner/type/classification/expected metadata | Short-lived upload session | Yes |
| POST | `/api/v1/documents/upload-sessions/{id}/complete` | `document.upload` | Object receipt/digest/expected version | Quarantined document version | Yes |
| POST | `/api/v1/documents/{id}/versions` | `document.version.create` | Upload-session/reason/expected version | Successor version ref | Yes |
| GET | `/api/v1/documents/{id}` | `document.view` or self | Purpose/context | Masked metadata | Query |
| GET | `/api/v1/documents/{id}/versions` | Scoped document view | Bounded page | Masked version history | Query |
| POST | `/api/v1/documents/{id}/download-grants` | `document.download` or self | Version/purpose/channel | Short-lived grant/URL metadata | Yes |
| POST | `/api/v1/documents/{id}/legal-holds` | `document.legal_hold.apply` | Authority/case/scope/reason | Hold ref/status | Yes |
| POST | `/api/v1/documents/{id}/disposition-requests` | `document.retention.manage` | Policy/action/reason | Disposition request | Yes |

### Template and evidence APIs

| Method | Route | Permission | Request | Response | Idempotent |
| --- | --- | --- | --- | --- | --- |
| POST | `/api/v1/document-templates` | `document.template.create` | Type/scope/schema | Template/draft | Yes |
| POST | `/api/v1/document-templates/{id}/versions` | `document.template.create` | Layout/assets/locales | Draft version | Yes |
| POST | `/api/v1/document-template-versions/{id}/preview` | Template preview permission | Synthetic facts only | Preview artifact/ref | No business state |
| POST | `/api/v1/document-template-versions/{id}/submit` | `document.template.create` | Expected version/reason | Workflow ref | Yes |
| POST | `/api/v1/document-template-versions/{id}/publish` | `document.template.publish` | Approval ref/effective window | Publication status | Yes |
| GET | `/api/v1/document-artifacts/{id}/manifest` | `document.evidence.view` | Purpose | Redacted source/render manifest | Query |
| POST | `/api/v1/document-artifacts/{id}/integrity-checks` | Evidence/integrity permission | Reason | Check ref/result state | Yes |

### Internal APIs

| Method | Route | Service permission | Request | Response | Idempotent |
| --- | --- | --- | --- | --- | --- |
| POST | `/internal/v1/statements/generation-requests` | Account service | Authorised request/account/period/variant | Generation/status ref | Yes |
| GET | `/internal/v1/statements/generation-requests/{id}` | Scoped owner service | None | Generation/artifact status | Query |
| POST | `/internal/v1/receipts/generation-requests` | Owning fact provider | Verified fact ref/type/variant | Generation/status ref | Yes |
| POST | `/internal/v1/documents/owner-links` | Customer/owner service | Owner/requirement/document ref | Link result | Yes |
| GET | `/internal/v1/documents/{id}/status` | Scoped owner service | Owner context | Lifecycle/classification status | Query |

The statement generation API does not accept transaction lines, balances or totals from a frontend. Documents obtains authoritative data from Ledger using the trusted statement request/snapshot contract. Receipt input is accepted only from allow-listed owning service identities and exact schemas.

Stable errors include `document.cross_tenant_denied`, `document.owner_scope_denied`, `document.classification_denied`, `document.upload_expired`, `document.file_type_rejected`, `document.scan_failed`, `document.integrity_failed`, `document.legal_hold_active`, `document.template_unavailable`, `statement.source_mismatch`, `statement.reconciliation_failed` and `document.access_grant_expired`.

All list/history endpoints are paginated. Errors do not reveal document existence, object keys, paths, digests or protected owner information to unauthorised callers.

## 11. Event contracts

### Produced events

| Event | Trigger | Key fields | Ordering key | Consumers |
| --- | --- | --- | --- | --- |
| `DocumentVersionActivated` | Upload/version passes controls | Document/version/type/classification/owner ref/digest ref | Document | Customer owner, Audit |
| `DocumentVersionRejected` | Validation/scan rejects | Document/version/safe reason | Document | Owner/Operations |
| `DocumentAccessGranted` | Secure access grant issued | Document/artifact/actor/audience/purpose/expiry ref | Document | Audit |
| `DocumentIntegrityFailed` | Object/manifest verification mismatch | Artifact/object version/check ref | Artifact | Security, Operations, Audit |
| `DocumentTemplatePublished` | Approved version effective | Template/type/scope/version/digest/effective time | Template | Render workers, Audit |
| `StatementDocumentReady` | Statement artifact verified/stored | Statement request/generation/artifact/source snapshot refs | Statement request | Accounts, Channels, Audit |
| `ReceiptDocumentReady` | Receipt artifact verified/stored | Source business/generation/artifact/status refs | Source business ref | Owner, Channels, Audit |
| `DocumentGenerationFailed` | Generation terminal failure | Request/generation/safe reason/source ref | Generation | Owner, Operations |
| `DocumentLegalHoldApplied` | Hold becomes active | Hold/document/scope/authority/case refs | Document | Owner, Audit |
| `DocumentLegalHoldReleased` | Approved release effective | Hold/release authority/ref/time | Document | Owner, Audit |
| `DocumentDispositionCompleted` | Approved disposition verified | Document/version/action/policy/evidence refs | Document | Owner, Audit |

### Consumed events

| Event | Producer | Consumer behaviour | Deduplication key | Failure handling |
| --- | --- | --- | --- | --- |
| `StatementRequested` | Products and Accounts | Create exact idempotent generation request | Event ID + statement request ID | Inbox/DLQ; no duplicate artifact |
| `StatementDataPrepared` | Ledger | Bind exact source manifest and resume rendering | Event ID + snapshot ref | Inbox; mismatches investigated |
| Verified receipt fact event | Ledger/Payments/owner | Create approved receipt generation request | Event ID + purpose + fact version | Inbox; uncertain fact rejected |
| `KycDocumentRequirementChanged` | Customer | Update allowed owner-link requirement projection | Event ID + case/requirement version | Inbox; deny on stale/missing |
| `CustomerAccessRestricted` | Customer/Identity | Restrict relevant document access projection | Event ID + subject/version | Inbox; fail closed if freshness required |
| `WorkflowDecisionRecorded` | Workflow | Apply exact template/hold/disposition decision | Event ID + target/version | Inbox; mismatches rejected |
| `RetentionPolicyPublished` | Institution/Compliance configuration | Assign future/effective policy version | Event ID + policy version | Inbox; no silent retroactive deletion |

Every event includes event ID/type/schema version, institution ID, occurred time, correlation/causation IDs and aggregate/business reference. Events never contain artifact bytes, secure-link tokens, object keys, full account numbers, unrestricted filenames, KYC images or unnecessary PII.

## 12. Database and object-storage design

### Ownership

- Database: `risk_reporting_db`.
- Added schemas for this module: `document`, `statement`; existing `reporting` and `integration` remain separate.
- Migration assembly: `CoreBanking.RiskReporting.Infrastructure`.
- Object storage: approved institution/environment-scoped buckets/containers with opaque object IDs and object versioning/immutability controls as approved.
- Database principal: Risk and Reporting runtime/migration identities only, separated by privilege.
- Object-storage identity: dedicated workload identity with least-privilege per environment and operation.

The architecture baseline calls its schema list suggested rather than exhaustive. Adding `document` and `statement` requires the module-ownership ADR and review; it does not create another database/service.

### Tables

| Table | Purpose | Key constraints | Tenant-owned | Retention |
| --- | --- | --- | --- | --- |
| `document.documents` | Stable document/type/owner lifecycle | Unique institution + document ref | Yes | Record policy |
| `document.document_versions` | Immutable logical version metadata | Unique document + version; predecessor link | Yes | Record/legal policy |
| `document.artifacts` | Object ref/version, size, type and integrity | Unique object ref/version and artifact digest scope | Yes | Artifact policy |
| `document.upload_sessions` | Short-lived expected upload controls | Unique session; expiry/state checks | Yes | Short operational period |
| `document.scan_results` | Scanner/validator version and safe outcome | Unique artifact + required scan profile/version | Yes | Security evidence policy |
| `document.classifications` | Effective classification assignment/history | Unique document/version + assignment version | Yes | Permanent history |
| `document.owner_links` | External owner/case/account relationship refs | Unique owner service + owner ref + document + purpose | Yes | Owner/record policy |
| `document.templates` | Stable template/type/scope identity | Unique institution + type + template code | Yes | Life + evidence |
| `document.template_versions` | Immutable layout/schema/assets/publication | Unique template + version | Yes | Template evidence policy |
| `document.template_localisations` | Localised approved assets/text/fonts | Unique template version + locale | Yes | Version lifetime |
| `document.access_grants` | Hashed/scoped short-lived grant evidence | Unique grant ref/hash; expiry/action/audience checks | Yes | Security policy |
| `document.download_access_events` | Append-only issuance/access outcomes | Unique access event ID | Yes | Audit policy |
| `document.retention_assignments` | Policy/version/trigger/disposition state | Unique document/version + effective assignment | Yes | Permanent evidence |
| `document.legal_holds` | Immutable hold/release lineage | Unique authority/case + target scope + occurrence | Yes | Legal policy |
| `document.disposition_requests` | Approved archive/delete/anonymise workflow | One active request per target/action | Yes | Permanent evidence |
| `document.integrity_checks` | Scheduled/manual verification evidence | Unique artifact + check occurrence | Yes | Evidence policy |
| `statement.generation_requests` | Statement/receipt generation state/idempotency | Unique source request + purpose + variant | Yes | Document policy |
| `statement.source_manifests` | Exact source/template/render lineage | Unique generation + source snapshot/fact version | Yes | Artifact/evidence policy |
| `statement.generated_artifacts` | Generation-to-artifact relationship/status | Unique generation + output variant | Yes | Artifact policy |
| `integration.idempotency_records` | Stable command/generation results | Unique institution + operation + key; digest | Yes | Replay-window policy |
| `integration.outbox_messages` | Durable event publication | Unique event ID | Yes | Operational policy |
| `integration.inbox_messages` | Consumer deduplication | Unique producer + event ID + consumer | Yes | Replay-window policy |

### Constraints and indexes

- Tenant-owned indexes begin with `institution_id`; owner lookup indexes include owner service/type/reference and classification.
- Versions, manifests, access events, scan results, legal-hold history and published template versions are append-only under runtime privileges.
- Partial uniqueness permits only one current document version and one effective published template per exact scope/time.
- Generation idempotency includes source request, source snapshot/fact version, purpose and output variant; request digest detects conflicts.
- Artifact state can become Active/Ready only with verified object reference/version/digest, required scan results and allowed classification.
- Disposition state transition checks active legal holds with concurrency protection immediately before destructive object action.
- Object keys and stored filenames are server-generated opaque identifiers. Original filenames, if retained, are encrypted/classified metadata and never used as paths.
- Query indexes cover lifecycle/status age, scan backlog, generation backlog, owner scope, expiry, legal hold and retention due date without exposing plaintext PII.

### Object-storage controls

- Separate environments and institution/tenancy partitions according to approved isolation policy.
- Server-side encryption with managed key references, TLS and least-privilege workload identities.
- Object versioning, retention/object lock where required, lifecycle rules and access logging.
- Public access disabled. Applications never construct public URLs from keys.
- Approved content-length/type checks and multipart controls; abandoned uploads expire safely.
- Integrity metadata verified on upload completion, generation publication, retrieval where required and scheduled checks.
- Backup/replication/residency follow institution/country/document classification.
- Object lifecycle cannot delete an item that relational policy/legal-hold state requires; jobs reconcile before/after changes.

### Migration and backfill

- Create relational schemas/constraints, object-store boundaries and workload identities before accepting files.
- Seed only document/template type metadata and synthetic templates; no customer documents or production branding secrets.
- Existing files are imported through inventory, owner mapping, classification, scanning, digesting, versioning and approval evidence.
- Backfills are bounded/idempotent and cannot publish/download/dispose artifacts as an unintended side effect.
- Released migrations are append-only. Changes to object keys, encryption, digests or retention need dual-read/migration/verification/forward-fix plans.
- Verification detects orphan metadata/objects, missing object versions, digest mismatches, duplicate current versions, overlapping templates and illegal disposition under hold.

## 13. Consistency, concurrency and idempotency

- Relational state/status history/outbox commits atomically. Object upload/render occurs outside the transaction.
- Use a staged protocol: reserve generation/upload, write object, verify object/version/digest, then atomically mark Active/Ready and publish.
- A crash after object write but before metadata commit leaves a quarantined/orphan candidate for reconciliation, never an accessible artifact.
- A crash after relational Ready commit is prevented by verifying durable object state before the commit; reconciliation detects provider anomalies.
- Repeated identical generation requests return the same logical generation/artifact. Changed source/template/variant under the same key is a conflict.
- Concurrent new versions use expected current version and a unique successor constraint; only one becomes current.
- Concurrent download-grant requests may issue separate scoped grants but do not change artifact state; each issuance/access is audited.
- Legal hold/disposition races use optimistic concurrency plus a final locked eligibility/hold check before object deletion/anonymisation.
- Outbox/inbox replay cannot duplicate generated artifacts, owner links, access history, legal holds or disposition.
- Cross-service workflows use stable references/events and reconciliation, not distributed transactions.

## 14. Statement and receipt correctness

- Documents accepts only Ledger's statement snapshot as the source for posted lines/totals.
- Preserve transaction timestamp, business date, posting date and value date as distinct labelled fields.
- Preserve currency explicitly and never combine currencies into an unlabeled total.
- Use source-provided debit/credit/sign/running-balance semantics; do not infer from narration or account type.
- Reversals/corrections remain separate linked entries; the original line is not rewritten.
- Opening + movements = closing validation is performed per Ledger contract and currency scope.
- Page breaks/subtotals are presentation only and cannot alter source totals.
- Sorting is deterministic using approved source order and stable tie-breakers.
- Masking affects display only, not source manifest/digest comparison.
- Rounding/formatting follows approved currency/locale presentation; financial calculation remains in the source.
- Pending/failed/uncertain/disputed items are included only under approved policy, clearly labelled and excluded from posted totals.
- A receipt reflects exact verified status and explicit fee/tax/commission components supplied by the owner.

Golden statement/receipt fixtures require Finance/Product approval and use synthetic data covering reversals, same-time postings, date boundaries, multiple pages, long safe narrations, unicode/locales if approved and zero/negative display cases.

## 15. Rendering and document quality

- Rendering uses a pinned, approved engine/runtime and records its version.
- Templates use typed placeholder schemas. Arbitrary code, file access, network fetch, SQL, shell commands and unbounded expressions are prohibited.
- Template assets/fonts are approved, versioned, integrity-checked and packaged; production rendering does not fetch arbitrary remote resources.
- Escape/sanitise all untrusted text for the target format and constrain length/layout.
- Enforce deterministic pagination, headers/footers, page numbers, repeated table headings and no clipped/overlapping content.
- Watermarks are versioned by document type/classification/purpose and must not leak unnecessary PII.
- Generated PDFs are visually rendered and inspected in test pipelines for approved golden cases; text extraction and structural checks supplement visual QA.
- Accessibility/tagging requirements and supported PDF profiles require an explicit decision; do not claim compliance without validation.
- CSV/Excel exports and scheduled distribution remain disabled in this Phase 1 epic unless specifically approved and tested.

## 16. Security and privacy

Threats include cross-tenant/object reference access, IDOR, path traversal, malicious PDF/image/archive, macro/script execution, MIME spoofing, parser exploits, decompression bombs, object overwrite, secure-link leakage/replay, recipient substitution, template injection, PII leakage, forged source facts, legal-hold bypass and destructive disposition races.

Controls:

- Resolve institution/actor from trusted context and verify owner/resource/classification scope server-side.
- Generate opaque IDs/keys; never accept caller object paths/bucket names or interpolate filenames into storage paths.
- Validate magic bytes, media type, extension, size, dimensions/page/archive limits and allowed encryption/password policy.
- Quarantine before parsing; scan with isolated, patched tooling and record scanner/signature/profile versions.
- Disable active content, macros, external references, embedded scripts/actions and unsafe archive extraction.
- Use bounded streaming, cancellation, time/memory limits and isolated rendering/scanning workers.
- Encrypt sensitive data at rest and in transit; key references follow institution/environment/classification scope.
- Mask document metadata and generated output by policy; never log bodies, secure tokens, object keys, full account numbers or KYC values.
- Store secure-link/grant tokens only as approved non-reversible verification material; use narrow audience/action/expiry.
- Apply rate limits and abuse controls to uploads, generation, search, link issuance and downloads.
- Scan dependencies/rendering tools and monitor vulnerabilities/certificates.
- Non-production uses synthetic documents/data; production artifacts cannot be copied without approved irreversible masking.

Security tests cover IDOR/cross-tenant refs, path traversal, MIME/polyglot files, malware fixtures, archive bombs, parser timeout, embedded scripts, template injection, forged owner/source events, object-version substitution, link leakage/replay, stale entitlement, mass assignment, legal-hold race and log/trace/error leakage.

## 17. Retention, legal hold and disposition

- Retention policy is versioned by institution, country pack, record class, document type, classification and trigger.
- Each document/version records the exact policy assignment and computed eligibility date with provenance.
- Retroactive policy changes require explicit migration/reassessment approval; they never silently delete existing records.
- Legal hold can target an exact document/version, owner/case scope or approved bounded set and overrides disposition.
- Release requires authority and preserves the hold/release history.
- Archive maintains retrieval, integrity, encryption, classification and evidential value.
- Deletion/anonymisation occurs only where legally permitted, approved and technically verified across primary, versions, replicas, search/cache and derived artifacts.
- Required tombstone/digest/decision evidence remains without retaining prohibited content.
- Failed/partial disposition remains visible for repair; it is never marked complete optimistically.
- Backup expiry and restored data are included in the approved retention/legal-hold model.

## 18. Audit and observability

Audit upload initiation/completion, scan/classification, version activation/rejection, owner link, metadata/body access, link issuance/download, template creation/approval/publication, generation/source binding, watermark, integrity check, legal hold/release, disposition and exceptional repair.

Evidence includes actor/service, permission/audience, institution, branch/channel where applicable, document/artifact/owner/source refs, classification/purpose, object version and safe digest refs, template/source/render/policy versions, old/new state, reason, approval/authority, correlation/causation and timestamp. Audit summaries exclude artifact bodies, tokens, object keys and unnecessary PII.

Minimum metrics:

- Upload sessions by state/age/size bucket/type and safe rejection reason.
- Quarantine, validation and scan backlog/latency/outcome/engine version.
- Documents/versions by type/classification/lifecycle/owner service without PII labels.
- Statement/receipt generation by state/age/template/source version and failure reason.
- Source mismatch/reconciliation and integrity-check failures.
- Download-grant issuance/use/expiry/denial and anomalous access rate.
- Template publication/locale coverage/render regression.
- Legal holds, due dispositions, blocked/partial disposition and archive retrieval.
- Orphan/missing object reconciliation and object-store/database drift.
- Inbox/outbox/dead-letter lag and restore verification failures.

Traces span Account/Customer/Payments, Ledger source preparation, Documents generation/storage/retrieval, Channels delivery and Audit. Logs use stable masked refs only. Alerts/runbooks cover scan outage/backlog, malware spike, render failure, statement source mismatch, object integrity/missing object, unauthorised access surge, expiring link abuse, legal-hold conflict, disposition failure and restore drift.

## 19. Failure, retry and recovery

| Failure | Safe state | Recovery |
| --- | --- | --- |
| Upload interrupted/expired | Session Expired; partial object inaccessible | Abort/expire multipart; new approved session |
| Validation/scan unavailable | Version remains Quarantined | Retry scan with same artifact/version; no access |
| Malware/unsafe content found | Rejected/isolated | Retain only approved security evidence; notify owner |
| Ledger source unavailable | Generation remains SourcePending/Failed | Retry original request/snapshot protocol |
| Ledger source totals mismatch | Generation fails/investigation | Ledger/Finance resolves; no local correction |
| Render worker crashes | Rendering request remains durable | Resume/retry same generation key/source/template |
| Object write succeeds, DB update fails | Inaccessible orphan/quarantine candidate | Reconcile/delete or attach through controlled repair |
| DB marks ready attempt fails before commit | Prior state remains; object not exposed | Idempotent worker verifies/reuses object |
| Integrity mismatch/missing object | Artifact Restricted; downloads denied | Restore verified version/investigate; never substitute silently |
| Link/grant store unavailable | No new grant; artifact unchanged | Retry after recovery; no broad public link |
| Template withdrawn mid-run | Run uses pinned approved version or fails by exact policy | New request/version if required |
| Legal hold races disposition | Hold/locked eligibility wins; disposition blocked | Re-evaluate with approval |
| Owner access changes | New downloads denied after freshness/current check | Owner/Identity state correction |
| Outbox/consumer failure | Local committed state authoritative | Idempotent retry/DLQ |
| Restore/PITR | Upload/render/disposition workers paused | Reconcile DB, object versions, manifests and events before resume |

Manual repair uses typed authorised commands. Operators cannot edit digests, source totals, object versions, scan results, Ready status, legal holds or disposition evidence directly. Restore verifies metadata/object inventory, current-version uniqueness, template versions, generation idempotency, grants, holds, retention, outbox/inbox and source-service references.

## 20. Configuration

| Setting | Owner | Secret | Validation | Reload behaviour |
| --- | --- | --- | --- | --- |
| Enabled document/artifact types | Product/Compliance/Owner service | No | Source contract/classification/template | Versioned/effective-dated |
| Allowed upload types/sizes/content profiles | Security/Document owner | No | Scanner/parser/storage limits | Versioned |
| Object store/bucket/key reference | Platform/Security | Credential external | Environment/tenant/residency/encryption | Controlled deployment |
| Scan/validation profile | Security | Engine creds external if any | Engines/signatures/timeouts/fail-safe | Versioned; quarantine on stale |
| Template/layout/assets/locales | Document/Product/Brand | Assets may be protected | Schema/font/link/output QA | Versioned/approved |
| Watermark/tamper-evidence policy | Compliance/Security | Signing key external | Type/classification/purpose/key version | Versioned |
| Statement period/variant/inclusion policy | Finance/Product | No | Ledger contract/reconciliation/display | New requests/versioned |
| Receipt finality/type policy | Owning service/Finance | No | Verified source statuses/components | Versioned |
| Secure link/grant policy | Security/Product | Signing key external | Audience/action/expiry/replay/channel | Versioned |
| Retention/archive/disposition policy | Compliance/Legal | No | Country/class/trigger/hold/approval | Versioned/effective-dated |
| Backup/replication/residency policy | Platform/Compliance | Key refs external | Classification/country/RTO/RPO | Controlled rollout |

Missing/stale template, scanner, source, key, retention or authorisation policy disables the affected action. Feature flags cannot bypass quarantine, scanning, source authority, permission, integrity, legal hold, audit or retention.

## 21. Implementation slices

1. Approve module ownership, Phase 1 document types, source contracts, formats, object storage, secure access, retention and legal-hold ADRs.
2. Create C# Documents/Statements boundaries, architecture tests and versioned contracts without production files.
3. Provision tenant/environment object-store controls and implement artifact metadata/opaque-reference persistence with synthetic objects.
4. Implement upload sessions, quarantine, type/size validation, scanning, activation and rejection with malicious-file fixtures.
5. Implement document registry, owner links, classification and successor-version lifecycle.
6. Implement template/version/localisation/assets/preview/Workflow publication and render sandbox.
7. Implement secure metadata view, access grants/download gateway, current authorisation and sensitive-access audit.
8. Implement Account `StatementRequested` consumer and Ledger statement snapshot contract using synthetic authoritative fixtures.
9. Implement deterministic PDF statement generation, reconciliation checks, manifests, watermark/integrity and Ready event.
10. Implement receipt/confirmation generation one verified owner fact/type at a time.
11. Implement Customer/KYC document upload/link/retrieval contract and classification policies.
12. Implement retention assignment, archive, legal hold/release and approved disposition with race tests.
13. Implement object/database reconciliation, integrity workers, orphan repair, dashboards, alerts and runbooks.
14. Complete rendering visual QA, security/parser isolation, performance, concurrency, accessibility/localisation decisions and end-to-end evidence.
15. Complete backup/PITR/object restore, source-reference reconciliation and safe worker-resume evidence.

Each slice starts with failing traceable tests and leaves unapproved document types/templates/formats disabled. Use synthetic files/data only until security and privacy controls pass.

## 22. Test plan

| Test ID | Requirement/story | Level | Scenario | Expected evidence |
| --- | --- | --- | --- | --- |
| DOC-001 | Architecture/DAT-001 | Architecture | Inspect module/service dependencies | No tenth service/cross-database access; explicit contracts |
| DOC-002 | US-064 / SEC-009 | Security | Cross-tenant document/version/artifact ID | Denied without existence disclosure |
| DOC-003 | US-064 | Integration | Valid KYC document upload | Quarantine, scan, classify, version, owner ref |
| DOC-004 | US-064 / SEC-011 | Security | Path traversal/client object key | Ignored/rejected; server opaque key only |
| DOC-005 | US-064 | Security | MIME/extension/magic mismatch | Rejected in quarantine |
| DOC-006 | US-064 / SEC-011 | Security | Malware/polyglot/script/macro fixture | Rejected/isolated; no parser execution |
| DOC-007 | US-064 | Resilience | Scanner unavailable | Remains Quarantined; no download |
| DOC-008 | DAT-008 / US-064 | Domain | Upload successor document version | Prior unchanged; current successor linked |
| DOC-009 | SEC-013 | Security | Metadata/log/event/error inspection | Object key/full PII/body absent or masked |
| DOC-010 | SEC-009/010 | Security | Sensitive document without step-up/purpose | Denied and audited |
| DOC-011 | RPT-002 / US-023 | Integration | Issue secure statement download | Short-lived exact-scope grant; audited |
| DOC-012 | RPT-002 | Security | Expired/wrong-audience/replayed grant | Denied without object disclosure |
| DOC-013 | RPT-002 | Security | Direct object-store public access | Disabled by policy/test |
| DOC-014 | RPT-001 / US-023 | Integration | Generate statement from authorised request | Verified artifact and manifest returned |
| DOC-015 | LED-013 / US-023 | Architecture | Attempt statement from report/cache/UI lines | Contract/architecture guard rejects source |
| DOC-016 | LED-013 | Domain | Opening + movements + closing totals | Reconcile exactly per currency/source contract |
| DOC-017 | LED-013 | Domain | Reversal/correction in statement | Separate linked entries; original preserved |
| DOC-018 | LED-013 / DAT-005 | Domain | Transaction/posting/value/business dates differ | Each retained and labelled correctly |
| DOC-019 | LED-013 | Domain | Pending/failed/uncertain items included | Separate section; excluded from posted totals |
| DOC-020 | US-023 | Concurrency | Duplicate statement events/workers | One logical generation/artifact outcome |
| DOC-021 | US-023 | Recovery | Crash after render before DB Ready | Artifact inaccessible; same job repairs/reuses safely |
| DOC-022 | RPT-001 | Integration | Generate verified success receipt | Exact owner fact/components/refs rendered |
| DOC-023 | RPT-001 | Security | Generate success receipt from Uncertain/payment callback | Rejected; no false finality |
| DOC-024 | RPT-002 | Workflow | Author approves/publishes own restricted template | Denied |
| DOC-025 | RPT-002 | Concurrency | Overlapping effective template versions | Constraint/conflict; one effective version |
| DOC-026 | RPT-002 | Security | Template arbitrary code/network/file reference | Rejected by typed sandbox policy |
| DOC-027 | RPT-002 | Visual | Multi-page statement golden render | No clipping/overlap; headers/pages/totals correct |
| DOC-028 | RPT-002 | Visual | Long/localised/masked content render | Approved fonts/layout/fallback; no leakage |
| DOC-029 | RPT-002 / US-023 | Security | Tamper with stored statement bytes/version | Integrity failure; artifact restricted/alerted |
| DOC-030 | RPT-004 / BR-015 | Architecture | Use artifact/report as balance authority | Forbidden dependency/operation denied |
| DOC-031 | DAT-003 | Contract | Inspect generated source manifest | Immutable refs/dates/currency/amount relationships |
| DOC-032 | DAT-006 / SEC-008 | Security | Inspect DB/object/config | Sensitive bytes encrypted; keys referenced externally |
| DOC-033 | DAT-008 | Domain | Correct document metadata/classification | New history/version; prior evidence retained |
| DOC-034 | SEC-015 | Workflow | Approved legally permitted anonymisation | Exact fields/artifacts processed; evidence preserved |
| DOC-035 | REG-003 | Concurrency | Legal hold races disposition | Hold blocks deletion; no partial completion |
| DOC-036 | REG-003 | Security | Ordinary admin releases hold | Denied; immutable hold history |
| DOC-037 | Retention | Domain | Policy change affects existing records | No silent retroactive deletion; reviewed reassignment |
| DOC-038 | Retention | Recovery | Object deletion partially fails | Disposition remains failed/open for repair |
| DOC-039 | NFR-SCAL-001 | Concurrency | Multiple workers claim generation/scan | One authoritative lease/effect; idempotent completion |
| DOC-040 | NFR-OBS-001 | End-to-end | Trace account request to Ledger/artifact/download | Complete correlated masked evidence |
| DOC-041 | NFR-OBS-002 | Security | Logs/traces/snapshots/DLQ inspection | No body, token, key, OTP/secret or prohibited PII |
| DOC-042 | Messaging | Replay | Replay source/ready/owner-link events | No duplicate artifact/link/access grant |
| DOC-043 | Reconciliation | Integration | Object exists without metadata | Detected/quarantined/repaired under policy |
| DOC-044 | Reconciliation | Integration | Metadata Ready but object missing/wrong version | Download denied; alert/investigation |
| DOC-045 | NFR-DR-001/002 | Recovery | DB/object/PITR restore | Inventory/manifests/holds/idempotency/events reconcile |
| DOC-046 | US-064 | End-to-end | Upload KYC doc then authorised retrieval | Owner link/classification/scan/access/audit pass |
| DOC-047 | US-023 | End-to-end | Request statement then secure download | Account auth, Ledger snapshot, render, link/audit pass |
| DOC-048 | RPT-001 | End-to-end | Verified payment fact to receipt delivery | Correct finality/components/artifact/owner refs |

Golden rendering tests require visual page-image comparison/inspection plus PDF structural/text checks. Security file fixtures are synthetic and isolated; never use live customer documents.

## 23. Acceptance criteria

- [ ] RPT-001, RPT-002, RPT-004, LED-013, US-023 and US-064 trace to decisions, contracts, code and tests.
- [ ] Documents/Statements are explicit C# modules inside Risk and Reporting; no unapproved deployable service is added.
- [ ] Statement requests originate from authorised Accounts flows and data comes only from stable authoritative Ledger snapshots.
- [ ] Posted totals, currencies and distinct business/transaction/posting/value dates reproduce the Ledger source.
- [ ] Receipts/confirmations cannot misrepresent pending, uncertain, unverified or failed outcomes as success.
- [ ] Uploaded artifacts remain quarantined until validation/scanning/classification succeeds.
- [ ] Object keys are opaque and storage is private, encrypted, versioned and reconciled with relational metadata.
- [ ] Access is tenant/owner/classification/purpose scoped and secure links/grants are short-lived and audited.
- [ ] Templates are typed, immutable by version, localised, approved, effective-dated and rendered in an isolated bounded worker.
- [ ] Watermark/integrity/source/template/render evidence supports tamper detection and reproducibility.
- [ ] Legal hold prevents disposition and retention/deletion/anonymisation preserves required evidence.
- [ ] Artifacts/reports/object storage never become a financial source of truth or write path.
- [ ] RPT-003, RPT-005 through RPT-008, US-065/066 and unapproved artifact types/formats remain outside Phase 1 execution.
- [ ] Security, privacy, rendering, concurrency, replay, reconciliation and restore tests pass.

## 24. Definition of Done

- Build, analyzers, formatting, architecture tests and affected suites pass using the repository-pinned .NET SDK.
- EF Core migrations apply to empty/supported upgrade databases; tenant, current-version, publication, idempotency, hold and disposition constraints pass PostgreSQL/Testcontainers tests.
- Object-store isolation, encryption, versioning, access logs, lifecycle and no-public-access controls are verified in each environment.
- Malware/content scanning, parser/render isolation and security regression suites pass with approved synthetic fixtures.
- Ledger statement-source and owner receipt-fact contracts pass compatibility and financial review.
- Approved templates/locales/golden PDFs pass automated structural checks and human/visual QA with retained evidence.
- Secure download/link/grant threat model, permission tests and audit evidence are approved.
- Retention/legal-hold/disposition and object/database reconciliation jobs/runbooks are demonstrated.
- Backup/PITR/object restore proves no missing/substituted artifact, duplicate generation or lost hold/idempotency evidence.
- Traceability lists requirement/story, source/template/render/policy versions, permissions, APIs/events, migrations, artifacts, tests and operational evidence.

## 25. Open decisions

| Decision | Impact | Owner | Required by | Status |
| --- | --- | --- | --- | --- |
| Documents/Statements module ownership ADR | Service/data boundary | Architecture | Slice 1 | Open |
| Phase 1 document/receipt/confirmation types | Scope/templates | Product/Compliance/Domain owners | Slice 1 | Open |
| Approved object store, tenant layout and residency | Security/operations | Platform/Security/Compliance | Slice 1/3 | Open |
| Encryption/key/object-lock/versioning model | Confidentiality/integrity | Security/Platform | Slice 3 | Open |
| Allowed file types/sizes/encryption and scan profiles | Upload attack surface | Security/Customer | Slice 4 | Open |
| Malware/content scanner and isolation runtime | Security/availability | Security/Platform | Slice 4 | Open |
| Document classification and owner-link taxonomy | Access/retention | Compliance/Domain owners | Slice 5 | Open |
| Rendering engine, PDF profile, fonts and accessibility target | Quality/compatibility | Product/Platform/Accessibility | Slice 6 | Open |
| Template approval/publication and branding ownership | Governance | Product/Brand/Compliance | Slice 6 | Open |
| Secure gateway versus direct time-limited object URL | Revocation/audit/performance | Security/Platform | Slice 7 | Open |
| Access-grant lifetime/use/audience policy | Link abuse risk | Security/Product | Slice 7 | Open |
| Statement periods, running-balance/snapshot/order semantics | US-023 correctness | Finance/Product/Ledger | Slice 8 | Open |
| Pending/disputed item statement display policy | Customer accuracy | Finance/Product | Slice 8 | Open |
| Watermark, tamper evidence and signing policy | Evidence/customer sharing | Compliance/Security | Slice 9 | Open |
| Receipt final statuses/fields/components per journey | False finality risk | Domain owner/Finance | Slice 10 | Open |
| Retention triggers/periods/archive/disposition by class | Compliance/storage | Legal/Compliance | Slice 12 | Open |
| Legal-hold authority/scope/release process | Regulatory evidence | Legal/Compliance | Slice 12 | Open |
| Backup/object replication/RTO/RPO | Recovery/residency | Platform/Compliance | Before release | Open |
| Capacity, generation latency and maximum workload | Release/performance | Product/Platform | Before load testing | Open |

## 26. Prohibited assumptions

- Do not create a tenth document microservice without an approved ADR and operational/data justification.
- Do not generate statement totals from account summaries, reporting tables, analytics, Redis, UI transaction lists or document artifacts.
- Do not recalculate or repair Ledger facts inside the rendering service.
- Do not treat a statement/receipt/document as authority to post, reverse, change a payment or close an account.
- Do not accept frontend transaction lines, balances, final status, permissions, owner identity, object key or publication state as trusted.
- Do not render a success receipt from a pending, uncertain, unverified callback or provider-only claim.
- Do not use caller filenames or customer/account/transaction identifiers in object paths/URLs.
- Do not make storage public or treat possession of an object/artifact reference as authorisation.
- Do not log or event document bodies, secure tokens, object keys, full account/identity numbers or unrestricted filenames.
- Do not parse/render untrusted uploads before quarantine and required scanning/validation.
- Do not enable arbitrary template code, scripts, macros, external resource fetches or filesystem/network access.
- Do not edit active document/template versions, source manifests, digests, scan results or access histories in place.
- Do not delete/anonymise/archive under an active legal hold or without exact approved retention policy/evidence.
- Do not claim accessibility, tamper-proof status, non-repudiation or regulatory compliance without approved standards and tests.
- Do not implement RPT-003, RPT-005 through RPT-008, scheduled reporting, Excel/CSV or bulk/regulatory outputs as incidental Phase 1 scope.

Until source, storage, template, security and retention decisions are approved, use synthetic artifacts and keep the document type disabled.

## 27. Agent implementation instructions

- Required reading: root `AGENTS.md`, architecture, Phase 0 evidence, Epics 01-09, this epic, RPT/LED/DAT/SEC/REG/NFR requirements, US-023/US-064 and approved object-storage/privacy policies.
- Expected C# boundaries: `CoreBanking.RiskReporting.Domain`, `.Application`, `.Infrastructure`, `.Api`, `.Contracts`, `.Workers` and tests with explicit Documents/Statements modules.
- Domain/Application must not reference ASP.NET Core, EF Core, object-store SDKs, PDF/render libraries, scanner clients, Ledger/Customer infrastructure or another service database.
- Keep object storage, scanning and rendering implementations behind narrow ports in Infrastructure.
- Use strongly typed IDs/owner refs, immutable versions/manifests, explicit classifications, `decimal`/currency only for source facts, `DateTimeOffset`, `DateOnly`, `TimeProvider` and cancellation tokens.
- Persist generation/upload intent before external/object work; never hold a database transaction during Ledger, scanner, renderer or storage I/O.
- Generate opaque storage keys server-side and centralise safe storage metadata mapping; never expose bucket/key in public contracts.
- Implement staged quarantine/write/verify/activate and object/database reconciliation; test every crash boundary.
- Render only typed schemas from exact approved source/template versions. Never provide a general HTML/file conversion endpoint.
- Use outbox/inbox and stable generation/source/owner IDs. Replays must return the original logical outcome.
- Add architecture tests preventing report/cache statement sources, direct Ledger writes, cross-service EF access, object SDK leakage and unsafe template engines in Domain/Application.
- Add PostgreSQL/Testcontainers concurrency tests for generation idempotency, current versions, template windows, holds and disposition races.
- Use the PDF skill/render pipeline when implementing templates: render every approved golden PDF to images and inspect layout after meaningful changes.
- Use synthetic documents/financial facts. Do not copy the supplied requirements PDFs into product test data or object storage.
- Stop when work requires an unapproved file type/size, source/finality semantic, template/format, crypto/watermark, link policy, retention/legal-hold rule, storage topology or later reporting scope.
- Completion report must list requirements/stories, enabled document types/formats/locales, ownership boundaries, permissions, APIs/events, migrations, storage/scanner/renderer versions, templates/manifests, test commands/results, visual QA, security/privacy/retention, reconciliation/restore evidence, risks and rollout/rollback notes.

## 28. Traceability summary

| Source | Primary implementation evidence |
| --- | --- |
| RPT-001 | Approved statement, receipt, confirmation and record generation from verified owner facts |
| RPT-002 | Versioned templates, watermark/integrity, secure access, permissions and retention |
| RPT-004 / BR-015 | Artifacts use authoritative sources and never become Ledger truth |
| LED-013 / US-023 | Stable posted-history snapshot, reconciled totals, deterministic render and secure download |
| US-064 | Quarantined/scanned/classified/versioned object storage and scoped retrieval |
| DAT-001/003/005/006/008 | Owner boundaries, immutable financial refs/dates, classification/encryption and correction history |
| SEC-008/009/010/011/013/015 | Encryption, least privilege, step-up, injection/replay/IDOR controls, masking and lawful disposition |
| REG-003 | Legal hold blocks alteration/deletion and preserves immutable evidence |
| NFR-DR/OBS/SCAL/I18N/MNT | Idempotent workers, traceability without sensitive data, localisation, runbooks and tested DB/object restore |

RPT-003, RPT-005 through RPT-008, US-065 and US-066 remain later reporting/data-governance scope. Their foundation must not be represented as completed by this epic.
