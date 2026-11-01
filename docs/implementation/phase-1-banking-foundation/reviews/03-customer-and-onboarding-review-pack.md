---
artifact: Slice review pack
epic: P1-E03 Customer and Onboarding
snapshot: 2026-10-31
reviewer: project owner (recorded in the implementation session)
approved: 2026-10-31
related:
  - 03-customer-and-onboarding.md
  - slice-ledger.md
  - ../operations/phase-1-customer-onboarding-acceptance-evidence.md
---

# Review pack: Customer and Onboarding (P1-E03)

One row per slice, with what is claimed, where the evidence is, and why the slice is not `approved`. A slice
becomes `approved` only when a named human reviewer accepts it; this pack exists so that decision is quick and
auditable. Nothing here is a claim of production readiness.

## 1. How to verify the whole epic

```powershell
# Build, analyzers and architecture rules
dotnet build CoreBanking.sln --no-restore

# Migrations for every service, database and integration suites, and the privilege suite
powershell -File scripts/db-verify.ps1
```

Expected at this snapshot:

| Check | Result |
| --- | --- |
| Solution build | 0 warnings, 0 errors |
| Architecture tests | 15/15 |
| Customer and Onboarding tests | 248/248 (102 of them `Database` or `Integration`) |
| Identity and Access database tests | 76/76 |
| Audit and Reconciliation database tests | 2/2 |
| Application-principal privilege suite | 54/54 |
| Migrations | apply cleanly from empty and from the previous snapshot |

## 2. Approval sheet

Tick one box per slice. A slice may be approved **with named gaps** (the gaps stay open and traceable) or
rejected, in which case the gap list becomes the change request.

| Slice | Claimed status | Decision |
| --- | --- | --- |
| 1 Approve decisions | implemented | **approved** 2026-10-31 |
| 2 Contracts and architecture tests | in progress | **approved with gaps** 2026-10-31 |
| 3 Institution provisioning | implemented | **approved** 2026-10-31 |
| 4 Individual capture | implemented | **approved** 2026-10-31 |
| 5 Requirement packs and submission gates | in progress | **approved with gaps** 2026-10-31 |
| 6 Duplicate detection | in progress | **approved with gaps** 2026-10-31 |
| 7 KYC cases and verifications | in progress | **approved with gaps** 2026-10-31 |
| 8 Synthetic KYC adapter and evidence | in progress | **approved with gaps** 2026-10-31 |
| 9 Classification, activation and approval loop | in progress | **approved with gaps** 2026-10-31 |
| 10 Lifecycle and corrections | in progress | **approved with gaps** 2026-10-31 |
| 11 Merges, aliases and merge approval | in progress | **approved with gaps** 2026-10-31 |
| 12 Preferences, consent and relationship managers | in progress | **approved with gaps** 2026-10-31 |
| 13 Customer 360 | in progress | **approved with gaps** 2026-10-31 |
| 14 Immutable audit, SIEM and reconciliation | in progress | **approved with gaps** 2026-10-31 |
| 15 Acceptance evidence | in progress | **approved with gaps** 2026-10-31 |

Reviewer: project owner, recorded in the implementation session  Date: 2026-10-31

**Decision:** all fifteen slices approved **with their named gaps**. The gaps in section 3 and the
external confirmations in section 4 remain open items; they are not waived by this approval, and they
still gate production use.

## 3. Slice detail

### Slice 1 - Approve the epic's decisions

- **Claim:** decisions D01-D15 are recorded, approved and linked to briefs in `decisions/`.
- **Evidence:** [section 21 of the epic](../03-customer-and-onboarding.md), `decision-register.csv`,
  `decisions/P1-E03-D*.md`.
- **Gaps:** several decisions carry a named external confirmation that is still outstanding (see section 4).

### Slice 2 - Versioned contracts and architecture tests

- **Claim:** party, customer, application, KYC and event contracts exist, and the architecture tests enforce the
  layering (`Domain` does not depend on Application, `Application` does not reference Contracts, Infrastructure
  owns EF).
- **Evidence:** `CoreBanking.CustomerOnboarding.Contracts/*`, `tests/CoreBanking.ArchitectureTests` 15/15,
  `InstitutionProvisioningTests` (6).
- **Gaps:** HTTP request/response contracts and OpenAPI are not authored; there is no API surface yet.

### Slice 3 - Institution and branch provisioning

- **Claim:** `customer_onboarding_db` participates in institution provisioning and boundary acknowledgement.
- **Evidence:** `Infrastructure/Institutions/InstitutionProvisioningConsumer.cs`, `InstitutionProvisioningTests`,
  ledger cross-reference to P1-E01 slice 6.
- **Gaps:** none specific to this epic; the claim is cross-epic.

### Slice 4 - Individual capture

- **Claim:** individual party/application capture with explicit field mapping, field-level protection through the
  protector port, and immutable revisions.
- **Evidence:** `Domain/Parties/*`, `Infrastructure/Onboarding/*`, `OnboardingDomainTests` (23),
  `OnboardingCaptureDatabaseTests` (11), `AesGcmProtectedValueProtectorTests` (8),
  [capture runbook](../../../operations/customer-onboarding-capture.md).
