---
decision: P1-E03-D12
title: Consent purposes, lawful bases and customer preferences
epic: P1-E03 Customer and Onboarding
owners:
  - Legal
  - Privacy
  - Product
  - Channels
required_by: Slice 12
status: Approved
approved_on: 2026-10-29
brief_date: 2026-10-29
related: [P1-E03-D03, P1-E03-D09]
decisions: [P1-E03-D12]
---

# Decision brief: consent purposes, lawful bases and preferences

## 1. Decision

| # | Question | Answer |
| --- | --- | --- |
| D12-A | What is a preference? | A versioned, effective-dated statement that one customer allows or denies one channel to process data for one purpose. The purpose, channel and lawful basis **codes** are approved Legal and Product configuration; code validates and stores them and defines none |
| D12-B | Do preferences live on the customer aggregate? | No. They are their own versioned records keyed by customer, purpose and channel, so a purpose can be added or withdrawn without rewriting the customer or its classification |
| D12-C | What makes a granted preference valid? | A granted preference must cite a lawful basis (`consent_lawful_basis_required`). A denial needs no lawful basis but does need a reason when it withdraws an earlier grant |
| D12-D | How does change work? | Each change closes the current version and appends the next. Repeating the current state is idempotent and creates no new version. Consent evidence is append-only and keeps every version with its source and effective period |
| D12-E | Who may change a preference? | The customer through an approved channel, or a staff member holding the preference permission. The request cannot nominate its own customer identity; the caller's scope decides it |
| D12-F | Does a denial delete data? | No. Withdrawal records evidence and stops future processing for that purpose; it never deletes financial, KYC or audit history, which follow the retention decision (`P1-E03-D14`) |

## 2. Model

| Element | Content | Source |
| --- | --- | --- |
| Purpose | Stable code such as a marketing or servicing purpose | Legal and Privacy configuration |
| Channel | Stable channel code | Channels configuration |
| State | `permitted` or `denied` | Customer or staff decision |
| Lawful basis | Stable code, required for `permitted` | Legal configuration |
| Source | `customer`, `officer` or `channel` | Recorded, never inferred |
| Version | Monotonic per customer, purpose and channel | This service |
| Effective period | `effective_from` and `effective_to` | This service |
| Evidence | Append-only consent row per version with actor and reference | This service |

Only one version per customer, purpose and channel is current; the database enforces that with a partial
unique index rather than caller discipline.

## 3. Constraints

- No purpose, channel or lawful basis is invented in code; an unknown code is a data fault, not a default.
- A preference never overrides a legal obligation, a KYC requirement or a status restriction, and it never
  activates or restricts a customer.
- Consent evidence is append-only, carries no free-text personal data and captures the actor and source.
- A self-service change is attributed to the customer identity; a staff change is attributed to the staff
  actor. Separation of duties and step-up belong to the permission model (`P1-E02-D10`).
- Nothing here deletes, anonymises or exports data. Data-subject requests are their own workflow
  (`P1-E03-D14`).

## 4. Deferred for named approval

| Item | Owner | Why it is not decided here |
| --- | --- | --- |
| The purpose catalogue, lawful bases and per-purpose retention | Legal and Privacy | Requires the approved privacy notice and record of processing |
| Channel list and per-channel defaults | Product and Channels | Requires the launch channel journeys |
| Marketing eligibility and quiet-hours rules | Product and Compliance | Business rules outside customer data ownership |
| Data-subject request handling and deletion/anonymisation | Legal, Privacy and Compliance | Set with `P1-E03-D14` |

## 5. Sign-off

| Role | Name | Decision | Date |
| --- | --- | --- | --- |
| Project owner | Recorded in the implementation session | Approved versioned preferences with data-driven purpose and lawful-basis codes, append-only consent evidence and idempotent repeats | 2026-10-29 |
| Legal | | Named confirmation required before any purpose or lawful basis is published | |
| Privacy | | Named confirmation required before consent evidence is relied on in production | |
| Product | | Named confirmation required before channel defaults are enabled | |
