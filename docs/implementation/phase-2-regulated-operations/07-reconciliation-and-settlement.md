---
phase: "Phase 2 - Regulated Operations"
epic: "Reconciliation and Settlement"
owning_service: "Audit and Reconciliation"
modules: [Source Ingestion, Matching, Breaks, Suspense, Settlement, Certification]
status: Draft
requirements: [REC-001..REC-010, NIB-005, PSP-006, AUD-004, REP-002, BR-012]
stories: [US-051, US-052, US-053, US-054]
dependencies: [Phase 1 Payments and Providers, Phase 1 Audit and Traceability, Phase 1 Ledger]
adrs: [Reconciliation evidence and matching, Settlement calendars and positions, Suspense correction]
last_updated: 2026-10-16
---

# Epic 07: Reconciliation and Settlement

## 1. Objective

Ingest authenticated provider/bank records, match them to internal payment and ledger outcomes, expose and resolve breaks, calculate settlement positions and certify periods with immutable evidence.

## 2. Success measures

- Files/API records validate source, checksum/signature, schema, institution, period and duplicate identity.
- Exact and approved tolerance rules produce reproducible matched/unmatched/duplicate/amount-mismatch outcomes.
- Breaks retain owner, age, SLA, evidence and links to provider disputes or ledger corrections.
- Certification is blocked by material exceptions unless an authorised waiver is evidenced.

## 3. Scope

Included: source registration, file/API ingestion, quarantine, canonical rows, match-rule versions, batches, matches, breaks, suspense investigations, settlement positions, certification and evidence export. Excluded: direct ledger edits, invented tolerance/materiality, and provider-specific parsing outside Integration Hub adapters.

## 4. Actors, roles and permissions

| Actor | Permission | Scope | Step-up | Approval |
| --- | --- | --- | --- | --- |
| Reconciliation operator | `reconciliation.batch.manage` | Institution/source/period | Manual actions | Maker |
| Break investigator | `reconciliation.break.resolve` | Assigned break/source | Adjustments | Approval when financial |
| Rule manager | `reconciliation.rule.manage` | Institution/source | Publish | Maker-checker |
| Finance supervisor | `reconciliation.certify` | Period/materiality | Required | Cannot certify own preparation alone |
| Ingestion identity | `reconciliation.source.ingest` | Approved source/environment | Workload | None |

## 5. Domain model

Concepts: `SourceDefinition`, `Ingestion`, `SourceRecord`, `ReconciliationBatch`, `MatchRuleVersion`, `MatchCandidate`, `Match`, `Break`, `BreakAction`, `SettlementCycle/Position`, `Certification`, `Waiver`.

States: ingestion `Received -> Validating -> Accepted/Quarantined -> Parsed`; batch `Open -> Matching -> Review -> ReadyToCertify -> Certified`; break `Open -> Assigned -> Investigating -> Resolved/AcceptedWithAuthority/WrittenOff`; never silently closed.

Invariants: raw evidence/digest is immutable; duplicate ingestion creates no duplicate records; matches reference exact rule version; tolerance does not alter amounts; corrections use controlled ledger commands; certification accounts for every material break; replay creates no duplicate postings/breaks.

## 6. Use cases

- Receive and validate a settlement file/API dataset.
- Parse canonical records and quarantine invalid input.
- Run/re-run matching under an approved rule version.
- Assign/investigate/manual-match or request controlled correction.
- Calculate settlement positions and compare expected/actual.
- Review and certify a period or approve an explicit waiver.

## 7. API contracts

| Method | Route | Permission | Idempotency |
| --- | --- | --- | --- |
| POST | `/api/v1/reconciliation/ingestions` | `reconciliation.source.ingest` | Source/file digest |
| POST | `/api/v1/reconciliation/batches/{id}/match` | `reconciliation.batch.manage` | Batch/rule/version |
| GET | `/api/v1/reconciliation/breaks` | `reconciliation.break.view` | Query |
| POST | `/api/v1/reconciliation/breaks/{id}/actions` | `reconciliation.break.resolve` | Action key |
| GET | `/api/v1/settlements/positions` | `settlement.position.view` | Query |
| POST | `/api/v1/reconciliation/batches/{id}/certifications` | `reconciliation.certify` | Decision key |

Uploads are bounded/streamed, malware-scanned where applicable and never parsed by trusting filename/content type alone.

## 8. Event contracts