- **Gaps:** request contracts, authorisation wiring and the API surface remain unregistered by design.

### Slice 5 - Requirement packs, snapshots and submission gates

- **Claim:** institution-owned versioned requirement packs with a content hash, one immutable snapshot per
  submitted revision, document metadata behind a fail-closed quarantine port, and submission refused when no
  pack is effective or a registered document failed its scan.
- **Evidence:** `Domain/Requirements/*`, `Infrastructure/Requirements/*`, `RequirementPackTests` (6),
  `RequirementSnapshotDatabaseTests` (7), migrations `20261028151013`, `20261028151200`.
- **Gaps:** pack content is Product/Compliance data and is not authored in code; the quarantine port answers
  `pending` until the document component is wired.

### Slice 6 - Duplicate detection

- **Claim:** exact contact, name + birth-date and address-line factors with derived bands, bounded candidate
  sets, reviewer decisions that refuse stale evidence and stale versions, and deterministic lookup tokens.
- **Evidence:** `Domain/Duplicates/*`, `DuplicateDetectionTests` (14), `DuplicateDetectionDatabaseTests` (10),
  migrations `20261028141338`, `20261028141500`, `20261028142241`.
- **Gaps:** probabilistic scoring, verified-identifier uniqueness and the review HTTP surface remain; the named
  Compliance confirmation on the uniqueness policy is outstanding.

### Slice 7 - KYC cases and canonical verifications

- **Claim:** case opened from the submitted revision's snapshot, requirement lifecycle, canonical
  request/attempt/result contracts, mapping-version enforcement, reviewer resolution, waivers and append-only
  requirement and verification history.
- **Evidence:** `Domain/Kyc/*`, `Infrastructure/Kyc/KycCaseService.cs`, `KycCaseTests` (13),
  `KycCaseDatabaseTests` (10), migration `20261028155300`.
- **Gaps:** check codes, provider routing and mapping tables are Compliance/Integration data; no provider is
  bound at this slice (slice 8 adds the synthetic one).

### Slice 8 - Synthetic provider adapter, uncertainty and evidence

- **Claim:** one canonical provider port and one deterministic synthetic adapter; a refusal before acceptance is
  retried as a new attempt, an accepted-but-late submission becomes `Uncertain` and is resolved only by bounded
  status enquiry, an unknown submission stays uncertain, and an accepted outcome records provider route, mapping
  version, evidence reference and SHA-256 hash.
- **Evidence:** `Application/Kyc/ProviderContracts.cs`, `Infrastructure/Integration/SyntheticKycProviderAdapter.cs`,
  `Infrastructure/Kyc/KycVerificationRunner.cs`, `KycProviderRunDatabaseTests` (6),
  `VerificationEvidenceTests` (6), migration `20261030165849`, decision
  [D07/D08](../decisions/P1-E03-D07-D08-provider-retry-evidence-and-status-enquiry.md).
- **Gaps:** no real provider route, check code or mapping table; provider-side evidence retention is contractual.

### Slice 9 - Classification, activation and the approval loop

- **Claim:** versioned tier/segment/risk/EDD classification with Workflow decision references and supersession;
  activation gated by a completed KYC case plus an approved classification; institution-sequence numbering;
  maker-checker through a frozen content hash with an `approval_decided` consumer.
- **Evidence:** `Domain/Customers/Customer.cs`, `CustomerService`,
  `Approvals/CustomerClassificationApprovalDecidedConsumer.cs`, `CustomerTests` (9), `CustomerDatabaseTests` (7),
  `CustomerClassificationApprovalDatabaseTests` (7), migrations `20261028161841`, `20261030180615`, decision
  [D09/D10](../decisions/P1-E03-D09-D10-classification-activation-and-status-transitions.md).
- **Gaps:** Risk assessment references are Risk data; Workflow must register the customer classification profile
  before a real decision can arrive.

### Slice 10 - Lifecycle and protected-data corrections

- **Claim:** the epic's transition table with reasons and approval references for restricted/deceased/closed,
  terminal states never reversed, append-only status history, `CustomerCreated.v1`/`CustomerStatusChanged.v1`,
  and corrections that keep the superseded reference, require an approval for a verified attribute and reopen the
  affected requirement without editing accepted evidence.
- **Evidence:** `Domain/Customers/CustomerCorrection.cs`, `CustomerCorrectionService.cs`, `CustomerTests` (9),
  `CustomerDatabaseTests` (7), `CustomerCorrectionTests` (8), `CustomerCorrectionDatabaseTests` (5),
  migration `20261030181515`.
- **Gaps:** the field policy that decides which corrections need approval is policy data; no account state is
  touched by design.

### Slice 11 - Merges, aliases and merge approval

- **Claim:** frozen merge plans with a plan hash, Workflow approval reference, one active merge per source, source
  closure through the normal transition with retained history, permanent aliases, `CustomersMerged.v1`,
  required-consumer acknowledgements, `reconciliation_required` until convergence, and an `approval_decided`
  consumer that applies a decision only to the matching plan hash.
