---
decision: P1-E04-D10
title: Restriction, freeze and block precedence
epic: P1-E04 Products and Accounts
owners:
  - Legal/Risk
  - Core operations
  - Architecture
required_by: P1-E04-S10
status: Ready for review
brief_date: 2026-11-06
related: [ADR-0015, P1-E04-D11, P1-E04-D13]
decisions: [P1-E04-D10]
---

# Decision brief: control precedence

`EvaluateAccountControlsRequestV1` already exists and the ledger will honour whatever the Account module answers.
The epic forbids treating restriction, block, freeze, hold and lien as synonyms and requires a permissive control to
never cancel a stronger one, but the actual precedence order — the thing that decides whether a debit is allowed —
is unapproved.

## 1. Decision requested

| # | Question | What the answer fixes |
| --- | --- | --- |
| D10-A | Is the evaluation deny-dominant, or can a control grant permission? | The whole combination model |
| D10-B | What is the precedence order across control types? | Deterministic outcome and reason codes |
| D10-C | How are the five concepts kept distinct? | Domain model, storage and event payloads |
| D10-D | How does control version skew behave? | Fail-closed policy for high-risk operations |
| D10-E | How do effective periods and expiry interact with evaluation? | Time semantics and job evidence |
| D10-F | What may a control authorise, and who may release a legal control? | Release authority and approval requirements |

## 2. Why the answer is needed now

- Slice 10 introduces typed restrictions, freezes and blocks and hands the outcome to the ledger. Getting the
  combination wrong allows a posting the law prohibits, or blocks a customer who should be served.
- The contract already returns `Outcome`, `ReasonCodes`, `EffectiveControls` and `ControlVersion`, so the vocabulary
  and ordering have to be fixed before those values are produced.
- `AccountControlsV1` distinguishes restriction type, posting direction, channel scope and transaction scope, and
  carries `PriorityCode` for liens — meaning precedence is already assumed by the contract shape.

## 3. Current implementation state

| Capability | State | Evidence |
| --- | --- | --- |
| Control contracts | Transport-only: create/evaluate restriction, create lien, decision, changed events | `AccountControlsV1.cs` |
| Control domain and storage | **Not implemented** | Core Ledger Products module |
| Ledger control evaluation | **Not implemented**; the Ledger module does not exist in Phase 1 | `P1-E05` |
| Balances as distinct concepts | Contract-only: `AccountBalanceSummaryV1` keeps ledger balance, available balance, holds and liens separate | `AccountLifecycleV1.cs` |
| Approval profiles | Implemented pattern with pinned hashes and maker exclusion | `ApprovalProfiles` |

## 4. Recommended position

### D10-A Combination model

| Item | Recommendation |
| --- | --- |
| Model | Deny-dominant. Every active applicable control contributes to the decision; one prohibitive control denies regardless of any permissive control |
| Permissive controls | Cannot exist. A control never grants an ability the account status does not already allow; there is no "allow list" control type |
| Outcome values | `allow`, `deny`, `review` only. `review` is used when a control requires human release before the operation proceeds, not as a soft allow |
| Determinism | The same control set, control version and evaluation time always yields the same outcome and the same ordered reason codes |
| Reason codes | Sorted and deduplicated; the response reports which controls produced the decision so the ledger can evidence it |

### D10-B Precedence order

Ordered from strongest to weakest; the first applicable type determines the primary reason code, and all applicable
codes are still reported.

| Rank | Control | Meaning | Typical source |
| --- | --- | --- | --- |
| 1 | Legal block | Court order or statutory prohibition, may forbid credits too | Legal |
| 2 | Regulatory freeze | Supervisor instruction, whole account | Risk/Compliance |
| 3 | Institution freeze | Operational freeze approved under policy | Operations |
| 4 | Typed restriction | Posting direction, channel and transaction-type scoped | Operations/Risk |
| 5 | Product policy restriction | Product term or lifecycle rule (for example dormant) | Product |
| 6 | Lien | Does not deny a posting; reduces available balance | Legal/Finance |
| 7 | Hold | Ledger-owned funds reservation; distinct from a lien and never created here | Ledger |
| 8 | Notice/warning | Informational, never changes the outcome | Operations |

Rules:

1. A freeze denies debits and, for a regulatory freeze, credits as well; an institution freeze denies debits by
   default and admits credits so the customer can still be funded.
2. A restriction never denies posting directions outside its declared scope, and never overrides a freeze.
3. A lien changes availability, not allowance: an operation may be `allow` on controls and still fail availability.
4. A hold is created and released by the ledger; the Account module must not create one, and must not model a lien
   as a hold.
5. Two equally ranked controls combine by denial; there is no last-writer-wins.

### D10-C Concept separation

