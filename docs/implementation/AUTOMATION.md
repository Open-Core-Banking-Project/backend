# Implementation automation

## Purpose

This workflow lets a coding agent advance the implementation roadmap one bounded vertical slice at a time. Engineering
completion is determined by executable acceptance evidence rather than a separate human code-review state. Consequential
product, regulatory, security, provider, production, and phase-entry decisions retain their named approval authority.

The phase and epic documents remain the implementation contracts. The slice ledger remains the source of delivery status.
Automation must not reinterpret a draft policy decision as approved, but may mark implementation complete when its stated
acceptance criteria and required automated evidence pass.

## Current hand-off point

As of 2026-11-26:

- Phase 0 is the established engineering foundation.
- Phase 1 Epic 3, Customer and Onboarding, is merged on `main` and its epic document is `Implemented`. Its slices were approved on 2026-10-31, although several ledger status cells still describe partial slice history and need evidence reconciliation.
- Phase 1 Epic 2, Identity, Roles and Permissions, has 14 of 15 slices recorded implemented. Slice 5 closed on the approved `P1-E02-D07` platform security floors (2026-11-06); `P1-E02-D14` and `P1-E02-D15` remain open production decisions.
- Phase 1 Epic 4, Products and Accounts, has an approved epic contract and partial implementation. Slices 6 to 14 are implemented. `P1-E04-D17` approved the customer, branch and currency fact transport contracts, and `P1-E04-D06`, `D07` and `D09` approved the account-numbering, multiple-account and approval/activation rules, all on 2026-11-06, so the account-opening journey is implemented in code. `P1-E04-D10`, `D11`, `D12`, `D13` and `D15` were approved on 2026-11-07. `P1-E04-D16` was approved on 2026-11-21 and its versioned profile/gate is implemented; `P1-E04-D14` remains open.
- Frontend Admin is still documentation-only. Frontend User and Frontend Mobile contain only their initial repository documentation. Backend contract readiness therefore controls the next full-stack implementation step.
- Phase 1 Epic 5 slice 6 is implemented as of 2026-11-24: the atomic posting domain, service,
  balance/movement migration and PostgreSQL commit/replay/rollback evidence pass under the restricted application
  principal. No posting HTTP endpoint is exposed.
- Phase 1 Epic 5 slice 7 is implemented as of 2026-11-24: the protected posting service consumes explicit control
  versions, enforces account controls after deterministic locks and refuses concurrent overspend using the approved
  available-funds formula.
- Phase 1 Epic 5 slice 8 is implemented as of 2026-11-24: create, release and expiry atomically maintain the reserved
  balance component and immutable hold history, while capture commits in the final posting transaction without double
  deduction. Separate-connection contention proves the same hold version cannot be captured twice. No posting or hold
  HTTP endpoint is exposed.
- Phase 1 Epic 5 slice 9 is implemented as of 2026-11-26: `P1-E05-D10` Option B approved full reversal as the default
  with mapping-gated partial reversal that fails closed without an approved event mapping. The protected reversal
  service derives exact opposite lines from authoritative posted rows, locks and validates the original, enforces
  reason/evidence/Workflow maker separation, refuses provider-uncertain evidence, and commits the immutable
  original/reversal link, audit and outbox fact atomically with the reversing posting. Duplicate, concurrent and
  same-key-changed-hash attempts are refused under real PostgreSQL contention. Partial/component reversal, periods and
  the reversal HTTP endpoint remain unimplemented.
- Phase 1 Epic 5 slice 10 is implemented as of 2026-11-26 under the project owner's `P1-E05-D11` Option B approval:
  the published `ledger.manual_journal_policy` drives chart-role class grants, the bounded reason catalogue, the
  compensating-account rule and the amount tiers; an exact-hash draft emits one `approval_requested` fact; the Workflow
  decision consumer records append-only approvals, excludes the maker, applies the Finance-head permission and posts
  through the protected boundary only at quorum. Dual-control break-glass posts with bounded security evidence, and
  PostgreSQL refuses to rewrite or delete posted requests and append-only lines/approvals. No manual-journal HTTP
  endpoint is exposed; partial component reversal, accounting periods and the reversal/manual-journal endpoints remain
  unimplemented.
