---
decision: P1-E04-D09
title: Account approval and activation sequence
epic: P1-E04 Products and Accounts
owners:
  - Operations/Product
  - Architecture
required_by: P1-E04-S9
status: Approved
brief_date: 2026-11-06
related: [ADR-0015, ADR-0009, P1-E04-D06, P1-E04-D07, P1-E04-D08, P1-E04-D13]
decisions: [P1-E04-D09]
---

# Decision brief: approval and activation sequence

Slice 9 hands an opened account to the ledger. The epic allows the institution to choose whether the number is
reserved before approval or at activation, and forbids creating any balance in the process. That choice decides how
many numbers are burned by rejected applications, what a failed ledger initialisation leaves behind and which
permissions exist.

## 1. Decision requested

| # | Question | What the answer fixes |
| --- | --- | --- |
| D09-A | Is the account-number reserved before approval or after it? | Number leakage, transaction boundaries and recovery |
| D09-B | What exactly transforms an application into an `Open` account? | The single transactional step and its evidence |
| D09-C | What must be true before activation, and who may activate? | Preconditions, permissions and the ledger handshake |
| D09-D | Is an automatic approval path allowed for a single-officer institution? | Maker-checker strictness |
| D09-E | What happens when number reservation succeeds but ledger initialisation fails? | Recovery, retry and reconciliation |
| D09-F | Which permission names implement the journey? | The permission catalogue entries and their grammar |

## 2. Why the answer is needed now

- `AccountLifecycleV1` already exposes `AccountResponseV1` with `Status` and `LedgerInitialisationStatus`, and
  `ActivateAccountRequestV1` with only `ApplicationId` and `ExpectedVersion` — the contract assumes the account exists
  before activation, so the ordering must be approved to match it.
- Account invariant 5 forbids creating a balance during opening or activation, so the ledger handshake has to be an
  initialisation that proves absence of money, not a posting.
- A number reserved for an application that is later rejected is either reusable (a security and audit problem) or
  burned (an exhaustion problem). The epic leaves the choice open, so it must be decided with D06.

## 3. Current implementation state

| Capability | State | Evidence |
| --- | --- | --- |
| Account contracts | Transport-only: application create/submit, `ActivateAccountRequestV1`, `AccountResponseV1`, `AccountOpenedV1`, `AccountActivatedV1` | `AccountLifecycleV1.cs` |
| Account aggregate and state machine | **Not implemented** | Core Ledger Products module |
| Workflow approval | Implemented: code-registered approval profiles with a SHA-256 definition hash, exact request/version/hash binding, maker exclusion | `ApprovalProfiles`, `InstitutionConfigurationApprovalDecidedConsumer` |
| Ledger initialisation | **Not implemented.** No ledger account contract exists in Phase 1 | Core Ledger Ledger module |
| Permissions | `PermissionIdentifier` enforces exactly `domain.resource.action`; two-segment names such as the epic's `account.activate` are not expressible | `PermissionIdentifier` |

## 4. Recommended position

### D09-A Ordering

| Step | Recommendation |
| --- | --- |
| 1 | `POST account applications` creates the application in `Draft`; no number exists |
| 2 | Eligibility is evaluated from authoritative facts and snapshotted (`EligibilityPending` → `Eligible` or `Ineligible`) |
| 3 | Submission creates a Workflow approval request bound to the exact application version and eligibility snapshot hash (`PendingApproval`) |
| 4 | The approval decision is applied idempotently; the application becomes `Approved` |
| 5 | **Number reservation, account creation and the `AccountOpenedV1` outbox fact commit in one transaction**, which moves the application to `Open` |
| 6 | Ledger initialisation is requested through a protected ledger contract that creates the ledger account with no balance and returns its status |
| 7 | Activation is a separate authorised transition to `Active`, gated on D09-C |

Recommended answer to the epic's open question: **reserve at approval time (step 5), never at draft time.** A number
reserved before approval leaks identity for an application the bank has not accepted, and a number reserved at
activation can fail after the customer was told the account exists.

### D09-B The transition that creates an account

| Item | Recommendation |
| --- | --- |
| Trigger | Application reaches `Approved` and the number allocation succeeds |
| Atomic unit | Reservation + `Account` row + status history + audit row + outbox fact |
| Idempotency | Keyed by application id and expected version; a replay returns the existing account and never allocates again |
| Prohibited | Creating an `Account` row in `Draft`, creating one before approval, and writing any balance, journal line or hold |
| Recovery | If the transaction fails after reservation, the reservation is released in the same failed transaction; the retired-number rule from D06 applies only to numbers that reached an account row |

### D09-C Activation preconditions

