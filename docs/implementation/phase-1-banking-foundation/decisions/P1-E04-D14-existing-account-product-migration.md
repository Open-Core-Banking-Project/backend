---
decision: P1-E04-D14
title: Existing-account product migration policy
epic: P1-E04 Products and Accounts
owners:
  - Product
  - Legal
required_by: Before migration feature
status: Proposed
brief_date: 2026-11-08
related: [P1-E04-D07, P1-E04-D09, P1-E04-D15, P1-E04-D17]
decisions: [P1-E04-D14]
---

# Decision brief: existing-account product migration

The epic states that "every account is bound permanently to the exact product version used at opening, with controlled
migration to later versions when explicitly approved", and prohibits silently migrating existing accounts. The binding
half is implemented; the migration half does not exist and cannot be designed until Product and Legal say what a
migration is allowed to do to an existing customer's terms.

## 1. Decision requested

| # | Question | What the answer fixes |
| --- | --- | --- |
| D14-A | Is product-version migration part of Phase 1 at all? | Whether any migration slice is scheduled |
| D14-B | What does an existing account keep when a later version publishes? | The account's version, hash and terms identity |
| D14-C | If migration is ever enabled, what must one migration carry? | Effective date, authority, notice, evidence and reversibility |
| D14-D | What does the platform do today when a caller asks to migrate? | The refusal contract and the absence of partial effect |
| D14-E | Which facts must be re-established for a migrated account? | Eligibility, controls, limits, numbering and statement consequences |

## 2. Why the answer is needed now

- Publishing a new product version is already a live capability, so the question "what happens to the accounts on the
  old version" is asked by every publication, not only by a migration feature.
- Slice 15 cannot close the epic: the ledger records `P1-E04-D14` as unresolved, and an acceptance mapping that omits
  version-migration behaviour is incomplete for a product catalogue that supports versioning.
- The posting boundary depends on it. Ledger-account creation, statement content and any future accounting mapping
  reference the product version, so a migration that silently changes the version invalidates committed evidence.

## 3. Current implementation state

| Capability | State | Evidence |
| --- | --- | --- |
| Account bound to the opening version | Implemented: `product_version_id` and `product_content_hash` are stored on the account and never rewritten | `AccountRecord`, `AccountRecordConfigurations` |
| Version identity on contracts | Implemented: lifecycle, summary and statement contracts carry `ProductVersionId` | `AccountLifecycleV1`, `AccountSummaryStatementCommands` |
| Publication lifecycle | Implemented and approved | `P1-E04` slices 3 to 6 |
| Eligibility bound to a version and content hash | Implemented and database-proven | `AccountEligibilityEvaluationRecord`, `AccountApplicationDatabaseTests` |
| Migration command, endpoint or worker | **Not implemented** | No command, aggregate method or migration table |
| Customer notice or consent for changed terms | **Not implemented** | No notice artefact or consent record |
| Migration evidence row | **Not implemented** | No before/after version record exists |

## 4. Recommended position

### D14-A Scope for Phase 1

| Option | Consequence | Recommendation |
| --- | --- | --- |
| A. No migration in Phase 1; accounts keep their opening version | Publication affects new applications only; no terms change ever happens without a product decision | **Recommended** |
| B. Migration available in Phase 1 behind dual control | Needs customer notice, consent, effective dating, ledger consequences and reversibility before the first use | Not in Phase 1; the prerequisites are policy, not engineering |
| C. Migration prohibited permanently | Removes a capability banks need for repricing and remediation | Rejected: it closes a door Phase 2 needs |

Recommendation A keeps the invariant the epic already states, and it is the only option that needs no customer-terms
decision before the account module is complete.

### D14-B What an account keeps

| Item | Rule |
| --- | --- |
| `product_version_id` | Stays the version accepted at approval, for the life of the account |
| `product_content_hash` | Stays the hash of that version's published content |
| Later publications | Reach only applications created after publication |
| Product deactivation | Does not move or close the accounts already on the version |
| Display | Shows the account's version, never the product's current version |

### D14-C What any future migration must carry

| Requirement | Why |
| --- | --- |
| An approved target version and its content hash | Otherwise the migration target is not reproducible |
| A business effective date, not a wall-clock timestamp | Dormancy, statements and interest depend on the business date |
| Dual control with an independent approver | Repricing is a customer-terms change |
| A recorded reason and the approving actor | Audit and complaint handling |
| Customer notice or consent evidence, per the approved policy | Legal exposure |
| A per-account before/after evidence row | Proves which terms applied on which date |
| Cohort selection that is explicit and bounded | No silent bulk update of an institution |
| A defined failure mode that leaves the account on its original version | No partial migration |
| Ledger agreement on the consequences | A version change may change accounting mappings (`P1-E05-D03`) |

### D14-D Today's refusal

| Condition | Behaviour |
| --- | --- |
| Caller submits a migration request | Refused as an unsupported capability; no version field is written and no event is published |
| Caller submits a new product version on an existing account | Refused; the account update path does not accept product identity |
| New version published with accounts on the old version | Succeeds; existing accounts are untouched and their display keeps the old version |

The refusal must be a named code rather than a generic validation failure, so a channel can explain it and operators can
count attempts.

### D14-E Facts to re-establish if a migration is ever approved

Eligibility for the target version, the multiple-account ceiling, the account-number scheme in force, controls and liens
that the new version's rules would treat differently, dormancy thresholds, statement formats and any ledger mapping the
version references. A migration that re-evaluates nothing is a silent terms change.

## 5. Constraints

- Do not rewrite `product_version_id` or `product_content_hash` under any code path.
- Do not let publication or deactivation mutate existing accounts.
- Do not treat a product's current version as the account's version in any response, statement or event.
- Do not build a migration command before the terms, notice and ledger consequences are approved.
- This brief does not approve the account-number scheme (`P1-E04-D06`), the multiple-account rules (`P1-E04-D07`), the
  approval sequence (`P1-E04-D09`) or the accounting mappings (`P1-E05-D03`).

## 6. Deferred for named approval

| Item | Owner | Why it is not decided here |
| --- | --- | --- |
| Notice period and consent mechanism for changed terms | Product/Legal | Jurisdiction-specific customer protection |
| Which version attributes may differ in a migration | Product | Pricing and feature policy |
| Migration pricing and remediation campaigns | Product/Finance | Commercial decision |
| Ledger treatment of a version change | Finance | Depends on `P1-E05-D03` |
| Historical restatement of statements after a migration | Compliance | Records and retention policy |

## 7. Sign-off

| Role | Name | Decision | Date |
| --- | --- | --- | --- |
| Project owner | | Pending | |
| Product | | Pending: confirm no Phase 1 migration and the "new applications only" rule | |
| Legal | | Pending: confirm the notice and consent prerequisites for any future migration | |

## 8. Implementation notes

1. Keep the account's version columns immutable in the aggregate, and assert in a test that no update path changes them.
2. Return a named refusal code for a migration request so the capability is visibly absent rather than silently missing.
3. Cover the publication case with a database test: publish a new version while an account exists on the old one, then
   prove the account still reports the old version in its summary, statement and lifecycle contract.
4. If migration is approved later, one account is one migration: a per-account evidence row, one transaction per
   account, an explicit failure that keeps the original version, and a cohort report that proves the set migrated.
