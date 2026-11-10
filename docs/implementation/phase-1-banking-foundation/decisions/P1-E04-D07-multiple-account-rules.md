---
decision: P1-E04-D07
title: Multiple-account-per-product and per-customer rules
epic: P1-E04 Products and Accounts
owners:
  - Product/Risk
  - Compliance
required_by: P1-E04-S7
status: Approved
brief_date: 2026-11-06
related: [ADR-0015, P1-E04-D06, P1-E04-D08, P1-E04-D09]
decisions: [P1-E04-D07]
---

# Decision brief: multiple-account rules

Slice 7 applies and snapshots account eligibility. Whether a customer may hold a second account of the same product
changes the eligibility answer, the concurrency design and the failure a duplicate request must produce, so it cannot
be left to a hard-coded limit in a handler.

## 1. Decision requested

| # | Question | What the answer fixes |
| --- | --- | --- |
| D07-A | Is the rule platform-wide, product-level or institution-level? | Ownership, versioning and who may change it |
| D07-B | Which account and application statuses count towards a limit? | The counting rule and therefore the eligibility answer |
| D07-C | How is the limit enforced against concurrent applications? | Locking, reservation and idempotency design |
| D07-D | What does a customer see when the limit is reached? | Stable outcome and reason codes |
| D07-E | Is there an exception path, and how is it controlled? | Override permission, approval and audit |

## 2. Why the answer is needed now

- Eligibility rules already exist and are versioned, simulated and published; an account-count criterion is an
  eligibility fact, not a policy branch in code.
- A first account is already open in the journey by the time a second application is submitted, so the rule is
  exercised in Phase 1 acceptance, not only in later products.
- A count-based rule has a race: two applications submitted concurrently both see "no existing account" and both pass.
  Without a decision the implementation either introduces a lock nobody approved or silently allows duplicates.

## 3. Current implementation state

| Capability | State | Evidence |
| --- | --- | --- |
| Eligibility rule engine | Implemented for `product_eligibility.v1`; typed explicit criteria, simulate/submit/approve/publish | `SavingsProductConfigurationService`, `ProductEligibility` |
| Authoritative customer facts | Implemented: status, classification, tier, segment, risk class, EDD, age, type; 60-second ceiling | `CustomerEligibilityFactsV1`, P1-E04-D17 |
| Account aggregate | **Not implemented.** No `Account` rows exist yet, so no count can be taken | Core Ledger Products module |
| Account-count criterion | Not present in the rule schema. The schema is allow-listed, so adding it is a deliberate contract change | `product_eligibility.v1` schema |
| Request-level idempotency | Implemented for product configuration endpoints; the account endpoints do not exist yet | Slice 3-5 evidence |

## 4. Recommended position

### D07-A Where the rule lives

| Item | Recommendation |
| --- | --- |
| Primary home | Product version eligibility rules: a new optional criterion group `existing_accounts` inside `product_eligibility.v1`, so the limit is versioned, simulated, approved and effective-dated with the product |
| Secondary home | An institution-level ceiling in an effective-dated configuration area (`institution.account-opening-policy`) that a product rule may tighten but never loosen |
| Precedence | The stricter of the two applies: institution ceiling ≥ product rule. A product rule that exceeds the institution ceiling is refused at publication |
| Rationale | Product terms belong to the product version; the institution expresses a legal or operational ceiling once instead of repeating it per product |
| Default | A product version with no `existing_accounts` criterion means *no product-imposed limit*; the institution ceiling still applies. An institution with no published ceiling blocks opening, because an absent limit is a missing configuration, not "unlimited" |

### D07-B Counting basis

| Item | Recommendation |
| --- | --- |
| Counted | `Open`, `Active`, `Restricted`, `Frozen`, `Dormant` |
| Not counted | `PendingClosure`, `Closed`, `CancelledBeforeActivation`, `Rejected`, `Ineligible` |
| In-flight | Applications in `Draft`, `EligibilityPending`, `Eligible`, `PendingApproval`, `Approved` count against the same limit for the duration of a short-lived opening claim (see D07-C), so an approved-but-unactivated application cannot be duplicated |
| Dimension | Configurable per rule: per customer per product, per customer per product family, per customer per currency, per customer overall. Default recommendation: per customer per product family |
| Joint ownership | Counted once for each customer party. Phase 1 captures `Individual` only, so joint ownership is recorded as deferred rather than invented |
| Counting evidence | The evaluation snapshot records the counted account ids and their statuses, so a later dispute is answerable from evidence |

