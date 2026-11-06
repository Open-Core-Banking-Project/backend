---
artifact: Slice review pack
epic: P1-E02 Identity, Roles and Permissions
slice: 6
snapshot: 2026-11-01
reviewer: project owner (recorded in implementation session)
approved: 2026-11-01
related:
  - ../02-identity-roles-and-permissions.md
  - ../decisions/P1-E02-D08-mfa-and-recovery-methods.md
  - ../slice-ledger.md
---

# Review pack: P1-E02 slice 6 MFA lifecycle and atomic step-up

## Claim under review

Step-up evidence is now single-use under database concurrency, not only during sequential requests. The service:

- reads an unconsumed candidate without tracking it;
- repeats every actor, institution, session, device, purpose and validity condition in one conditional PostgreSQL
  update;
- returns success only when that update changes exactly one row;
- permits exactly one winner when separate API replicas concurrently consume the same evidence.

The same increment now appends authentication events for enrolment start and verified or rejected step-up
outcomes. Event detail is restricted to the stable factor kind or mapped failure code. Tests prove that neither
the submitted factor response nor the opaque provider reference appears in the evidence.

Self-revocation is also implemented through a step-up-protected, actor-scoped endpoint. It accepts only
`user_requested`, `device_lost` or `suspected_compromise`, reaches one terminal state idempotently, invalidates
unused evidence from the factor in the same transaction, records one audit outcome, and allows a replacement
factor to be enrolled. Migration `20261101130009_AddMfaEvidenceInvalidation` adds explicit invalidation fields and
database constraints; it does not overload the meaning of `consumed_at`.

The evidence-invalidation schema change is limited to explicit invalidation metadata. The factor response and opaque
provider reference remain outside this path.

## Evidence

| Check | Result |
| --- | --- |
| Identity/Access test-project build | Passed; 0 warnings, 0 errors |
| Non-database Identity/Access tests | 176 passed, 0 failed |
| Complete Identity database/integration suite | 95 passed, 0 failed against local PostgreSQL |
| Contention case | Two contexts consumed concurrently; exactly one returned true |
| Authentication evidence | Enrolment, verified and rejected events persisted without response/provider-reference leakage |
| Revocation lifecycle | Actor scoping, idempotency, evidence invalidation and re-enrolment passed |
| Migration | `20261101130009_AddMfaEvidenceInvalidation` applied successfully to local PostgreSQL |
| Affected-file formatting/analyzers | `dotnet format --verify-no-changes`; passed |

## Files to review

- `src/Services/IdentityAccess/src/CoreBanking.IdentityAccess.Infrastructure/Identity/MfaService.cs`
- `src/Services/IdentityAccess/src/CoreBanking.IdentityAccess.Api/Program.cs`
- `src/Services/IdentityAccess/src/CoreBanking.IdentityAccess.Contracts/MfaContractsV1.cs`
- `src/Services/IdentityAccess/src/CoreBanking.IdentityAccess.Infrastructure/Persistence/Migrations/20261101130009_AddMfaEvidenceInvalidation.cs`
- `src/Services/IdentityAccess/tests/CoreBanking.IdentityAccess.UnitTests/Persistence/MfaDatabaseTests.cs`
- `docs/implementation/phase-1-banking-foundation/02-identity-roles-and-permissions.md`
- `docs/implementation/phase-1-banking-foundation/slice-ledger.csv`
- `docs/implementation/phase-1-banking-foundation/slice-ledger.md`

## Remaining gates

- The customer channel journey has not been implemented.
- Passkey and push provider delivery remains open.
- Security, Product and Platform named production confirmations in P1-E02-D08 remain open.
- Recovery workflows remain unavailable; this change does not add a support override or recovery shortcut.

## Decision

- [x] Approve this MFA lifecycle increment with the named Slice 6 gates remaining open.
- [ ] Request changes: record instructions below.

Reviewer: Project owner (approval recorded in implementation session)  Date: 2026-11-01

Change request:

> Approved with the named Slice 6 gates remaining open. This approval advances implementation; it does not approve
> production enablement or close the remaining epic gates.

