---
decision: P1-E03-D14
title: Customer evidence retention, immutable audit and reconciliation posture
epic: P1-E03 Customer and Onboarding
owners:
  - Legal
  - Compliance
  - Privacy
  - Operations
required_by: Slice 14
status: Approved
approved_on: 2026-10-29
brief_date: 2026-10-29
related: [P1-E03-D03, P1-E03-D12]
decisions: [P1-E03-D14]
---

# Decision brief: retention, immutable audit and reconciliation posture

## 1. Decision

| # | Question | Answer |
| --- | --- | --- |
| D14-A | What is the retention period for each record class? | Not set here. Retention is **institution data supplied by Legal and Compliance**, per record class and jurisdiction. Engineering ships no number, no default and no "reasonable" value |
| D14-B | What happens when no retention policy is configured? | Nothing is purged. The scheduled job reports "no policy" and deletes nothing; an absent policy is a configuration fault, not a licence to delete |
| D14-C | Which records are never purged by code? | Audit events, status history, consent evidence, merge plans and aliases, KYC decisions and waivers. They are append-only and are only ever addressed by a separate, approved legal process |
| D14-D | How is the audit trail protected? | A dedicated `audit` schema the application principal may only `SELECT` and `INSERT` on; no update or delete path exists in code. Each audited action also emits a SIEM event through the outbox with the same identity, so a downstream sink keeps an independent copy |
| D14-E | What does the reconciliation job check? | Deterministic integrity invariants that must always hold: merges awaiting consumers, applied merges without an alias, aliases pointing at a missing survivor, restricted/deceased/closed transitions without an approval reference, and KYC requirements at a decision state with no linked verification. It reports; it never repairs silently. A lawful basis for every grant is already a database constraint, so it is enforced at write time rather than reconciled later |
| D14-F | Do time-based thresholds exist? | Not yet. "Awaiting consumers for too long" needs an Operations-approved threshold (`P1-E03-D15`), so the job reports counts and identifiers instead of inventing a limit |

## 2. Record classes

| Class | Examples | Retention owner | Purge |
| --- | --- | --- | --- |
| Audit and SIEM | `audit.customer_audit_events`, outbox audit events | Compliance | Never by code |
| Lifecycle evidence | status history, classification versions, merge plans, aliases, consent evidence | Legal and Compliance | Never by code |
| Operational records | applications, parties, customers, preferences, requirement snapshots | Legal, Compliance and Privacy | Only by an approved retention job, which does not exist yet |
| Document metadata | `kyc.document_references` | Compliance and Security | Only with the document retention decision and the storage component |

## 3. Constraints

- No personal data in audit detail: stable action codes, subject references, statuses and approval references
  only; never a name, identifier, contact value or document content.
- An audit row is written in the same transaction as the state change it describes; an action cannot be
  committed without its audit row.
- SIEM events reuse the shared envelope and outbox; a failed publication is retried, not dropped.
- Reconciliation findings are reported to operations with identifiers; no automatic fix, no silent
  reassignment and no state mutation.
- Deleting or anonymising customer data is out of scope until the retention policy and the data-subject
  process are approved.

## 4. Deferred for named approval

| Item | Owner | Why it is not decided here |
| --- | --- | --- |
| Retention periods per class and jurisdiction | Legal and Compliance | Requires the approved retention schedule |
| Data-subject export/erasure implementation | Legal, Privacy and Compliance | Requires the approved process and legal basis |
| Dashboards, alert thresholds and on-call routing | Operations | Requires staffing and the operations calendar |
| SIEM sink, forwarding guarantees and retention at the sink | Security | Requires the security platform decision |
| Stuck-merge and stale-requirement thresholds | Operations | Needs service levels (`P1-E03-D15`) |

## 5. Sign-off

| Role | Name | Decision | Date |
| --- | --- | --- | --- |
| Project owner | Recorded in the implementation session | Approved configuration-only retention with no purge by default, append-only audit with SIEM publication and a deterministic, report-only reconciliation job | 2026-10-29 |
| Legal | | Named confirmation required before any retention period is configured | |
| Compliance | | Named confirmation required before audit evidence is relied on in production | |
| Operations | | Named confirmation required before reconciliation alerts are enabled | |
