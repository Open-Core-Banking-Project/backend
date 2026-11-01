---
decision: P1-E03-D06
title: KYC checks, provider posture and canonical verification outcomes
epic: P1-E03 Customer and Onboarding
owners:
  - Compliance
  - Product
  - Integration
required_by: Slice 7
status: Approved
approved_on: 2026-10-29
brief_date: 2026-10-29
related: [P1-E03-D01, ADR-0004]
decisions: [P1-E03-D06]
---

# Decision brief: KYC checks, provider posture and canonical outcomes

## 1. Decision

| # | Question | Answer |
| --- | --- | --- |
| D06-A | Which KYC checks run in Phase 1? | The **model and contracts** only. The approved check list is configuration data supplied by Compliance (the same way the onboarding requirement pack is data); no check, provider or country rule is authored in code |
| D06-B | Which provider is bound? | None. Phase 1 binds no production provider. Integration Hub owns provider protocol, credentials and routing; a synthetic adapter may be wired in slice 8 behind the Integration Hub port for end-to-end evidence |
| D06-C | How does a provider result become a decision? | Only through a **versioned provider-result mapping** supplied as configuration. Unknown, missing or unmapped provider statuses never pass: they resolve to `inconclusive` and require manual review |
| D06-D | What is a timeout or an unanswered call? | `uncertain`. It is not failure and not success. It requires a status enquiry or manual review, and it never permits blind resubmission that could produce two accepted outcomes |
| D06-E | What may the customer service decide alone? | It records the canonical outcome and the requirement state. It never activates a customer: activation still needs every mandatory requirement passed or waived and the approved classification (`P1-E03-D09`) |

## 2. Canonical outcomes

| Canonical outcome | Meaning | Requirement effect |
| --- | --- | --- |
| `passed` | The check reached a positive terminal result through the mapping | Requirement `passed` |
| `failed` | The check reached a negative terminal result through the mapping | Requirement `failed` |
| `inconclusive` | The provider answered something that is not a decision, or answered nothing | Requirement `inconclusive`, then manual review |
| `manual_review` | A human must decide | Requirement `manual_review` until a reviewer resolves it |

Verification lifecycle is the epic's: `created → submitted → pending → succeeded | failed | uncertain`, with
`uncertain → status enquiry` and `inconclusive → manual review`. A verification that reached a terminal
outcome cannot be re-resolved; a further check is a new verification.

## 3. Constraints

- A verification always belongs to one application, one party and one approved check code, and it is bound
  to the requirement snapshot of the revision that requested it.
- Attempts are immutable evidence: a new provider call is a new attempt, never an edit of the previous one.
- Two accepted outcomes for one logical verification are impossible; reconciliation is required instead of
  a second acceptance.
- No raw provider payload, identifier or document content is stored in the case, requirement, verification
  or history rows: only canonical codes, references and evidence references.
- Manual review requires an actor and a reason, and it records the exact verification outcome it resolved.
- Waiving a requirement requires explicit authority recorded on the requirement; there is no default waiver.

## 4. Deferred for named approval

| Item | Owner | Why it is not decided here |
| --- | --- | --- |
| Launch check list, per-country evidence kinds and tiers | Compliance and Product | Requires the approved jurisdictional rulebook and the risk appetite |
| Provider selection, certification and live contract | Compliance and Integration | Requires provider due diligence and sandbox certification |
| Provider-result mapping tables and version numbering | Compliance and Integration | Content of a mapping depends on the chosen provider's status vocabulary |
| Retry, failover and status-enquiry intervals | Integration and Compliance | Set with `P1-E03-D07` and provider SLAs |
| Raw evidence format, storage and retention | Compliance, Security and Privacy | Set with `P1-E03-D08` and `P1-E03-D14` |

## 5. Sign-off

| Role | Name | Decision | Date |
| --- | --- | --- | --- |
| Project owner | Recorded in the implementation session | Approved a data-driven check model with no bound provider, canonical outcomes only through a versioned mapping, and uncertain never treated as pass or fail | 2026-10-29 |
| Compliance | | Named confirmation required before any check or mapping is published | |
| Integration | | Named confirmation required before a provider is routed through Integration Hub | |
