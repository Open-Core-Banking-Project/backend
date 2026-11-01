---
decision: P1-E03-D09
title: Customer classification (tier, risk, EDD) and status transitions
epic: P1-E03 Customer and Onboarding
owners:
  - Compliance
  - Risk
  - Product
  - Operations
required_by: Slice 9
status: Approved
approved_on: 2026-10-29
brief_date: 2026-10-29
related: [P1-E03-D02, P1-E03-D06, P1-E02-D10]
decisions: [P1-E03-D09, P1-E03-D10]
---

# Decision brief: classification, activation and customer status transitions

## 1. Decision

| # | Question | Answer |
| --- | --- | --- |
| D09-A | What is a classification? | A versioned record of KYC tier, segment, risk class and an enhanced-due-diligence flag. The **values** are approved configuration and Risk references; code stores and validates them, it does not define them |
| D09-B | When is a classification usable? | Only when approved through Workflow: an approved version carries the exact decision reference and approval time. A proposed version never activates a customer |
| D09-C | What does activation require? | A completed KYC case for the submitted application **and** an approved classification version. Missing either refuses activation; neither can be assumed |
| D09-D | How do changes work? | A new classification is a new version. Approving it supersedes the earlier approved version for future decisions and never rewrites history or an existing activation decision |
| D09-E | What does the EDD flag mean? | It records that enhanced due diligence is required. It creates outstanding work for the KYC slices and blocks nothing by itself in code; no threshold, score or timeframe is invented here |
| D10-A | Which status transitions are legal? | Exactly the epic's lifecycle: `pending → active | rejected`; `active → restricted | dormant | deceased | closed`; `restricted → active | closed`; `dormant → active | closed`. `rejected`, `deceased` and `closed` are terminal |
| D10-B | What does a transition require? | A reason and an actor. `restricted`, `deceased` and `closed` additionally require an approval reference; a terminal state is never reversed by code, only by an explicitly approved process that does not exist yet |
| D10-C | Does customer status change accounts? | Never in this service. The status change is published as an event; Core Banking and Ledger decides what an account does, under its own approved rules |

## 2. Classification model

| Field | Source | Notes |
| --- | --- | --- |
| Tier | Compliance configuration | Code validates presence, not the value set |
| Segment | Product configuration | Same rule |
| Risk class | Risk and Reporting reference | Stored as an approved reference, never a score this service computes |
| Enhanced due diligence required | Compliance and Risk | Recorded on the version; review dates stay with the KYC slices |
| Version | This service | Monotonic per customer; approving a version supersedes the previous approved one |
| Approval | Operations and Workflow | Exact decision reference and time; a proposal without approval cannot activate |

No monetary limit, score, threshold, review interval or country risk rating is defined here. Those remain
Compliance, Risk and Product decisions with their own records.

## 3. Status model

| From | Allowed to | Approval reference |
| --- | --- | --- |
| `pending` | `active`, `rejected` | Activation needs the approved classification; rejection needs a reason only |
| `active` | `restricted`, `dormant`, `deceased`, `closed` | `restricted`, `deceased`, `closed` |
| `restricted` | `active`, `closed` | `closed` |
| `dormant` | `active`, `closed` | `closed` |
| `rejected`, `deceased`, `closed` | none | Terminal; reopening is a new approved process, not a transition |

Constraints:

- Every transition appends an immutable history entry with from/to, reason, actor and approval reference.
- Death marking never deletes financial, KYC or audit history; it records the state and publishes an event.
- A status change from this service carries no account instruction. Account behaviour belongs to Core
  Banking and Ledger, which consumes `CustomerStatusChanged.v1`.
- Nothing here may infer reversibility: reactivation from a terminal state is refused with
  `customer_transition_forbidden` until an approved policy exists.

## 4. Deferred for named approval

| Item | Owner | Why it is not decided here |
| --- | --- | --- |
| Tier names, segment list and risk-class list | Compliance, Risk and Product | Content of the classification catalogue |
| EDD triggers, review schedule and clearance rules | Compliance and Risk | Requires the risk appetite and review policy |
| Which transitions need a second approver and at what level | Operations and Compliance | Approval levels are Workflow policy data |
| Account consequences per customer status | Core Banking and Product | Owned by Core Banking and Ledger |
| Reactivation from `deceased` or `closed` | Legal and Compliance | Requires a documented legal process |

## 5. Sign-off

| Role | Name | Decision | Date |
| --- | --- | --- | --- |
| Project owner | Recorded in the implementation session | Approved data-driven classification with Workflow approval, activation gated by KYC completion plus approved classification, and the epic's status lifecycle with no account mutation | 2026-10-29 |
| Compliance | | Named confirmation required before tier, segment and risk values are published | |
| Risk | | Named confirmation required before EDD rules and review schedules are enabled | |
| Operations | | Named confirmation required before approval levels are configured in Workflow | |