| Precondition | Recommendation |
| --- | --- |
| Ledger state | `LedgerInitialisationStatus = initialised`, proven by a ledger contract response carrying a reference, not by a local flag |
| Facts freshness | Authoritative eligibility facts re-read within the approved 60-second ceiling (`P1-E04-D08`); stale facts fail closed |
| Number scheme | The scheme version that issued the number is still effective for issuing; a scheme superseded for new issuance does not invalidate an issued number |
| Controls | No prohibitive control active at the activation instant |
| Approval | Present and bound to the exact application version |
| Permission | A distinct activation permission held by the activator, not necessarily the approver |
| Prohibited | Activating with `LedgerInitialisationStatus` other than `initialised`, and activating a `CancelledBeforeActivation` application |

### D09-D Maker-checker

| Item | Recommendation |
| --- | --- |
| Default | Maker-checker is mandatory for every Phase 1 account opening, including single-officer institutions |
| Rationale | An account is a financial control boundary; an institution that cannot staff two approvers cannot open accounts through this platform in Phase 1 |
| Activation | May be performed by the approver or a separate officer; the maker of the application may activate only if a policy explicitly allows it, and the default is no |
| Prohibited | Self-approval, approval by the same actor through a different permission, and approval after the application changed |

### D09-E Failure and recovery

| Condition | Result |
| --- | --- |
| Number allocation fails | Application stays `Approved`, response `409` with a stable code; retry is safe |
| Account transaction fails after reservation | Reservation released; no account row; retry re-reserves |
| Ledger initialisation fails | Account stays `Open` with `LedgerInitialisationStatus = failed`; activation refused; a bounded retry job re-drives initialisation and records each attempt |
| Ledger initialisation is unavailable | `Open` + `unavailable`; visible in the reconciliation report with age |
| Activation fails on stale facts | `409 eligibility_facts_stale`; the application returns to a re-evaluable state, never to `Approved` with stale evidence |
| Account row exists without ledger reference past the configured window | Reconciliation finding `account_without_ledger_initialisation` |

### D09-F Permissions

The epic's names are not expressible under the three-segment grammar. Recommended replacements:

| Epic name | Recommended permission | Purpose |
| --- | --- | --- |
| `account.open` | `account.application.create` | Create and submit an application |
| `account.open.approve` | `account.application.approve` | Approve the exact application version |
| `account.activate` | `account.activation.create` | Activate a `Open` account |
| — | `account.application.cancel` | Cancel before activation |

Approval profile: `account.open.v1`, subject `account.application`, permission `account.application.approve`, maker
excluded, decision-time entitlement enforced, published by Core Ledger and mirrored in the source service's contracts
with the same SHA-256 definition hash pattern used by `institution.configuration.publish.v1`.

## 5. Constraints

- Do not create a balance, journal line, hold or posting in any step of this journey.
- Do not activate before the ledger proves initialisation through its own contract response.
- Do not let the Account module write ledger state directly; only protected ledger contracts.
- Do not release a number once an account row exists, and do not reuse a retired number.
- Do not add a two-segment permission; the catalogue grammar is three segments and the epic's names must be renamed.
- This decision approves sequence and preconditions only; it does not approve dormancy, restrictions or closure.

## 6. Deferred for named approval

| Item | Owner | Why it is not decided here |
| --- | --- | --- |
| The ledger account initialisation contract shape | Architecture/Core Ledger | The Ledger module does not exist yet; `P1-E05` owns it |
| Channel-initiated opening without a human approver | Product/Risk | Phase 1 has no approved channel identity flow for opening |
| Activation by the same officer who approved | Operations | Recommended default is no; an institution exception needs its own decision |
| Number of retry attempts and window length | Platform | Depends on `P1-E04-D16` targets |

## 7. Sign-off

| Role | Name | Decision | Date |
| --- | --- | --- | --- |
| Project owner | Recorded in the implementation session | Approved the recommended positions: reserve at approval and never at draft, create the account only from `Approved` in one atomic unit with the outbox fact, prove ledger initialisation from a ledger response rather than a local flag, and treat activation as a separate authorised transition gated on ledger state and the 60-second fact-freshness ceiling | 2026-11-06 |
| Operations/Product | | Named confirmation required before slice 9 merges | |
| Architecture | | Named confirmation required for the permission names and the pinned approval-profile hash | |

## 8. Implementation notes (for slice 9)

1. Implement the application state machine with an explicit transition table; reject any transition not in the table.
2. Reserve the number inside the approval-application transaction, and release on failure within the same unit.
3. Add the ledger initialisation port with `initialise`, `status` and a bounded retry; store the ledger reference and
   status, never a locally derived "assume initialised".
4. Register the four permissions from D09-F and the `account.open.v1` approval profile with its pinned hash, then pin
   the hash in both the Core Ledger and Operations Workflow tests.
5. Emit stable codes: `account_number_allocation_failed`, `ledger_initialisation_failed`, `ledger_initialisation_unavailable`,
   `activation_precondition_failed`, `eligibility_facts_stale`.
6. Add reconciliation findings `account_without_ledger_initialisation` and `approved_application_without_account`.
7. Prove with tests: no balance exists after activation; a replayed approval creates one account; a failed ledger
   initialisation blocks activation; a stale fact set refuses activation; maker cannot approve.
