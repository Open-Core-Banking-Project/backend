---
phase: "Phase 2 - Regulated Operations"
epic: "Regulatory and Scheduled Reporting"
owning_service: "Risk and Reporting"
modules: [Governed Read Models, Report Scheduling, Regulatory Returns, Secure Distribution]
status: Draft
requirements: [RPT-001..RPT-005, RPT-008, REP-001..REP-003, REG-001..REG-003, AUD-001..AUD-004]
stories: [US-047, US-065]
dependencies: [All enabled Phase 1/2 source modules, Phase 1 Documents, Epic 06 Risk, Epic 07 Reconciliation]
adrs: [Reporting read models and lineage, Regulatory country packs, Report rendering and distribution]
last_updated: 2026-10-16
---

# Epic 10: Regulatory and Scheduled Reporting

## 1. Objective

Prepare governed, traceable regulatory returns and schedule operational/management reports with controlled approval, versioning, PDF/Excel/CSV export and secure audited distribution.

## 2. Success measures

- Every material output field traces to an approved source contract, transformation and snapshot/version.
- Regulatory returns retain period, country pack, version, preparer, reviewer, status, adjustments and submission evidence.
- Scheduled reports apply institution/branch/product/period scope and deliver only to authorised recipients.
- Output formats reconcile to the same dataset/totals and never become balance authority.

## 3. Scope

Included: report definitions/versions, governed read models, lineage for delivered outputs, schedules, parameters, runs, artifacts, approval, CBN, NIBSS, NDIC and institution-specific return preparation when enabled by an approved country pack, export and distribution evidence. Excluded: Phase 5 enterprise glossary/lineage platform, invented regulator schemas, direct queries to private service databases, and submission without approved external interface.

## 4. Actors, roles and permissions

| Actor | Permission | Scope | Step-up | Approval |
| --- | --- | --- | --- | --- |
| Report manager | `report.definition.manage` | Institution/report family | Publish | Maker-checker |
| Manager/consumer | `report.run.view` | Institution/branch/product/classification | Sensitive export | None |
| Compliance preparer | `regulatory.return.prepare` | Country/return/period | Required | Maker |
| Compliance reviewer | `regulatory.return.approve` | Country/return/period | Required | Separate reviewer |
| Scheduler identity | `report.schedule.execute` | Approved schedule | Workload | Pre-approved schedule |

## 5. Domain model

Concepts: `ReportDefinition/Version`, `FieldMapping`, `SourceSnapshot`, `ReportSchedule`, `ReportRun`, `ReportArtifact`, `Distribution`, `RegulatoryReturn`, `ReturnAdjustment`, `SubmissionEvidence`.

States: report run `Queued -> Extracting -> Rendering -> Ready -> Distributed/Failed`; return `Draft -> Prepared -> ReviewPending -> Approved -> Submitted/Rejected/Amended`.

Invariants: definition/source snapshot is immutable per run; artifacts share one canonical dataset/totals; report data cannot post or change source state; adjustment preserves before/after/reason; preparer cannot be sole reviewer; legal hold overrides deletion; recipients are reauthorised at delivery/access time.

## 6. Use cases

- Define/version/approve a report and field lineage.
- Schedule with bounded parameters and secure recipient policy.
- Build approved read-model snapshot and render PDF/Excel/CSV.
- Prepare/review/amend/submit a regulatory return.
- Distribute using secure time-limited access and retain evidence.
- Re-run a failed report without duplicate distribution.

## 7. API contracts

| Method | Route | Permission | Idempotency |
| --- | --- | --- | --- |
| POST | `/api/v1/report-definitions` | `report.definition.manage` | Request key |
| POST | `/api/v1/report-schedules` | `report.schedule.manage` | Schedule key |
| POST | `/api/v1/report-runs` | `report.run.execute` | Definition/params/as-of key |
| GET | `/api/v1/report-runs/{id}` | `report.run.view` | Query |
| POST | `/api/v1/regulatory-returns` | `regulatory.return.prepare` | Return/period/version |
| POST | `/api/v1/regulatory-returns/{id}/decisions` | `regulatory.return.approve` | Decision key |
| POST | `/api/v1/regulatory-returns/{id}/submission-evidence` | `regulatory.return.submit` | Submission ref |

Downloads use short-lived authorised links; status never claims regulator acceptance without evidence.

## 8. Event contracts

