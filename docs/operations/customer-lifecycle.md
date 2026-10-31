# Customer lifecycle and classification

This note records what slices 9 and 10 of `P1-E03 Customer and Onboarding` implement, the rules the code
enforces and what deliberately stays unavailable. Decision record:
[P1-E03-D09/D10](../implementation/phase-1-banking-foundation/decisions/P1-E03-D09-D10-classification-activation-and-status-transitions.md).

## What exists now

| Area | Implementation |
| --- | --- |
| Domain | `Customer`, `CustomerClassification`, `CustomerStatusChange` in `CoreBanking.CustomerOnboarding.Domain.Customers` |
| Application | `ICustomerService` with register, propose/approve classification, activate, change status and read |
| Infrastructure | `CustomerService` over `customer.customers`, `customer.customer_classifications`, `customer.customer_status_history` and `customer.customer_number_sequences` |
| Events | `CustomerCreated.v1` and `CustomerStatusChanged.v1` on `corebanking.customer-onboarding.customer.v1` through the outbox |
| Migration | `AddCustomersAndClassifications`; the `customer` schema grants already exist |

## Rules the code enforces

- **One relationship per party per institution**, created only from a submitted application
  (`customer_application_not_submitted`, `customer_exists`).
- **Numbers come from the institution sequence.** Allocation is a single atomic
  `INSERT … ON CONFLICT DO UPDATE … RETURNING`. Numbers are monotonic and never reused; a failed create can
  leave a gap, which the contract allows because uniqueness and non-reuse are what matter. Display
  formatting is Operations configuration, not stored here.
- **Classification is versioned.** Tier, segment, risk class and the EDD flag are configuration codes; a
  proposed version is not usable. Approving a version cites the exact Workflow decision and supersedes the
  earlier approved version (`customer_classification_required`,
  `customer_classification_transition_invalid`).
- **Activation is double-gated.** It needs a **completed KYC case** for the submitted application
  (`kyc_case_incomplete`) **and** an approved classification version. Neither is assumed.
- **The epic's transition table is enforced**: `pending → active | rejected`;
  `active → restricted | dormant | deceased | closed`; `restricted → active | closed`;
  `dormant → active | closed`. Anything else is `customer_transition_forbidden`.
- **Sensitive transitions need an approval reference**: `restricted`, `deceased` and `closed` are refused
  with `customer_approval_required` without one.
- **Terminal states stay terminal.** `rejected`, `deceased` and `closed` have no outgoing transition and
  cannot receive a new classification (`customer_status_terminal`). Reopening needs an approved process that
  does not exist yet.
- **History is append-only**, one sequenced row per transition with from/to, reason, actor and approval
  reference.
- **No account is ever touched.** Status changes publish `CustomerStatusChanged.v1`; Core Banking and Ledger
  decides what an account does. Events carry statuses and references only, never reason text or personal
  data.

## Deliberately unavailable

- No Workflow consumer: `ApprovalDecided.v1` is not yet applied, so an approval reference is supplied through
  the command surface and must not be trusted as a substitute for the consumer.
- No Risk reference storage: `CustomerRiskAssessed.v1` and a risk assessment reference land with the Risk
  integration slice.
- No customer corrections (`customer.profile.correct`), no merge, no preferences and no Customer 360.
- No HTTP surface yet: contracts exist and the API waits for the service authorisation port.
- No tier, segment, risk-class or EDD trigger content; those are Compliance, Risk and Product data.

## Verification

```powershell
dotnet build CoreBanking.sln --no-restore
dotnet test src/Services/CustomerOnboarding/tests/CoreBanking.CustomerOnboarding.UnitTests --filter "Category!=Database"
powershell -File scripts/db-verify.ps1
```

`CustomerDatabaseTests` run under `Category=Database` as the application principal and include the outbox
assertions, so a missing grant or a malformed envelope fails there rather than in review.

## Preferences and consent

Slice 12 adds versioned customer preferences with append-only consent evidence. Decision:
[P1-E03-D12](../implementation/phase-1-banking-foundation/decisions/P1-E03-D12-consent-purposes-lawful-bases-and-preferences.md).

- **One current version per customer, purpose and channel**, enforced by a partial unique index. A change
  closes the current version (`effective_to`) and appends the next version number.
- **Repeating the current decision is idempotent**: no new version and no new evidence row.
- **A grant needs a lawful basis** (`consent_lawful_basis_required`); a denial does not, but withdrawing an
  existing grant requires a recorded reason (`preference_reason_required`).
