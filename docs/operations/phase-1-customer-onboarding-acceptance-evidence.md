# Customer and onboarding acceptance evidence

Result of the Phase 1 Customer and Onboarding epic evidence pass (slice 15). Every row is produced by a test
that runs in `scripts/db-verify.ps1`, so the evidence is re-run on each verification instead of being a
dated screenshot. Decision:
[P1-E03-D15](../implementation/phase-1-banking-foundation/decisions/P1-E03-D15-acceptance-evidence-and-measurement-posture.md).

## What is evidenced

| Requirement | Evidence (test) | Suite |
| --- | --- | --- |
| Capture, requirement gating and submission | `OnboardingCaptureDatabaseTests`, `RequirementSnapshotDatabaseTests` | Database |
| Explainable duplicate candidates | `DuplicateDetectionDatabaseTests` | Database |
| KYC case, requirements and canonical outcomes | `KycCaseTests`, `KycCaseDatabaseTests` | Unit, Database |
| Provider retry, uncertainty, status enquiry and evidence | `KycProviderRunDatabaseTests`, `VerificationEvidenceTests` | Database, Unit |
| Classification, activation and status transitions | `CustomerTests`, `CustomerDatabaseTests` | Unit, Database |
| Classification approval loop (`approval_requested` / `approval_decided`) | `CustomerClassificationApprovalDatabaseTests` | Database |
| Protected-data corrections (`CUS-019`) | `CustomerCorrectionTests`, `CustomerCorrectionDatabaseTests` | Unit, Database |
| Merges, aliases and consumer convergence | `CustomerMergeDatabaseTests` | Database |
| Merge approval loop (`approval_requested` / `approval_decided`, plan hash) | `CustomerMergeApprovalDatabaseTests` | Database |
| Preferences and consent evidence | `ServicePreferenceTests`, `CustomerPreferencesDatabaseTests` | Unit, Database |
| Relationship-manager assignments | `RelationshipManagerTests`, `RelationshipManagerDatabaseTests` | Unit, Database |
| Customer 360 sections, masking and freshness | `Customer360DatabaseTests` | Database |
| Immutable audit, SIEM events and reconciliation | `CustomerAuditDatabaseTests`, privilege suite | Database, Security |
| End-to-end individual onboarding (`CUS-034`) | `An_individual_is_onboarded_verified_classified_and_activated` | Integration |
| Replay creates no duplicate (`CUS-033`) | `A_replayed_submission_creates_no_duplicate_customer` | Integration |
| Cross-tenant refusal and data minimisation (`CUS-021`, `CUS-028`) | `Another_institution_reaches_nothing_and_no_protected_value_reaches_an_event` | Integration |

Run them with:

```powershell
powershell -File scripts/db-verify.ps1
```

## Provider posture

One adapter is bound: `SyntheticKycProviderAdapter`. It is deterministic, uses no network, and its behaviour is
a script (refused submissions, late submissions, pending enquiries, canonical outcome). It defines no KYC rule.
A real provider arrives with the provider agreement, approved check codes and the mapping tables; the
`IKycProviderPort` boundary and the retry/enquiry rules do not change when it does.

## Gaps, recorded as gaps

| Gap | Owner | Note |
| --- | --- | --- |
| Real provider route and certification | Integration | Requires the provider agreement and approved mapping tables |
| Workflow ingestion of the customer classification profile | Operations and Workflow | This service publishes the request and applies the decision; their consumer currently recognises only the savings-eligibility profile |
| Core, Workflow, Risk, Document and Channel Customer 360 read contracts | Domain owners | The sections report `source_not_integrated` until published |
| Per-section permission enforcement at the API layer | Platform | The composition publishes the permission each section needs |
| Field policy that decides which corrections need an approval | Product and Compliance | The service requires one for a verified attribute; the full field catalogue is policy data |
| Capacity, SLO, RTO/RPO targets and a load profile | Product and Platform | Not invented here; the local suites measure only their own runtime |
| Screenshot-based dashboards and alert routing | Operations | The reconciliation metrics and runbook exist; routing is deployment configuration |
| Retention periods, legal hold and anonymisation windows | Legal and Compliance | Posture is approved; the numbers are not |

## Known limitations of this evidence

- The suites run against a local PostgreSQL with synthetic data only; they are not a load test.
- Categories `Database` and `Integration` share one test database and use unique institution identifiers for
  isolation, so a re-run never depends on a cleaned database.
- Reconciliation assertions are scoped to the test's own institution because the job is deliberately
  cross-tenant.
