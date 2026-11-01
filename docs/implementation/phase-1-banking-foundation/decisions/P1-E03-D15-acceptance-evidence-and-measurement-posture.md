---
decision: P1-E03-D15
title: Acceptance evidence, measurement posture and non-invented targets
epic: P1-E03 Customer and Onboarding
owners:
  - Product
  - Platform
required_by: Slice 15
status: Approved
approved_on: 2026-10-30
brief_date: 2026-10-30
related: [P1-E03-D07, P1-E03-D14]
decisions: [P1-E03-D15]
---

# Decision brief: acceptance evidence and measurement posture

## 1. Decision

| # | Question | Answer |
| --- | --- | --- |
| D15-A | What does slice 15 deliver? | Executable acceptance **evidence**: an end-to-end flow that captures, gates, verifies, classifies, activates and composes Customer 360; a replay that creates no duplicate; a cross-tenant refusal; and a data-minimisation check over every produced event and audit row |
| D15-B | Are capacity, SLO, RTO and RPO numbers decided here? | **No.** They stay Product and Platform data. This slice records what was measured (test counts, durations, the flows exercised) and the **shape** of the measurement, not a target that would look approved when it is not |
| D15-C | Which acceptance criteria can be evidenced without a live provider? | Every criterion except those that name a real provider contract, a real Workflow decision and a real Core Banking read. Those stay open with a named owner |
| D15-D | How is evidence kept current? | The end-to-end suite is part of `scripts/db-verify.ps1`, so the evidence is re-run on every verification pass instead of being a document that ages. The evidence note cites the test names it depends on |
| D15-E | What must not be claimed? | No claim of production capacity, latency, RTO/RPO, provider certification or privacy sign-off. Unknowns are listed as unknowns with the owner who must close them |

## 2. Evidence map

| Acceptance criterion | Evidence | Status |
| --- | --- | --- |
| Individual application requirements derive from approved snapshots | `RequirementSnapshotDatabaseTests`, submission gates | Evidenced |
| Duplicate detection is explainable and cannot auto-merge | `DuplicateDetectionDatabaseTests` | Evidenced |
| Verification providers are reached only through a canonical port | `KycVerificationRunner` + `SyntheticKycProviderAdapter`, `KycProviderRunDatabaseTests` | Evidenced for the synthetic adapter; real route open |
| Uncertainty, replay, conflict and raw evidence handled safely | `KycProviderRunDatabaseTests`, `VerificationEvidenceTests` | Evidenced; conflict path is manual review by design |
| Classification and activation need exact versions | `CustomerDatabaseTests`, `An_individual_is_onboarded_verified_classified_and_activated` | Evidenced; Workflow `ApprovalDecided` consumer still open |
| Lifecycle preserves history and never mutates accounts | `CustomerDatabaseTests`, `CustomerAuditDatabaseTests` | Evidenced |
| Merge retains aliases and tracks consumers | `CustomerMergeDatabaseTests` | Evidenced |
| Customer 360 enforces scope, masking and freshness metadata | `Customer360DatabaseTests`, end-to-end flow | Evidenced; per-section permission enforcement is the API layer's job |
| Sensitive data is excluded from events, audit and logs | `Another_institution_reaches_nothing_and_no_protected_value_reaches_an_event` | Evidenced for events and audit; HTTP logs follow the observability pipeline |
| APIs, migrations, dashboards, alerts and recovery runbooks reviewed | `CustomerReconciliationWorker` + runbook, migrations applied by `db-verify.ps1` | Partially evidenced: dashboards and alert routing are Operations configuration |

## 3. Constraints

- No test or fixture uses real identifiers, real documents or production data.
- Evidence is produced by tests that run in the standard verification pass, not by manual screenshots.
- Where a criterion needs a service outside this epic, the gap is recorded with the owning service rather than
  marked done.
- Performance evidence is limited to what the local suites measure; no load profile is invented.

## 4. Deferred for named approval

| Item | Owner | Why it is not decided here |
| --- | --- | --- |
| Capacity, SLO, incident and RTO/RPO targets | Product and Platform | Operational commitments |
| Load and performance test profile and data volumes | Platform | Requires the approved environment |
| Real provider certification and evidence retention terms | Integration and Compliance | Contractual |
| Screenshot-based dashboards and alert routing | Operations | Deployment-specific |

## 5. Sign-off

| Role | Name | Decision | Date |
| --- | --- | --- | --- |
| Project owner | Recorded in the implementation session | Approved executable acceptance evidence with no invented capacity, SLO, RTO or RPO commitments | 2026-10-30 |
| Platform | | Named confirmation required before performance or recovery targets are published | |