Consume approved facts/read-model feeds from owning services. Produce `ReportRunCompleted/Failed`, `ReportDistributed`, `RegulatoryReturnPrepared/Approved/Submitted/Amended`. Use snapshot/as-of time, source version and outbox/inbox. Events contain artifact/evidence references, not full reports.

## 9. Database design

Database `risk_reporting_db`; schema `reporting` and approved governed read-model schemas. Use architecture tables plus report versions, field mappings/lineage refs, schedules, parameters, source snapshots, distributions, regulatory return versions/adjustments/submission evidence. Explicit tenant/scope, unique schedule occurrence and return/period/version, append-only decisions/adjustments and retention/legal-hold fields.

Artifacts belong in approved object storage with digest, classification and URI. Read models are rebuilt from versioned contracts/events, never cross-service table joins.

## 10. Consistency, concurrency and idempotency

Report metadata/outbox commit locally; artifact upload finalizes only after digest verification. Run key includes definition version, tenant, parameters and as-of snapshot. Schedule occurrence sends once per recipient/version, with recoverable delivery states. Source corrections create new run/return versions, not mutation.

## 11. Security and privacy

Enforce report classification, tenant/branch/product/period filters, row/field masking and recipient reauthorisation. Prevent CSV/formula injection, unsafe templates, path traversal, SSRF and cross-tenant caching. Encrypt artifacts/links; audit sensitive view/download/distribution. Do not email unrestricted sensitive attachments.

## 12. Audit and observability

Audit definition/lineage, parameters, source snapshot, run, adjustment, decisions, artifacts, access, distribution and submission. Metrics: run latency/failure/backlog, data-quality rejection, delivery failure, return deadlines/status and access denials. Alert failed/late mandatory outputs without report data in notifications.

## 13. Failure, retry and recovery

Missing/late source data yields failed/incomplete state, never fabricated values. Render/delivery retries use run/recipient keys. Partial distribution remains explicit. Submission timeout is uncertain until external evidence/status. Restore verifies metadata/artifact digests, lineage, approvals and legal hold.

## 14. Configuration

Approve/effective-date country pack, return schema/calendar, definitions, mappings, data-quality rules, formats, schedules, recipients, distribution channel, watermark/link expiry, retention and submission interfaces.

## 15. Implementation slices

1. Reporting/lineage/country-pack ADRs and permissions.
2. Governed read-model ingestion and source snapshots.
3. Definition/version/run orchestration.
4. Safe PDF/Excel/CSV rendering and artifact storage.
5. Scheduling/distribution and recipient controls.
6. Regulatory preparation/review/submission evidence and operations.

## 16. Test plan

| ID | Scenario | Expected |
| --- | --- | --- |
| RSR-01 | Report from approved snapshot | Traceable totals/fields |
| RSR-02 | PDF/Excel/CSV same run | Same financial totals/scope |
| RSR-03 | Cross-branch/tenant filter bypass | Denied/no leakage |
| RSR-04 | Schedule retry | No duplicate distribution |
| RSR-05 | Recipient loses access | Delivery/download denied |
| RSR-06 | Return preparer self-approves | Denied/audited |
| RSR-07 | Adjustment | New version with before/after evidence |
| RSR-08 | Missing source/quality failure | Output blocked/incomplete |
| RSR-09 | Formula/template attack | Neutralized/rejected |

## 17. Acceptance criteria

- [ ] Regulatory outputs use governed sources and preserve lineage/approval/submission evidence.
- [ ] Reports filter by authorised institution/branch/product/period.
- [ ] PDF/Excel/CSV exports and secure distribution follow permission/classification.
- [ ] Sensitive access/delivery is audited and legal hold/retention enforced.

## 18. Definition of Done

Repository DoD plus approved country/report specifications, lineage review, migrations, renderer security tests, format reconciliation, access/distribution tests, deadline dashboards and runbooks.

## 19. Open decisions

Regulator/return schemas, calendars, submission methods, source/lineage approval, report catalogue, formats/templates, recipients/channels, retention/legal hold and volumes remain open.

## 20. Prohibited assumptions

Do not invent report schemas, query service-private databases, use reports as ledger authority, mark submitted as accepted without evidence, or distribute sensitive unprotected attachments.

## 21. Agent implementation instructions

Read all source-service contracts, document/audit epics and approved country packs. Keep read models non-authoritative. Run lineage, migration, totals/format, security, scheduling/idempotency, load, retention and E2E tests. Stop when a source mapping or regulatory rule is unapproved.

