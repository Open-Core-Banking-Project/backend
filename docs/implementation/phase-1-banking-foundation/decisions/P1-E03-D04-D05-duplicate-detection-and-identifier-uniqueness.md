---
decision: P1-E03-D04
title: Duplicate detection rules, candidate evidence and verified-identifier uniqueness
epic: P1-E03 Customer and Onboarding
owners:
  - Compliance
  - Data
  - Security
required_by: Slice 6
status: Approved
approved_on: 2026-10-29
brief_date: 2026-10-29
related: [P1-E02-D10, 03-customer-and-onboarding.md]
decisions: [P1-E03-D04, P1-E03-D05]
---

# Decision brief: duplicate detection rules and verified-identifier uniqueness

## 1. Decision

| # | Question | Answer |
| --- | --- | --- |
| D04-A | What does Phase 1 duplicate detection compare? | Only exact matches over already-protected tokens: contact-point lookup token per channel, legal-name normalised form together with the full date-of-birth token, and address line token. No fuzzy, phonetic, transliteration or learned matching in Phase 1 |
| D04-B | Which thresholds or scores apply? | None. A candidate carries ordered match *factors* and a derived band; no numeric score, weight or cut-off is set, so no threshold is invented by code |
| D04-C | What can a reviewer conclude? | `same_person`, `distinct` or `insufficient_evidence`. No outcome merges, rejects or blocks a customer; merge stays a separate approved proposal |
| D04-D | How long is a detection run valid? | One live candidate set per application revision. A later run supersedes the earlier open set, which stays readable as evidence with its rule version |
| D04-E | When are probabilistic matches allowed? | Only after Compliance and Data approve factor categories, model/rule versions, bias testing and reviewer sampling. That approval is not granted by this brief |
| D05-A | Which identifiers must be unique? | Verified government identifiers recorded per institution. Uniqueness is enforced on the protected lookup token, never on plaintext |
| D05-B | What happens when two customers legitimately share an identifier? | It is an explicit Compliance exception with a recorded case reference; the system routes it to manual review and never auto-rejects, auto-merges or silently deletes a record |
| D05-C | Is matching cross-institution? | No. Every comparison is scoped to the caller's institution, and no candidate, count or timing is exposed outside it |

## 2. Match factors and bands

| Factor kind | Basis | Produced when |
| --- | --- | --- |
| `contact_point` | `mobile`, `email`, `landline` | The same channel has the same lookup token |
| `name_and_birth_date` | `name_and_birth_date` | The normalised legal name **and** the full date-of-birth token both match |
| `address_line` | `address_line` | A protected address line token matches |

| Band | Derivation | Meaning |
| --- | --- | --- |
| `possible` | Any single factor family other than name + date of birth | Worth a reviewer look; commonly a shared household, employer or recycled number |
| `probable` | Name and date of birth, alone or with further factors | Strong exact evidence of the same person; still requires a reviewer decision |
| `confirmed` | Reserved for a verified-identifier factor | Not reachable in Phase 1, because verified identifiers arrive with the identifier slices |

Bands are ordered for presentation only. They are not risk classes, not a rejection rule and not an
approval shortcut: a customer is never blocked, merged or closed because of a band.

## 3. Reviewer evidence

- Candidates show the candidate party reference, its current status, the factor codes and masked
  comparison values only (for example a masked email, a masked address line, a birth year).
- The candidate set records the rule version and the detection time, so a later reviewer sees which
  rule produced the evidence.
- A decision records the reviewer, the outcome, the reason and the set version it was taken against.
- A decision is refused with `duplicate_candidates_stale` when the candidate's protected data changed
  after detection, and with `duplicate_candidate_set_stale_version` when another reviewer already
  decided the set. Both refusals require a fresh detection run rather than a silent overwrite.
- Decisions are final for a set. Reopening a decided set is a new detection run, which keeps the
  earlier decision permanently visible.

## 4. Constraints

- No raw identifier, contact value, address line or provider reference is stored in a candidate row,
  logged, emitted in an event, put in a URL or returned to a reviewer.
- Candidate collections are bounded; there is no unbounded scan endpoint and no customer-search API.
- Detection never creates, updates or deletes a party, customer, account or KYC record.
- Candidate sets follow the customer and KYC evidence retention decision (`P1-E03-D14`); this brief
  does not set a retention period.
- A verified-identifier match, once identifiers exist, adds a factor and the `confirmed` band; it still
  requires a reviewer decision and never triggers an automatic merge.
- Legitimate shared identifiers (family phone, employer address, joint arrangement) must not degrade
  into a false rejection; they are reviewed, not refused.

## 5. Deferred for named approval

| Item | Owner | Why it is not decided here |
| --- | --- | --- |
| Probabilistic or learned matching, factor weights and cut-offs | Compliance and Data | Needs false-positive/false-negative sampling, bias review and reviewer capacity evidence |
| Identifier exception register and legal case types | Compliance | Needs the institution's legal and regulatory case taxonomy |
| Retention period for candidate sets and decisions | Legal, Compliance and Privacy | Set by `P1-E03-D14` with the rest of the customer evidence schedule |
| Cross-institution or group-level matching | Compliance and Architecture | Outside Phase 1 scope; requires a data-sharing basis |

## 6. Sign-off

| Role | Name | Decision | Date |
| --- | --- | --- | --- |
| Project owner | Recorded in the implementation session | Approved exact-token detection, derived bands, reviewer-only outcomes and manual handling of shared identifiers | 2026-10-29 |
| Compliance | | Named confirmation required before production duplicate handling | |
| Data | | Named confirmation required before any probabilistic matching is enabled | |
| Security | | Named confirmation required before reviewer access to masked candidate evidence | |
