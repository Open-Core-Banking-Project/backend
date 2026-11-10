---
artifact: Slice review pack
epic: P1-E02 Identity, Roles and Permissions
slice: 7
increment: Role draft and submission contention protection
snapshot: 2026-11-01
reviewer: project owner (recorded in implementation session)
approved: 2026-11-01
related:
  - ../02-identity-roles-and-permissions.md
  - ../slice-ledger.md
---

# Review pack: P1-E02 slice 7 role contention protection

## Claim under review

Role drafts now carry an explicit optimistic-concurrency version. Draft update, submission and decision application
advance it. A stale update returns `identity.role_concurrency_conflict`; simultaneous submissions are transactionally
resolved so both callers receive the winning request and the database contains one approval request outbox fact.

## Evidence

| Check | Result |
| --- | --- |
| Affected build | Passed; 0 warnings, 0 errors |
| Non-database regression | 177 passed, 0 failed |
| Focused role database suite | 14 passed, 0 failed |
| Contention case | Two contexts submitted simultaneously; one request/hash and one outbox fact persisted |
| Migration | `20261101140355_AddRoleConcurrencyVersion` applied successfully |
| EF model drift | No pending model changes |
| Formatting/analyzers | Passed |

## Remaining gates

- The access-review query/API surface remains open.
- Named Security and Operations production confirmations remain open.

## Decision

- [x] Approve contention protection and continue with the access-review surface.
- [ ] Request changes: record instructions below.

Reviewer: Project owner (approval recorded in implementation session)  Date: 2026-11-01

Change request:

> Approved to continue with the access-review surface.
