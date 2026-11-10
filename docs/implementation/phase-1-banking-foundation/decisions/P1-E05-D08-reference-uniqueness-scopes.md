---
decision: P1-E05-D08
title: External and source reference uniqueness scopes
epic: P1-E05 Core Ledger and Financial Posting
owners:
  - Domain owners
  - Finance
required_by: P1-E05-S1
status: Approved
approved_on: 2026-11-09
approved_by: Project owner
brief_date: 2026-11-08
related: [P1-E05-D01, P1-E05-D07, ADR-0007]
decisions: [P1-E05-D08]
---

# Decision brief: reference uniqueness scopes

The epic's invariant 8 says "transaction, journal and configured external references are unique in their defined
scopes" — and the scopes are undefined. This is the decision that stops a provider callback, a channel retry and a
payment instruction from producing two postings for one real movement.

## 1. Decision requested

| # | Question | What the answer fixes |
| --- | --- | --- |
| D08-A | What references exist, and who owns each? | The contract fields and their validation |
| D08-B | What is the unique scope of each reference? | The database indexes and the conflict code |
| D08-C | How is a reference canonicalised before comparison? | Whether `ABC-1` and `abc-1 ` collide |
| D08-D | What happens on a duplicate — replay, conflict or refusal? | Caller-visible behaviour |
| D08-E | Which reference types are allow-listed? | The contract grammar |
| D08-F | How do reversals and partial reversals stay unique? | Reversal linkage constraints |

## 2. Why the answer is needed now

- Slice 2 defines the reference value objects. Slice 5 implements the claim and outcome. Slice 6 writes the first
  transaction.
- `P1-E07` payments will be the largest producer of external references, and a scope decision taken there instead of
  here would be inconsistent with the ledger's own guarantee.
- The account epic already asserts nothing about external references because accounts are not financial movements;
  this is the first decision that has to cover them.

## 3. Current implementation state

| Capability | State | Evidence |
| --- | --- | --- |
| Idempotency scope | Implemented and proven: institution, operation, initiator, key | `integration.idempotency_records` |
| Idempotency fingerprint | Implemented: canonical request hash, conflict on a different hash | `IdempotencyKey.Fingerprint` |
| External reference storage | **Not implemented** | No posting tables |
| Source business reference | **Not implemented**; the account module uses request ids per operation | `P1-E04` approval submissions |
| Reversal linkage | **Not implemented** | Slice 9 |

## 4. Recommended position

### D08-A The reference set

| Reference | Meaning | Owner |
| --- | --- | --- |
| `IdempotencyKey` | The caller's retry identity for one operation | Caller |
| `FinancialTransactionId` | The ledger's own root identity | Ledger |
| `SourceReference` | The business instruction in the owning service (a payment id, a transfer id) | Producing service |
| `ExternalReference` | A reference issued outside the platform (provider, switch, channel) | External party, recorded by a producer |
| `JournalReference` | The human-readable journal identity | Ledger |
| `ReversalReference` | The link from a reversal to its original | Ledger |

### D08-B Unique scopes

| Reference | Unique scope |
| --- | --- |
| Idempotency | `(institution_id, operation, initiator, idempotency_key)` |
| Financial transaction | `(institution_id, financial_transaction_id)` |
| Source reference | `(institution_id, source_service, source_reference)` |
| External reference | `(institution_id, producer, reference_type, external_reference)` |
| Journal reference | `(institution_id, journal_reference)` |
| Reversal | `(institution_id, original_transaction_id, sequence)` — sequence 1 is the full reversal; partial reversals increment |

| Rationale | |
| --- | --- |
| Producer in the external scope | Two providers may legitimately issue the same string; the pair is what is unique |
| Type in the external scope | A provider reference and a settlement reference from one provider are different facts |
| Institution in every scope | Tenants are independent; one tenant's provider ids never collide with another's |
| Source reference per producing service | Two services may each number their own instructions from 1 |

### D08-C Canonicalisation

| Item | Recommendation |
| --- | --- |
| Trim | Leading and trailing whitespace removed before validation and storage |
| Empty | A whitespace-only reference is refused, never stored as empty |
| Case | Preserved exactly; comparison is case-sensitive because provider ids are opaque |
| Character set | Printable ASCII, no control characters, no line breaks |
| Length | 1 to 128 characters for every reference |
| Prohibited | Uppercasing, diacritic folding, zero-padding or any other rewriting of an external id |

### D08-D Duplicate behaviour

| Situation | Answer |
| --- | --- |
| Same idempotency key and the same request hash | Return the recorded outcome with `replayed: true` |
| Same idempotency key and a different request hash | `idempotency_key_conflict`, no posting |
| Same source reference with the same canonical payload | Return the original transaction, `replayed: true` |
| Same source reference with a different payload | `source_reference_conflict`, no posting |
| Same external reference and type from the same producer with the same payload | Return the original transaction |
| Same external reference and type from the same producer with a different payload | `external_reference_conflict`, no posting |
| Same external reference from a different producer | Accepted as a distinct fact |

### D08-E Allow-listed reference types

`provider_reference`, `channel_reference`, `settlement_reference`, `statement_reference`, `external_reference`,
`internal_reference`. An unlisted type is refused so a producer cannot invent a namespace that bypasses a scope.

### D08-F Reversals

| Item | Recommendation |
| --- | --- |
| Full reversal | One per original transaction: `sequence = 1` |
| Partial reversal | Sequence increments, so a second partial reversal is a distinct fact rather than a duplicate |
| Ceiling | The total reversed amount across sequences may never exceed the approved reversible remainder, asserted under the original's lock |
| Idempotency | A repeated reversal request resolves through its own idempotency key and returns the recorded reversal |

## 5. Constraints

- Do not compare external references case-insensitively or after any rewriting.
- Do not scope an external reference on institution alone; a provider id is only meaningful with its producer and type.
- Do not treat a duplicate reference as a business rejection when the payload is identical; that is a replay.
- Do not let a producer choose its own uniqueness scope through a free-text type.
- This brief does not approve idempotency window expiry (`P1-E05-D16` retention) or reversal policy (`P1-E05-D10`).

## 6. Deferred for named approval

| Item | Owner | Why it is not decided here |
| --- | --- | --- |
| How long a reference must be retained before reuse is permitted | Compliance/DBA | `P1-E05-D16` |
| Provider-specific reference formats | Payments owner | `P1-E07`, constrained by this grammar |
| Whether a provider reference may be amended on a corrected callback | Operations | `P1-E07` |
| Statement and receipt reference formats | Product | `P1-E05-D15` |

## 7. Sign-off

| Role | Name | Decision | Date |
| --- | --- | --- | --- |
| Project owner | User | Approved recommended position | 2026-11-09 |
| Domain owners | | Pending: confirm source and external scopes | |
| Finance | | Pending: confirm the duplicate-versus-conflict rule | |

## 8. Implementation notes (for slices 2, 5, 6 and 9)

1. Implement one reference validator used by every contract, so length, character set and trimming cannot drift.
2. Create the unique indexes exactly as approved, in the same migration as the table.
3. Distinguish replay from conflict by comparing the canonical payload hash stored with the reference.
4. Assert the reversal ceiling under the original transaction's lock, not by counting rows afterwards.
5. Prove with tests: the same provider reference from two producers is accepted, from one producer with a different
   payload is refused, a case-different reference is a different reference, a whitespace-only reference is refused,
   and two concurrent postings with one source reference produce one transaction.
