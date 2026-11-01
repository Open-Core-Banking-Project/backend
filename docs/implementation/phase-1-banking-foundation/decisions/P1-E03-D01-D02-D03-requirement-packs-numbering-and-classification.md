---
decision: P1-E03-D01
title: Country-pack ownership, customer numbering and field classification
epic: P1-E03 Customer and Onboarding
owners:
  - Product
  - Compliance
  - Security
  - Operations
required_by: Slice 5
status: Approved
approved_on: 2026-10-29
brief_date: 2026-10-29
related: [P1-E03-D04, ADR-0004, ADR-0007]
decisions: [P1-E03-D01, P1-E03-D02, P1-E03-D03]
---

# Decision brief: requirement packs, numbering and field classification

## 1. Decision

| # | Question | Answer |
| --- | --- | --- |
| D01-A | Who owns the launch country pack? | Product and Compliance own its content. It is **institution data**: versioned, effective-dated and content-hashed, published through the service. Engineering authors no jurisdictional field, evidence or check rule in code |
| D01-B | What ships in Phase 1? | The mechanism only: a pack schema, an immutable requirement snapshot per submitted revision, and a hard refusal when no approved pack is effective. No Nigerian, UK or other national requirements are hard-coded, seeded or implied |
| D01-C | What happens when the pack changes? | A published pack supersedes the previous version for **new** submissions only. An existing snapshot keeps its pack code, version and hash forever; a change creates re-evaluation work, never a silent rule change |
| D01-D | What happens when no pack is effective? | Submission is denied with `onboarding_requirement_pack_missing`. A missing pack is a configuration fault, not a reason to accept an unvetted application |
| D02-A | How is a party identified? | An opaque 128-bit random identifier. It carries no meaning, no sequence and no country or branch hint |
| D02-B | How is a customer number formed? | Institution-unique, allocated from an institution-scoped sequence, never reused and never re-derived from a party or application. Format and check digit are Operations data, not code constants |
| D02-C | Is the application reference generated? | No. The officer or channel supplies it; it is unique per institution and is what the applicant is told |
| D03-A | What is the field classification? | Adopt the epic's classification unchanged: restricted (government identifiers, biometrics, provider evidence, document content, screening detail), confidential (profile, addresses, contacts, next of kin, KYC tier, risk class, cases), internal (operational state), and no public customer attribute by default |
| D03-B | How are restricted and confidential values protected? | Encrypted with AES-256-GCM through `IProtectedValueProtector` using a fresh nonce per value, plus a deterministic HMAC-SHA256 lookup token only where exact matching is required (contact point, address line, date of birth, identifier). Keys are references to the approved secret/KMS store, never values in configuration files |
| D03-C | What may appear in logs, events and URLs? | Internal references, stable codes and masked forms only. No restricted or confidential plaintext, no document content, no provider payload, no authentication secret |

## 2. Requirement pack mechanics

| Aspect | Rule |
| --- | --- |
| Identity | Pack code plus monotonically increasing version; one effective pack per institution |
| Content | Ordered requirements: stable code, label, mandatory flag and evidence kind codes. Labels are display data supplied by the pack |
| Integrity | SHA-256 content hash over the canonical pack form, stored with the pack and copied into every snapshot |
| Immutability | A published version is never edited; a change publishes the next version |
| Snapshot | One immutable snapshot per submitted application revision, linked to the pack code, version and hash |
| Evaluation | Missing configuration denies the operation. Outstanding mandatory requirements are recorded, never assumed satisfied |

The pack deliberately contains no amount, limit, risk tier or timeframe. Those remain Compliance and Risk policy decisions
(`P1-E03-D09`, `P1-E03-D14`) and are not invented here.

## 3. Numbering

- Party identifiers are opaque and random, so a leaked identifier reveals nothing about volume, sequence or geography.
- Customer numbers, when the customer record lands, come from an institution-scoped sequence; a retry never consumes two
  numbers for one customer, and a closed customer number is never reissued.
- No identifier, reference or sequence value is derived from a national identifier, date of birth or branch.
- Formatting, prefixes and check digits are configuration owned by Operations; code only enforces institution uniqueness
  and immutability.

## 4. Classification and protection

| Class | Examples | Storage rule | Display rule |
| --- | --- | --- | --- |
| Restricted | Government identifiers, biometrics, provider raw evidence, document content, screening detail | Encrypted, tokenised for exact match, referenced by opaque storage reference | Masked; disclosure needs the specific permission, purpose and audit evidence |
| Confidential | Full profile, addresses, contacts, next of kin, KYC tier, risk class, cases | Encrypted or protected at field level | Masked by default; field permissions decide the rest |
| Internal | Application state, references, requirement codes, candidate bands | Plain | Allowed in operational views and logs |
| Public | None by default | Not applicable | Not applicable |

- A new field is classified before it is persisted; an unclassified field is treated as restricted.
- Masking follows the existing protector behaviour: the trailing characters needed to confirm a value the caller already
  holds remain visible, everything else is asterisks.
- Classification changes are a Security decision recorded against this brief, not a code-review detail.

## 5. Deferred for named approval

| Item | Owner | Why it is not decided here |
| --- | --- | --- |
| Launch pack content (fields, evidence kinds, check list) | Product and Compliance | Requires the approved jurisdictional rulebook |
| Customer-number format, prefix and check digit | Operations and Product | Requires the numbering standard and migration plan |
| Key management service, rotation and retention of key material | Security | Requires the KMS design (`ADR-0004` follow-up) |
| Document content types, size limits and retention | Security, Compliance and Privacy | Set with `P1-E03-D06`, `P1-E03-D08` and `P1-E03-D14` |
| Data residency and cross-border processing | Legal and Compliance | Outside the field-classification scope |

## 6. Sign-off

| Role | Name | Decision | Date |
| --- | --- | --- | --- |
| Project owner | Recorded in the implementation session | Approved data-owned requirement packs with a hard refusal when absent, opaque party identifiers, sequence-based customer numbers and the epic's field classification | 2026-10-29 |
| Product | | Named confirmation required before the launch pack content is published | |
| Compliance | | Named confirmation required before any jurisdictional requirement is published | |
| Security | | Named confirmation required before production key management and classification sign-off | |
