---
decision: P1-E04-D06
title: Account-number format, scope, check digit and exhaustion
epic: P1-E04 Products and Accounts
owners:
  - Institution/Core operations
  - Architecture
required_by: P1-E04-S8
status: Ready for review
brief_date: 2026-11-06
related: [ADR-0015, P1-E04-D02, P1-E04-D09, P1-E04-D17]
decisions: [P1-E04-D06]
---

# Decision brief: account-number scheme

Slice 8 reserves and assigns account numbers. The epic forbids `max + 1`, random retry without constraints and any
unapproved universal format, so the scheme, its uniqueness scope, its check digit and its exhaustion behaviour have
to be approved before allocation code exists.

## 1. Decision requested

| # | Question | What the answer fixes |
| --- | --- | --- |
| D06-A | Who defines the number format, and is it platform-wide or institution-defined? | Where the scheme lives and who may change it |
| D06-B | What is the uniqueness scope of an account number? | Reservation key, indexes and cross-institution behaviour |
| D06-C | What may a scheme contain, and is a check digit required? | Validation, allow-listed segments and algorithm choice |
| D06-D | How is a number allocated without `max + 1`? | Reservation mechanics, concurrency and idempotency |
| D06-E | What happens at exhaustion, and how is the scheme widened? | Alerting, refusal code and expansion without renumbering |
| D06-F | How is the full number disclosed, if at all? | Contracts, masking, reveal permission and audit |

## 2. Why the answer is needed now

- Slice 8 is the first slice that creates `AccountNumberReservation` rows and assigns the number an account keeps for
  its lifetime; a wrong scheme cannot be corrected later without renumbering customers.
- Account invariant 3 states the number is unique within the approved institution/scheme scope and assigned once, so
  both the scope and the "once" rule must be concrete.
- Every published contract today exposes only `MaskedAccountNumber`; nothing defines what a full number is, how it is
  generated or who may see it.
- `AccountLifecycleV1` already carries `AccountId`, `MaskedAccountNumber` and `Version`, so the scheme must not change
  those shapes; it must supply the value behind the mask.

## 3. Current implementation state

| Capability | State | Evidence |
| --- | --- | --- |
| Account contracts | Transport-only records exist: `CreateAccountApplicationRequestV1` has no number field, responses carry `MaskedAccountNumber` | `AccountLifecycleV1.cs` |
| Account-number scheme | **Not implemented.** No scheme, sequence, check digit or reservation type exists in Core Ledger | grep over `CoreBanking.CoreLedger.*` |
| Institution configuration lifecycle | Effective-dated, versioned, maker-checked configuration areas exist and are exercised end to end by `product_eligibility.v1` and `institution.currencies` | `InstitutionCurrencyConfigurationService`, P1-E04-D17 |
| Institution and branch identity | Institution, branch and region records exist with lifecycle and status | Institution module |
| Permission grammar | `domain.resource.action` — three segments, no exceptions | `PermissionIdentifier` |

## 4. Recommended position

### D06-A Format ownership

| Item | Recommendation |
| --- | --- |
| Owner | Institution, through a new effective-dated configuration area `institution.account-numbering` |
| Lifecycle | The approved configuration lifecycle: draft, simulate against reserved synthetic numbers, submit, approve and publish; maker may not approve |
| Rationale | Account numbers are an institution and regulatory concern, not a platform constant; the existing lifecycle already provides versioning, no-overlap publication and audit |
| Platform floor | The platform registers the *vocabulary* of segments and algorithms; it never ships a default scheme, because a default would be an invented number format |

### D06-B Uniqueness scope

| Item | Recommendation |
| --- | --- |
| Scope | Unique within `(institution, scheme)`; no global uniqueness claim across institutions |
| Scheme instance | A published scheme has an id and a version; numbers issued under earlier versions stay valid and are not reissued |
| Index | Unique index on `(institution_id, scheme_id, number)`; a separate unique index on `(institution_id, number)` when the institution declares a single active scheme, so the scope cannot degrade silently |
| Disclosure | Two institutions may hold the same digits. Any cross-institution lookup must carry the institution, never search by number alone |

### D06-C Scheme content and check digit

Allow-listed, code-registered segments only. No template string, no regular expression from configuration and no
executable expression — the same rule that already forbids arbitrary rule blobs.

| Element | Recommendation |
| --- | --- |
| `prefix` | Optional fixed numeric prefix, 0-6 digits, institution-wide or per product family |
| Product/branch segment | Optional, taken from an approved code list (`product.code` or `branch.code`) with a declared width |
| `sequenceWidth` | 6-12 digits, zero-padded |
| `checkDigit` | One of `none`, `mod10_luhn`, `mod11_weighted_2_1`; any other algorithm requires a new platform release |
| `presentation` | Masking rule: keep the last four digits, mask the rest, and never include the check digit in the mask tail |
| Prohibited | Sequential numbers exposed as identity, digits derived from customer or national identifiers, and any scheme whose number encodes personal data |