### D07-C Concurrency

| Item | Recommendation |
| --- | --- |
| Mechanism | `AccountOpeningClaim` row per `(institution, customer, limitKey)`, acquired inside the eligibility transaction and released when the application reaches a counted or terminal status |
| Acquisition | `SELECT ... FOR UPDATE` on the claim row, then re-read the count in the same transaction; never a read-then-write outside a lock |
| Idempotency | The claim is keyed by `(institution, requestId)`; a replayed request returns the existing application instead of a second claim |
| Expiry | A claim expires after a bounded period and is released by an audited job, so an abandoned draft cannot block a customer permanently |
| Fail-closed | If the claim cannot be acquired within the bounded wait, refuse with `account_opening_claim_busy`; never fall through to an unlocked count |

### D07-D Customer-visible outcome

| Condition | Result |
| --- | --- |
| Count below the limit | Eligible; the snapshot records the counted accounts |
| Count at the limit | `Ineligible` with reason code `account_limit_reached`; no application row is created beyond the recorded evaluation |
| Duplicate submission of the same request | Replay of the first result, not a second refusal |
| Institution ceiling missing | Refuse with `account_opening_policy_unavailable` |
| Count source unavailable | Fail closed with `account_count_unavailable`; no optimistic pass |

### D07-E Exceptions

| Item | Recommendation |
| --- | --- |
| Permission | `account.opening.override` — institution-scoped, step-up, separate permission from `account.application.create` |
| Approval | Requires a second approver, a mandatory reason and an evidence reference; the maker may not approve |
| Ceiling | The override may exceed the product rule but not a hard institution maximum declared in the same configuration area; when that maximum is absent, override is refused |
| Audit | One audit row per override naming the rule, the counted accounts, the reason and the approver |
| Reporting | Overrides appear in the configuration reconciliation report so a pattern is visible, not just the individual case |

## 5. Constraints

- Do not hard-code a limit, and do not treat an absent rule as unlimited when the institution ceiling is missing.
- Do not count `Closed` or `CancelledBeforeActivation` accounts toward a limit.
- Do not add an `existing_accounts` criterion to the schema without making it simulated, approved and effective-dated
  like every other criterion.
- Do not evaluate the count from a cache, a read model or an event payload; the count is read from the authoritative
  account store inside the locking transaction.
- This decision approves the rule and its enforcement only; it does not approve the approval/activation sequence
  (`P1-E04-D09`) or number allocation (`P1-E04-D06`).

## 6. Deferred for named approval

| Item | Owner | Why it is not decided here |
| --- | --- | --- |
| The concrete limit each product family carries | Product/Risk | Product policy; the platform supplies the criterion |
| Joint and beneficiary ownership counting | Product/Legal | Phase 1 captures `Individual` only |
| Whether a customer may hold accounts in two institutions of the same group | Institution/Core operations | Cross-institution visibility is not in Phase 1 scope |
| Product migration interaction | Product/Legal | `P1-E04-D14` remains unresolved |

## 7. Sign-off

| Role | Name | Decision | Date |
| --- | --- | --- | --- |
| Project owner | Recorded in the implementation session | Approved the recommended positions: an `existing_accounts` criterion inside `product_eligibility.v1`, tightened by an `institution.account-opening-policy` ceiling with the stricter rule winning and a missing ceiling blocking opening, the counted status set, a short-lived `AccountOpeningClaim` covering in-flight applications, and per-customer-per-product-family as the default dimension | 2026-11-06 |
| Product/Risk | | Named confirmation required before slice 7 merges | |
| Compliance | | Named confirmation required that the institution ceiling, not the product rule, carries a legal limit | |

## 8. Implementation notes (for slice 7)

1. Extend the `product_eligibility.v1` schema with an optional `existing_accounts` criterion group and refuse a
   product rule that exceeds the published institution ceiling.
2. Add `institution.account-opening-policy` to the configuration catalogue, carrying the ceiling and the override
   maximum, with the same simulate/approve/publish lifecycle as `institution.currencies`.
3. Implement `AccountOpeningClaim` with row locking, an expiry job and idempotency by request id.
4. Emit `account_limit_reached`, `account_opening_claim_busy`, `account_opening_policy_unavailable` and
   `account_count_unavailable` as stable codes, and count them in the eligibility telemetry added in slice 14.
5. Prove with tests: two concurrent applications cannot both pass; a replayed request returns the first result; a
   closed account does not consume the limit; a missing institution ceiling blocks; an override needs a second
   approver and writes one audit row.