- **Purposes, channels, lawful bases and sources are approved configuration codes**
  (`preference_code_invalid`). Nothing is defaulted.
- **Consent evidence is append-only** in `privacy.consents`: purpose, channel, version, state, lawful basis,
  source, actor, timestamp and `withdrawn_at`. Nothing here deletes data; withdrawal stops future processing
  for that purpose only.
- The preference surface is `ICustomerPreferencesService` (`SetAsync`, `GetAsync`). Relationship-manager
  assignments and data-subject request handling are not implemented yet.

`CustomerPreferencesDatabaseTests` (Category=Database) cover versioning, idempotency, the lawful basis and
withdrawal rules, the single-current-version constraint and tenant isolation.

## Customer merges

Slice 11 adds controlled merges. Decision:
[P1-E03-D11](../implementation/phase-1-banking-foundation/decisions/P1-E03-D11-customer-merge-eligibility-and-reconciliation.md).

- **Only mergeable states take part**: `active`, `restricted` or `dormant` (`merge_customer_state_invalid`
  otherwise). A pending onboarding must complete first; a deceased, closed or rejected customer is never
  merged by this path.
- **One active merge per source customer**, enforced by a partial unique index (`merge_source_conflict`).
- **The plan is frozen when approved**: survivor, source, reason and the ordered required-consumer list are
  hashed into `plan_hash`; a change needs a new request. Approve requires the Workflow decision reference and
  moves `proposed → analysed → pending_approval → approved`.
- **Applying closes the source** through the normal `→ closed` transition, using the merge approval as its
  approval reference and `merged into <survivor>: <reason>` as the reason. History, classifications,
  preferences and evidence are retained; nothing is deleted.
- **A permanent alias** (`customer.customer_aliases`) redirects the source identifier to the survivor and is
  never reused.
- **`CustomersMerged.v1`** carries merge id, survivor, source and the plan hash, so a consumer can prove which
  plan it applied.
- **Completion waits for consumers.** Applying ends in `reconciliation_required` listing outstanding
  consumers; the merge becomes `completed` only when every required consumer acknowledged. Acknowledgements
  are idempotent per consumer.
- The consumer list is approved configuration data supplied at proposal time; no consumer is hard-coded.
- No account is touched here; Core Banking and Ledger consumes the event and applies its own rules.

### Merge approval through Workflow

`SubmitCustomerMergeForApprovalAsync` freezes the plan and submits it under the profile
`customer.merge.approve.v1`: the **merge request identifier is the approval request identifier** and the
**frozen plan hash is the subject hash**, so no extra state is needed and a repeat submission publishes nothing.
`CustomerMergeApprovalDecidedConsumer` applies a decision only when the envelope, profile, definition hash,
merge request, plan hash and entitlement window all match:

- `approve` moves the request to `approved` with the decision identifier as its approval reference, which is what
  `ApplyAsync` already requires;
- `reject` ends the request as `rejected`; the reason is recorded as audit evidence rather than on the plan;
- a repeat is a no-op through the inbox, and a decision citing another plan hash is deferred, never applied.

Both approval decisions (classification and merge) arrive on the same topic, so the router dispatches them by the
payload's subject type. `CustomerMergeApprovalDatabaseTests` (Category=Database) cover the frozen-hash
submission, the idempotent resubmission, the approval, the replay, the rejection and the mismatched hash.

`CustomerMergeDatabaseTests` (Category=Database) cover the source closure and alias, the event payload,
consumer-gated completion, unapproved application, the single-active-merge constraint, mergeable-state rules
and tenant isolation.

## Customer 360

Slice 13 adds the read-only composition. Decision:
[P1-E03-D13](../implementation/phase-1-banking-foundation/decisions/P1-E03-D13-customer-360-sections-and-freshness.md).

- **Per-section metadata**: every section reports its owning `source`, the `requiredPermission` the caller must
  hold, an `as_of` read time and a status of `available` or `unavailable`.
- **Three truthful sections**: `profile` (number, status, branch, approved classification codes, masked
  contacts and country), `kyc` (case status and requirement counts, never evidence content) and `merges` (open
  merge count and the alias redirect after a merge).
- **Five unavailable placeholders**: `accounts`, `cases`, `risk`, `documents`, `channels` return
  `source_not_integrated` with no values, until their owners publish a read contract.
- **Requests are bounded**: an empty request returns nothing, a duplicate is collapsed, more than eight
  sections or an unknown section is refused with `customer360_section_unknown`.