- Phase 1 Epic 5 slice 11 (accounting periods, date policies and controlled backdated/value-dated posting) is eligible as
  of 2026-11-26: `P1-E05-D12` approved a 90-day ordinary value-date window, a 91-365 day backdate authority with
  step-up and exact approval, refusal beyond 365 days pending a reopen, current-open-period posting with a prior-period
  classification, no future value dates and a mandatory recalculation handoff; `P1-E05-D13` approved four period states
  with an empty soft-close allow-list by default, a one-approver Finance close with balanced/reconciliation/processing/
  outbox evidence, a two-approver maker-checker reopen expiring after five business days with automatic reclose and a
  post-reclose reconciliation, and locked periods reopened only by dual-control break-glass. No period table, state
  machine or date-window enforcement exists yet.
- Phase 1 Epic 5 slice 12 (EOD run graph) remains gated by `P1-E05-D14`.

Slice 15 of P1-E04 is in progress: `scripts/phase1-evidence.ps1` collects the database-free evidence, `04-slice-15-evidence.md` maps every acceptance criterion, `scripts/performance-profile-gate.ps1` enforces the versioned `P1-E04-D16` load result, and `scripts/recovery-evidence-gate.ps1` enforces the fresh-cluster four-hour/zero-loss recovery result. A run must not mark the slice implemented from gate validation or unit tests: it still needs results from the dedicated acceptance environment, the actual recovery mechanisms and drill, the live-stack evidence on the acceptance commit and the end-to-end journey gated by `P1-E04-D14`. A run must still re-evaluate the ledger and repository before acting because the state may have changed.

## State machine

```text
RECONCILE
  -> DECISION_REQUIRED       create/update a decision record; stop for the named authority
  -> IMPLEMENT_SLICE         implement one smallest complete eligible slice
  -> VERIFY_SLICE            run acceptance, migration, security and formatting evidence
  -> RECORD_COMPLETE         record the verified implementation state
  -> PHASE_DECISION          stop only when a named non-engineering phase decision is required
  -> COMPLETE
```

## Eligibility rules

An automation run may implement a slice only when all of these are true:

1. The epic document status is `Approved`, `In Progress`, or `Implemented`, or the user explicitly authorised work from another state in the current task.
2. The slice is `in progress` or `not started`.
3. `blocked_by_decisions` is empty, or every listed decision is recorded as approved in the repository.
4. Required predecessor contracts and migrations exist and are stable enough for the slice.
5. The working tree does not contain overlapping user changes.

The run must stop and request human action when:

- a money, access, privacy, regulatory, provider, recovery, or production decision is unresolved;
- the next epic is `Draft` or `Ready for review` without explicit implementation authorisation;
- tests expose a safety or invariant failure that cannot be corrected inside the current slice;
- external credentials, production access, destructive changes, or a merge/deployment would be required.

## Run contract

Each automation run performs at most one implementation slice:

1. Read all applicable `AGENTS.md` files, the phase overview, epic document, slice ledger, relevant ADRs/contracts/tests, and this file.
2. Run `scripts/implementation-status.ps1 -Format Json` and independently verify the selected action against the repository.
3. Reconcile stale ledger text before selecting work. Do not treat a progress note as proof that acceptance evidence passed.
4. Implement the smallest complete vertical slice, including tests, migrations, contracts, documentation, and operational evidence required by the epic.
5. Run the narrowest relevant tests and then the affected verification suite. Never report an unrun check as successful.
6. Update the ledger with factual implementation state and exact evidence. Mark the slice implemented when all stated
   acceptance gates pass; never convert missing evidence into a completion claim.
7. Record requirement/story traceability, changed files, migrations/contracts, verification commands and results,
   remaining risks, and rollback/forward-fix notes in the epic or evidence pack.
8. Stop for user input only when a named consequential decision, external credential, production action, destructive
   change, merge, or deployment is required.

## Decision semantics

- `Resolve <decision-id>: <decision>` records product, security, compliance, operations, or architecture direction. Consequential decisions must be written into the applicable decision record or ADR before implementation resumes.
- Verified implementation is separate from permission to merge, deploy, migrate production data, or enable a provider.

## Phase progression

Within a phase, finish or explicitly defer the active epic before starting another epic that depends on it. At epic
completion, consolidate the executable evidence. At phase completion, assemble a phase exit pack and stop only for the
named business, security, compliance, operations, or production-readiness decisions that automation cannot establish.