- **Evidence:** `Domain/Customers/CustomerMergeRequest.cs`, `CustomerMergeService`,
  `Approvals/CustomerMergeApprovalDecidedConsumer.cs`, `CustomerMergeTests` (8), `CustomerMergeDatabaseTests` (6),
  `CustomerMergeApprovalDatabaseTests` (6), migration `20261028224026`,
  decision [D11](../decisions/P1-E03-D11-customer-merge-eligibility-and-reconciliation.md).
- **Gaps:** a merge rejection reason lives in audit evidence, not on the plan; Workflow must register the merge
  approval profile.

### Slice 12 - Preferences, consent and relationship managers

- **Claim:** versioned per-purpose/per-channel preferences with effective periods, idempotent repeats, a lawful
  basis for grants, a recorded withdrawal reason, append-only consent evidence, a database guarantee of one
  current version, and effective-dated relationship-manager assignments with one current per customer.
- **Evidence:** `Domain/Privacy/*`, `Domain/Customers/RelationshipManagerAssignment.cs`,
  `CustomerPreferencesService`, `RelationshipManagerService`, `ServicePreferenceTests` (7),
  `CustomerPreferencesDatabaseTests` (7), `RelationshipManagerTests` (9), `RelationshipManagerDatabaseTests` (6),
  migrations `20261028184107`, `20261028192500`, `20261030163320`,
  decision [D12](../decisions/P1-E03-D12-consent-purposes-lawful-bases-and-preferences.md).
- **Gaps:** the purpose/channel/lawful-basis catalogue and data-subject request handling remain; assignment
  entitlements belong to the API authorisation port.

### Slice 13 - Customer 360

- **Claim:** read-only composition with per-section status, source, required permission and freshness; three
  owned sections answered locally with masked values only; five explicit `source_not_integrated` placeholders;
  bounded requests and unknown-section refusal.
- **Evidence:** `Customer360Service`, `Customer360DatabaseTests` (6),
  decision [D13](../decisions/P1-E03-D13-customer-360-sections-and-freshness.md).
- **Gaps:** the five remote read contracts and API-layer per-section permission enforcement.

### Slice 14 - Immutable audit, SIEM events and reconciliation

- **Claim:** every audited action writes an append-only `audit.customer_audit_events` row and a
  `customer_audit_event` SIEM event in the same transaction; the runtime principal holds `SELECT`/`INSERT` on
  `audit` only; a scheduled pass reports five deterministic integrity codes without repairing state and publishes
  metrics; an operator runbook exists.
- **Evidence:** `Audit/AuditRecorder.cs`, `Audit/CustomerReconciliationService.cs`,
  `Workers/CustomerReconciliationWorker.cs`, `CustomerAuditDatabaseTests` (5), privilege suite 54/54, migrations
  `20261029215024`, `20261029224500`,
  decision [D14](../decisions/P1-E03-D14-retention-audit-and-reconciliation-posture.md),
  [runbook](../../../operations/customer-lifecycle.md#audit-evidence-and-reconciliation).
- **Gaps:** retention periods, legal hold and anonymisation windows; SIEM shipping, dashboards and alert routing
  are Integration/Operations configuration.

### Slice 15 - Acceptance evidence

- **Claim:** executable acceptance evidence runs inside the verification pass: end-to-end onboarding through
  capture, gating, synthetic verification with evidence, classification approval, activation and a masked
  Customer 360 view; a replay that creates no duplicate; a cross-tenant refusal; and no protected value in any
  outbox envelope or audit detail.
- **Evidence:** `CustomerOnboardingEndToEndTests` (3), `Category=Integration` in `scripts/db-verify.ps1`,
  [acceptance evidence](../../../operations/phase-1-customer-onboarding-acceptance-evidence.md),
  decision [D15](../decisions/P1-E03-D15-acceptance-evidence-and-measurement-posture.md).
- **Gaps:** capacity, SLO, RTO/RPO targets, a load profile, a real provider route and screenshot dashboards.

## 4. Named external confirmations still outstanding

These are recorded as gaps, not as approvals. They do not block review of the slices; they block production use.

| Confirmation | Owner |
| --- | --- |
| Provider agreement, approved check codes and mapping tables (D06, D07, D08) | Integration and Compliance |
| Verified-identifier uniqueness policy (D05) | Compliance |
| Workflow registration of the customer classification and merge approval profiles | Operations and Workflow |
| Retention periods, legal hold and anonymisation windows (D14) | Legal and Compliance |
| Capacity, SLO, RTO/RPO targets and the load profile (D15) | Product and Platform |
| SIEM shipping, dashboards and alert routing | Operations |
| Customer 360 remote read contracts and API-layer section permissions (D13) | Domain owners and Platform |
| Privacy sign-off before any Customer 360 section is enabled | Privacy |

## 5. What happens after approval

1. The reviewer's decision is recorded in `slice-ledger.csv` / `slice-ledger.md` (the `approval` column) and in
   section 17 of the epic document.
2. Approved slices stay in the ledger with their gaps; the gaps above remain open items owned by the named
   parties.
3. Rejected slices return to `in progress` with the reviewer's change request as the note.