- **No mutation**: the composition performs reads only and never writes back to a source service or this
  service's tables.
- Permission enforcement per section is the API layer's job (not wired yet); the composition publishes the
  permission each section needs so it cannot be forgotten.

`Customer360DatabaseTests` (Category=Database) cover masked profile fields, unavailable placeholders, KYC
counts, the merge alias redirect, bounded/unknown requests and tenant isolation.

## Relationship manager assignments

Slice 12 also adds the effective-dated staff relationship. `RelationshipManagerService` keeps one **current**
assignment per customer (`ux_relationship_manager_assignments_current` covers the open period), closes the
previous one at the moment of reassignment and appends the next, so history is never rewritten. Each
assignment records the manager's stable identity reference, an optional branch scope, the assigning actor and a
reason, publishes `RelationshipManagerAssigned.v1` and writes `customer.relationship_manager.assigned` audit
evidence. Repeating the current manager is idempotent.

The service validates the shape of the reference (a stable code, no free text), not who may be assigned where:
entitlement checks belong to the API authorisation port, which is not wired yet. No staff personal data is
stored here.

`RelationshipManagerTests` (unit) and `RelationshipManagerDatabaseTests` (Category=Database) cover the closure
rule, the idempotent repeat, the single-current constraint, the event and audit evidence, unknown customers and
tenant isolation.

## Classification approval loop

Slice 9 also closes the maker-checker loop through Operations and Workflow. `SubmitClassificationForApprovalAsync`
freezes a SHA-256 hash of the exact version (`customerId`, version, tier, segment, risk class, EDD flag) and
publishes `approval_requested` on `corebanking.customer-onboarding.approval.v1` under the profile
`customer.classification.approve.v1` (subject `customer.classification.version`, permission
`customer.classification.approve`). Resubmitting the same content is idempotent: the same request identifier is
returned and no second event is published.

`CustomerClassificationApprovalDecidedConsumer` consumes
`corebanking.operations-workflow.approval.v1` and applies a decision **only** when all of these match:

- the envelope's institution, business reference and correlation with the payload;
- the approved workflow profile, its version and its definition hash;
- the approval request the version was submitted under;
- the content hash this service froze at submission;
- the decision-time entitlement window.

An `approve` records the decision identifier as the classification's Workflow decision reference and writes
`customer.classification.approved` audit evidence; a `reject` leaves the version `proposed` but writes
`customer.classification.rejected` with the reason. A repeat delivery is a no-op through the inbox, and a
decision that cites a different request or hash is deferred to the dead-letter path, never applied.

**What is still Operations and Workflow's side:** their approval ingestion currently recognises only the
savings-eligibility profile, so this service's request reaches Workflow only once that service registers the
customer classification profile. The consumer, the profile and the hash rules on this side are complete and
tested; a decision generated by an authorised Workflow run applies without further change here.

`CustomerClassificationApprovalDatabaseTests` (Category=Database) cover the frozen hash and published request,
the idempotent resubmission, an approval, a replay, a rejection, a mismatched hash and an unsubmitted request.

## Protected-data corrections

`CustomerCorrectionService` implements the epic's correction journey (CUS-UC-06, invariant 15):

- **Append-only record**: `customer.customer_corrections` stores one row per correction with the affected
  field code, the **superseded** and the new protected-value references, the reason, an optional evidence
  reference and the recorded actor. Values are never copied into this table, the event or the audit row.
- **Verified attributes**: correcting one requires an approval reference (`customer_correction_approval_required`)
  and always invalidates the previous verification.
- **Invalidation**: a correction that invalidates evidence names the KYC requirement, which is reopened to
  `evidence_pending` with a requirement-history row and a `kyc.requirement.reopened` audit entry. The accepted
  verification row itself is never edited or deleted: it stays as immutable evidence and is simply no longer
  current.
- **Minimal event**: `CustomerProfileChanged.v1` carries the correction id, the field code and the affected
  requirement code, never a value or a reference.
- **Refusals**: a reason shorter than three characters, a correction that does not change the cited value, an
  unstable field code, and a customer or requirement outside the tenant are all refused with stable codes.

`CustomerCorrectionTests` (unit) and `CustomerCorrectionDatabaseTests` (Category=Database) cover the rules, the
prior-reference retention, the invalidation, the field-code-only event, no-PII assertions and tenant scoping.

## Audit evidence and reconciliation