| Concept | Owner | Effect on a posting | Stored as |
| --- | --- | --- | --- |
| Restriction | Account | Denies within direction/channel/transaction scope | `AccountRestriction` with scope value objects |
| Freeze | Account | Denies debits (broad), optionally credits | `AccountFreeze` with approved source and reason |
| Block | Account | Denies a declared operation set (policy, legal, operational) | `AccountBlock` with operation list |
| Lien | Account, enforced by Ledger | Reduces available balance by an explicit amount | `AccountLien` with amount, currency, priority |
| Hold | Ledger | Reserves specific funds for a specific intent | Ledger-owned; not stored by the Account module |

Prohibited: a single generic "status flag", any control type stored as free text, and any control whose release is
implemented as a delete.

### D10-D Version skew

| Condition | Result |
| --- | --- |
| `KnownControlVersion` older than the stored version | `deny` for operations that affect availability or legal state (lien, hold, settlement, closure) with `control_version_stale` |
| Same condition, low-risk read or enquiry | Answer the enquiry with the current version and report the skew in the response, without performing the operation |
| Unknown control version | `deny` with `control_version_unknown` |
| Evaluation source unavailable | `deny` with `control_evaluation_unavailable`; no cached allow |

### D10-E Time semantics

| Item | Recommendation |
| --- | --- |
| Periods | Half-open `[EffectiveFrom, EffectiveTo)` evaluated at the supplied `EvaluationTime` |
| Business time | `EvaluationTime` comes from the caller's business context; the Account module never substitutes wall-clock time |
| Expiry | A control stops applying at its end instant through evaluation only; a job records the `expired` transition once, idempotently by control id |
| Legal controls | A legal block or regulatory freeze may declare `manualReleaseOnly`, which suppresses automatic expiry and requires an authority reference to close |
| History | Expired and released controls remain readable; `Superseded` is used only when a newer control of the same type replaces one deliberately |

### D10-F Authority and release

| Control | Create authority | Release authority |
| --- | --- | --- |
| Legal block | Legal role with authority reference | Legal role with a new authority reference; maker ≠ approver |
| Regulatory freeze | Risk/Compliance role | Risk/Compliance with supervisor instruction reference |
| Institution freeze | Operations with approval | Operations with approval |
| Typed restriction | Operations/Risk with reason code | Operations with approval; reason required |
| Lien | Lien officer with authority reference | See `P1-E04-D11` |

Permission set (three segments): `account.control.create`, `account.control.release`, `account.control.view`,
`account.lien.manage`. Approval profiles `account.freeze.v1` and `account.legal-block.v1` follow the pinned-hash
pattern and exclude the maker.

## 5. Constraints

- Do not introduce a permissive control type, a "soft" outcome or an override that bypasses a denial.
- Do not treat restriction, freeze, block, hold and lien as synonyms, and do not store a control as free text.
- Do not delete a control to release it, and do not let a release lose its authority reference.
- Do not evaluate controls from a cache without the control version, and do not answer a high-risk operation against
  a stale version.
- Do not compute availability here; availability is the ledger's answer, computed from balances and liens.
- This decision approves precedence and control semantics only. It does not approve lien amounts or priority ranking
  beyond ordering (`P1-E04-D11`), dormancy (`P1-E04-D12`) or closure (`P1-E04-D13`).

## 6. Deferred for named approval

| Item | Owner | Why it is not decided here |
| --- | --- | --- |
| Which control types exist as codes (for example `garnishee`, `aml_review`) | Legal/Risk | Code lists are policy; the brief approves the typed mechanism |
| Maximum controls per account and response cap | Platform | Performance target is `P1-E04-D16` |
| Credit admission while frozen | Operations/Risk | Recommended default is admitted for institution freezes only |
| Notice text and customer communication | Compliance | Not a control-semantics question |

## 7. Sign-off

| Role | Name | Decision | Date |
| --- | --- | --- | --- |
| Project owner | | Pending: approve, amend or reject the positions in §4 | |
| Legal/Risk | | Pending: confirm the precedence order and manual-release rule | |
| Architecture | | Pending: confirm deny-dominance, version skew and permission names | |

## 8. Implementation notes (for slice 10)

1. Implement typed control aggregates with an explicit rank table in domain code, not in configuration, so precedence
   cannot be edited at runtime.
2. Implement `EvaluateAccountControls` to return ordered, deduplicated reason codes and the exact control set used.
3. Enforce release as a new transition with an authority reference and maker exclusion.
4. Add an expiry job that records one transition per control and is idempotent by control id.
5. Emit `control_version_stale`, `control_version_unknown`, `control_evaluation_unavailable` and
   `control_release_authority_required` as stable codes.
6. Prove with tests: a restriction inside its scope denies and outside its scope allows; a freeze denies debits while
   a regulatory freeze denies both; a lien does not deny but reduces availability; a stale version denies a lien
   operation; two concurrent releases produce one transition; a legal control without an authority reference is
   refused.