Recommended check digit default: `mod10_luhn` when a scheme wants self-validation, `none` when the institution's
regulator prescribes a format without one. A `none` scheme must not be presented as validated, and the reservation
service must not claim verification it did not perform. **The concrete algorithm per institution is the owner's
choice; this brief only fixes the allow-list and the requirement that a check digit is verified wherever it exists.**

### D06-D Allocation

| Item | Recommendation |
| --- | --- |
| Reservation record | `AccountNumberReservation` per `(institution, scheme)` holding `NextValue`, `Version` and a `HighWaterMark` |
| Concurrency | One row updated under `SELECT ... FOR UPDATE` inside the reservation transaction; the optimistic version is also carried so a stale client cannot allocate |
| Ranges | Optional block allocation (`blockSize`) for throughput, with unused blocks reclaimed only by an audited job and only above the allocated high-water mark |
| Idempotency | Reservation keyed by `(institution, requestId)` with the payload hash, so a retried request returns the same number |
| Release | A reservation that never became an account may be released only by an explicit audited action; a released value is never reissued, it is recorded in a `retired` list so the scheme stays gap-detectable |
| Prohibited | `max(number) + 1`, random retry, GUID fragments, and number generation in the API layer |

### D06-E Exhaustion and expansion

| Item | Recommendation |
| --- | --- |
| Thresholds | Alerts at 80%, 90% and 95% of `10^sequenceWidth`; one alert per threshold per scheme version |
| Behaviour at 100% | Refuse with `account_number_scheme_exhausted`; the application stays `Approved` and unsassigned, it is not failed and no partial account row is written |
| Expansion | A new published scheme version with a wider width or new prefix; existing numbers are never renumbered and both schemes stay effective for their issued numbers |
| Depletion visibility | The count of available numbers and the oldest reserved-but-unused number appear in the reconciliation report |

### D06-F Disclosure

| Item | Recommendation |
| --- | --- |
| Default | Contracts keep `MaskedAccountNumber`; the full number never appears in a list response, an event or a log |
| Reveal | `account.number.reveal` (three segments, institution-scoped, step-up required) returns exactly one account's full number with a mandatory reason |
| Audit | Every reveal writes an audit row with actor, permission, reason, correlation id and account id; the number itself is not written to the audit row |
| Events | `AccountOpenedV1` keeps the masked number; downstream services address the account by `AccountId` |

## 5. Constraints

- Do not ship a default scheme, a default width or a default prefix; a missing scheme blocks allocation and therefore
  account opening for that institution.
- Do not reuse a retired number, and do not renumber an account when a scheme version changes.
- Do not put the full number in contracts, events, telemetry or reconciliation output.
- Do not allocate numbers outside the configuration lifecycle, and do not let a draft scheme allocate.
- This decision approves the scheme model only. It does not approve an approval/activation sequence (`P1-E04-D09`) or
  a duplicate-accounting rule (`P1-E04-D07`).

## 6. Deferred for named approval

| Item | Owner | Why it is not decided here |
| --- | --- | --- |
| The actual scheme each pilot institution publishes (prefix, width, check digit) | Institution/Core operations | Institution policy; the platform supplies vocabulary, not values |
| Regulatory notice of the number format | Compliance | Jurisdiction specific |
| Migration of numbers from an existing core banking system | Institution/Core operations | Not in Phase 1 scope; the retired-number list is the only concession made here |
| Throughput sizing of block allocation | Platform | Depends on `P1-E04-D16` targets |

## 7. Sign-off

| Role | Name | Decision | Date |
| --- | --- | --- | --- |
| Project owner | | Pending: approve, amend or reject the positions in §4 | |
| Institution/Core operations | | Pending: confirm the configuration-area ownership and reveal permission | |
| Architecture | | Pending: confirm the allow-listed segment vocabulary and reservation mechanics | |

## 8. Implementation notes (for slice 8)

1. Add `institution.account-numbering` to the configuration area catalogue with a typed validator for the segment
   vocabulary in §4C, and refuse unknown segments or algorithms at draft and again at publication.
2. Create `AccountNumberReservation` with a `Version` concurrency token and a unique index on `(institution, scheme)`;
   allocate inside the same transaction that creates the account.
3. Emit `account_number_scheme_exhausted`, `account_number_scheme_missing` and `account_number_reservation_conflict`
   as stable codes, and add the depletion figures to the configuration reconciliation report.
4. Add a reveal endpoint with `account.number.reveal`, step-up and a mandatory reason, plus an audit row that excludes
   the number.
5. Prove with tests: two concurrent applications get distinct numbers; a retried request gets the same number; a
   rejected application leaks nothing; an exhausted scheme refuses without a partial account; a reveal writes audit
   evidence and a list response cannot expose a full number.