Slice 14 adds immutable audit evidence and the integrity reconciliation pass. Decision:
[P1-E03-D14](../implementation/phase-1-banking-foundation/decisions/P1-E03-D14-retention-audit-and-reconciliation-posture.md).

- **One audit row per audited action** in `audit.customer_audit_events`: status changes, approved
  classifications, applied merges, preference versions, KYC requirement waivers/resolutions and requirement
  pack publication. `AuditRecorder` writes the row and the `customer_audit_event` outbox event in the same
  transaction, so an action cannot commit without its evidence.
- **Detail is codes and references only** - statuses, versions, approval references and correlation ids.
  Names, contacts, identifiers and evidence content never enter the audit row or the event payload.
- **Append-only at the database**: `customeronboarding_app` holds `SELECT`/`INSERT` on `audit` only. Any
  `UPDATE`, `DELETE` or `TRUNCATE` fails, so the application cannot rewrite history; the privilege suite
  asserts this for every deployment.
- **SIEM transport**: the `customer_audit_event` event is published in the `audit` family on
  `corebanking.customer-onboarding.audit.v1`. The shipping pipeline (collector, sink, retention in the SIEM)
  is Integration and Operations configuration and is not in this repository.
- **Retention posture**: the service deletes nothing. Retention periods, legal hold and anonymisation windows
  remain Legal/Compliance data (open decision D14 content); any pruning is a separately approved operator
  procedure, never an application code path.

### Reconciliation runbook

`CustomerReconciliationWorker` runs one read-only pass per `CustomerReconciliation:IntervalSeconds`
(default 3600) and publishes `customer.reconciliation.passes{clean}` and
`customer.reconciliation.findings{code}` on the `CoreBanking.CustomerOnboarding` meter.

| Code | Meaning | Operator action |
| --- | --- | --- |
| `merge_awaiting_consumers` | A merge is applied but a required consumer has not acknowledged | Chase the consumer's acknowledgement; do not re-apply the merge |
| `merge_applied_without_alias` | Applied merge with no alias row | Reapply the merge through the service once the cause is understood |
| `transition_without_approval` | `restricted`, `deceased` or `closed` recorded without an approval reference | Escalate to Compliance; the history row is never edited |
| `kyc_requirement_without_verification` | Requirement in a verification-dependent state with no live verification | Reopen the case and re-run the check; never mark it passed manually |
| `alias_target_missing` | Alias points at a customer that does not exist | Escalate as data corruption; restore from backup evidence rather than deleting the alias |

Alert on any non-zero `customer.reconciliation.findings` with a code not already acknowledged in the current
shift, and on a `clean=false` pass count that persists after review. The pass never repairs state: it reports,
and an operator acts.

`CustomerAuditDatabaseTests` (Category=Database) cover the audit row plus SIEM event for an activation, one
row per audited action, a clean reconciliation pass, each violation code being reported without repair, and
tenant isolation.

## Provider verification and evidence

Slice 8 binds one adapter. `IKycProviderPort` is the boundary Integration Hub will implement; today
`SyntheticKycProviderAdapter` is the only binding and it is deterministic and offline. `KycVerificationRunner`
applies the approved rules:

- A submission **refused before acceptance** (`Unavailable`) closes the attempt as
  `cancelled_before_submission` and the retry is a **new attempt** of the same verification, bounded by
  `MaxSubmissionAttempts`.
- An **accepted but late** submission (`TimedOut`) becomes `Uncertain` and is resolved only by status enquiry.
  It is never resubmitted.
- An enquiry that answers `Unknown` (the provider has no record) leaves the verification `Uncertain` and
  `RequiresStatusEnquiry: true` for an operator. Nothing is guessed and nothing is resubmitted.
- An accepted outcome records the canonical outcome, the provider mapping version, the provider route, the
  **evidence reference** and its SHA-256 hash. Evidence references are the only raw-evidence artefact stored;
  payloads, documents and biometrics stay with the provider or Integration Hub.

Every submission, retry, uncertainty and enquiry writes audit evidence
(`kyc.attempt.submitted`, `kyc.attempt.retried`, `kyc.attempt.uncertain`, `kyc.attempt.enquired`).

`KycProviderRunDatabaseTests` (Category=Database) cover the retry, the enquiry-resolved timeout, the recorded
route/mapping/evidence, a failed check, the unknown submission that stays uncertain and a waived requirement that
needs no run. `VerificationEvidenceTests` (unit) cover the evidence requirement and hash validation.