Consume payment/provider/ledger/VTU/term-deposit/loan/teller/agent facts. Produce `SettlementSourceIngested`, `ReconciliationBatchMatched`, `ReconciliationBreakOpened`, `ReconciliationBreakResolved`, `SettlementPositionCalculated`, `ReconciliationPeriodCertified`. Use inbox/outbox, explicit source IDs and safe evidence refs.

## 9. Database design

Database `audit_reconciliation_db`; schemas `reconciliation`, `settlement`, `evidence`, `integration`. Use architecture tables. Enforce unique institution/source/environment/external-file identity and record identity; immutable raw digests/source rows; rule version/effective dates; explicit amount/currency; optimistic break/certification versions; period/source/status indexes and approved partition/retention.

Large raw files live in approved immutable object storage; database rows retain digest, classification and URI. No cross-service foreign keys.

## 10. Consistency, concurrency and idempotency

Ingestion registration, match batch state and outbox use local transactions. Workers claim bounded partitions and checkpoint. Matching is deterministic for the same canonical rows/rule version. Manual action uses optimistic concurrency. Ledger correction is asynchronous and break remains pending until authoritative posting and re-match succeed.

## 11. Security and privacy

Authenticate source/channel, validate checksum/signature, scan/quarantine input and protect evidence. Enforce tenant/source/period scope and separated preparation/certification. Test file/path/parser attacks, replay, formula injection in exports, cross-tenant search and unauthorised waiver/manual match.

## 12. Audit and observability

Audit ingestion, validation, parser/rule version, matching, manual actions, correction refs, waivers, certification and exports. Metrics: ingestion lag/failure, unmatched/duplicate/mismatch count/value/age, suspense age, position variance and certification delay. Alert material exceptions and stalled batches without sensitive rows in notifications.

## 13. Failure, retry and recovery

Invalid input is quarantined with stable reasons. Parser/matcher restarts from checkpoints. Provider/internal data absence remains a break, not fabricated match. Correction timeout is uncertain and requeried. Restore verifies file digests, source counts, match totals, open breaks and certification integrity.

## 14. Configuration

Version/approve source schema, mapping, calendars, cutoffs, match/tolerance rules, materiality, SLA, suspense, waiver authority, certification and retention. Values are Finance/Operations decisions.

## 15. Implementation slices

1. Source/evidence contracts and secure ingestion.
2. Canonical records, batch and rule versions.
3. Exact matching, then approved tolerance matching.
4. Break assignment, investigation and correction workflow.
5. Positions, suspense monitoring and certification.
6. Replay/load, audit, reports, dashboards and runbooks.

## 16. Test plan

| ID | Scenario | Expected |
| --- | --- | --- |
| RCS-01 | Valid file ingestion | Canonical rows/counts/digest |
| RCS-02 | Duplicate/tampered file | Deduplicated or quarantined |
| RCS-03 | Exact/tolerance match | Reproducible explicit outcome |
| RCS-04 | Amount mismatch | Open break, no amount mutation |
| RCS-05 | Manual match/adjustment | Approval/evidence and ledger ref |
| RCS-06 | Material open break | Certification blocked |
| RCS-07 | Replay batch | No duplicate matches/breaks/postings |
| RCS-08 | Cross-tenant/source access | Denied/audited |
| RCS-09 | Restore | Totals/digests/open state verified |

## 17. Acceptance criteria

- [ ] NIBSS/PSP/VTU and enabled sources ingest canonically with quarantine.
- [ ] Matching yields required outcome states under approved rules.
- [ ] Breaks and suspense remain visible until authorised resolution.
- [ ] Certification records preparer/reviewer and exports evidence.

## 18. Definition of Done

Repository DoD plus approved source/match/settlement policy, parser contracts, migration/load/replay evidence, ledger correction workflow, dashboards and runbooks.

## 19. Open decisions

Source formats/transport, calendars/cutoffs, tolerance/materiality, suspense ownership, dispute/correction process, settlement fee policy, certification/waiver, retention and volumes remain open.

## 20. Prohibited assumptions

Do not silently adjust within tolerance, treat missing rows as matched, edit ledger/history, certify material exceptions without authority, or parse unauthenticated input.

## 21. Agent implementation instructions

Read provider/payment/ledger/audit epics and approved source specifications. Keep adapters in Integration Hub and matching/evidence here. Run parser, migration, matching, replay, concurrency, security, load, restore and E2E tests. Stop for unresolved financial matching policy.

