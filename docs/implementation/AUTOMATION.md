# Implementation automation

## Purpose

This workflow lets a coding agent advance the implementation roadmap one bounded vertical slice at a time while preserving human approval for consequential decisions, epic contracts, completed epics, and phase exit.

The phase and epic documents remain the implementation contracts. The slice ledger remains the source of delivery status. Automation must not reinterpret a draft document as approval or promote its own work to `Approved` or `Verified`.

## Current hand-off point

As of 2026-11-06:

- Phase 0 is the established engineering foundation.
- Phase 1 Epic 3, Customer and Onboarding, is merged on `main` and its epic document is `Implemented`. Its slices were approved on 2026-10-31, although several ledger status cells still describe partial slice history and need evidence reconciliation.
- Phase 1 Epic 2, Identity, Roles and Permissions, has 14 of 15 slices recorded implemented pending review. Slice 5 closed on the approved `P1-E02-D07` platform security floors (2026-11-06); slice 6 already carries a bounded human approval; `P1-E02-D14` and `P1-E02-D15` remain open. Independent epic approval is still pending.
- Phase 1 Epic 4, Products and Accounts, has an approved epic contract and partial implementation. Its slice 6 has no decision blocker, but its recorded note still requires approved customer, branch and currency transport contracts before implementation.
- Frontend Admin is still documentation-only. Frontend User and Frontend Mobile contain only their initial repository documentation. Backend contract readiness therefore controls the next full-stack implementation step.

The controller now selects P1-E04 slice 6 as the first unfinished slice without a decision blocker. A run must confirm the prerequisite transport contracts are approved before editing; if they are not, the correct action is a contract review rather than implementation. Re-evaluate the ledger and repository before acting because the state may have changed.

## State machine

```text
RECONCILE
  -> DECISION_REQUIRED       create/update a decision or review pack; stop for human CTA
  -> CONTRACT_REVIEW         prepare an epic approval pack; stop for human CTA
  -> IMPLEMENT_SLICE         implement one smallest complete eligible slice
  -> SLICE_REVIEW            publish evidence and a review CTA; do not self-approve
  -> EPIC_REVIEW             publish consolidated epic evidence and a review CTA
  -> PHASE_REVIEW            publish phase exit evidence and a review CTA
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
- acceptance evidence is complete and an independent review is required;
- tests expose a safety or invariant failure that cannot be corrected inside the current slice;
- external credentials, production access, destructive changes, or a merge/deployment would be required.

## Run contract

Each automation run performs at most one implementation slice:

1. Read all applicable `AGENTS.md` files, the phase overview, epic document, slice ledger, relevant ADRs/contracts/tests, and this file.
2. Run `scripts/implementation-status.ps1 -Format Json` and independently verify the selected action against the repository.
3. Reconcile stale ledger text before selecting work. Do not treat a progress note as proof that acceptance evidence passed.
4. Implement the smallest complete vertical slice, including tests, migrations, contracts, documentation, and operational evidence required by the epic.
5. Run the narrowest relevant tests and then the affected verification suite. Never report an unrun check as successful.
6. Update the ledger with factual implementation state and evidence. Leave `approval` pending and never set `Approved` or `Verified` on the agent's own authority.
7. Produce a review pack or pull request containing requirement/story traceability, changed files, migrations/contracts, verification commands and results, risks, and rollback/forward-fix notes.
8. End with exactly one human CTA:
   - **Review and approve** when evidence is ready; or
   - **Resolve decision** when implementation is blocked; or
   - **Request changes** when the reviewer should return corrective instructions.

If a pull request exists, the CTA should link directly to it. Otherwise it should link to the repository review pack. A scheduled run must stay quiet when another run is active or when the same unresolved human gate has not changed.

## Human decision semantics

- `Approve <epic-or-slice-id>` authorises advancement past the named review gate only after the reviewer has examined the linked evidence.
- `Request changes <epic-or-slice-id>: <instructions>` returns the item to implementation with explicit corrections.
- `Resolve <decision-id>: <decision>` records product, security, compliance, operations, or architecture direction. Consequential decisions must be written into the applicable decision record or ADR before implementation resumes.
- Approval of code review is separate from permission to merge, deploy, migrate production data, or enable a provider.

## Phase progression

Within a phase, finish or explicitly defer the active epic before starting another epic that depends on it. At epic completion, request independent review. At phase completion, assemble a phase exit pack and stop. The next phase begins only after a human approves the phase exit and the next phase entry criteria.

