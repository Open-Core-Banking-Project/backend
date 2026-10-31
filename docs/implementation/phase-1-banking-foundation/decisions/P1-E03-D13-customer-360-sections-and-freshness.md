---
decision: P1-E03-D13
title: Customer 360 launch sections, section permissions and freshness
epic: P1-E03 Customer and Onboarding
owners:
  - Product
  - Privacy
  - Domain owners
required_by: Slice 13
status: Approved
approved_on: 2026-10-29
brief_date: 2026-10-29
related: [P1-E03-D03, P1-E03-D11]
decisions: [P1-E03-D13]
---

# Decision brief: Customer 360 sections, permissions and freshness

## 1. Decision

| # | Question | Answer |
| --- | --- | --- |
| D13-A | Which sections exist in Phase 1? | Three **owned** sections the service can answer truthfully (`profile`, `kyc`, `merges`) and five explicit **unavailable** sections (`accounts`, `cases`, `risk`, `documents`, `channels`). An unavailable section is reported as unavailable with a stable reason; it is never fabricated, estimated or silently omitted |
| D13-B | Who may read which section? | Each section names its own permission. The caller requests sections explicitly; an unknown section is refused, and a section is only read when its permission is held. `customer.profile.view` never implies unmasking or another section |
| D13-C | How is data masked? | Masked by default: contact points and address lines only in their masked form, no identifiers, no document content, no provider payload. Codes, statuses, references and counts are returned as they are stored |
| D13-D | How is freshness expressed? | Every section carries its source, an `as_of` timestamp and a status of `available` or `unavailable`. A section assembled from this service's own tables is `available` with the read time; anything else is `unavailable` until its service is integrated |
| D13-E | Can Customer 360 change anything? | No. It is a read-only composition: no writes, no write-back to a source service, no command side effects and no caching that hides a fresher value |

## 2. Section catalogue

| Section | Owner | Permission | Phase 1 status |
| --- | --- | --- | --- |
| `profile` | Customer and Onboarding | `customer.profile.view` | Available: number, status, branch, masked contacts, approved classification |
| `kyc` | Customer and Onboarding | `customer.kyc.view` | Available: case status and requirement counts, no evidence content |
| `merges` | Customer and Onboarding | `customer.merge.propose` | Available: open merge count and alias redirect |
| `accounts` | Core Banking and Ledger | `customer.profile.view` | Unavailable until Core exposes a read contract |
| `cases` | Operations and Workflow | `customer.profile.view` | Unavailable until Workflow exposes a read contract |
| `risk` | Risk and Reporting | `customer.profile.view` | Unavailable until Risk exposes a reference read |
| `documents` | Document storage | `customer.kyc.view` | Unavailable: metadata only, no content access |
| `channels` | Channels and Engagement | `customer.profile.view` | Unavailable until channel read models exist |

The final launch set, its permissions and its layout remain Product and Privacy data; this brief fixes the
**shape** (per-section permission, status, source, freshness) and the refusal behaviour, not the marketing copy.

## 3. Constraints

- A request may ask for a bounded number of sections; an empty request returns nothing rather than everything.
- Responses never contain raw identifiers, full contact values, full addresses, document content, tokens or
  provider references.
- A section that requires another service reports `unavailable` with a stable reason code; it never returns a
  stale value labelled as current and never blocks the sections that are available.
- Denials use the same stable codes as the rest of the service; a section that the caller may not read is
  refused, not silently dropped from a successful response.
- Customer 360 is not a data-export endpoint and is not a bulk search.

## 4. Deferred for named approval

| Item | Owner | Why it is not decided here |
| --- | --- | --- |
| Core, Workflow, Risk, Document and Channel read contracts | Domain owners | Each service must publish its own contract and permission |
| Section layout, ordering and channel-specific variants | Product and Channels | Presentation decisions |
| Field-level unmasking policy and step-up rules | Privacy and Security | Belongs with `P1-E03-D03` field classification |
| Freshness limits and cache TTLs | Platform and Domain owners | Requires the integration SLAs |

## 5. Sign-off

| Role | Name | Decision | Date |
| --- | --- | --- | --- |
| Project owner | Recorded in the implementation session | Approved three truthful local sections, explicit unavailable placeholders, per-section permissions and read-only composition | 2026-10-29 |
| Privacy | | Named confirmation required before any section is enabled in production | |
| Core Banking and Ledger | | Named confirmation required before the accounts section is enabled | |
