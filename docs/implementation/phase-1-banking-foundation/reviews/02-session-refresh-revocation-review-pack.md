---
artifact: Slice review pack
epic: P1-E02 Identity, Roles and Permissions
slice: 5
snapshot: 2026-11-01
reviewer: pending
approved: false
related:
  - ../02-identity-roles-and-permissions.md
  - ../slice-ledger.md
  - ../../../adr/ADR-0008-authentication-provider-token-model-and-service-identity.md
---

# Review pack: P1-E02 slice 5 refresh and revocation coordination

## Claim under review

This increment closes the local coordination gap around provider-confirmed refresh and reuse detection:

- a confirmed refresh moves the session to `Refreshed`, advances its optimistic concurrency version and appends
  successful `session_refreshed` evidence;
- a provider-session-reference mismatch fails closed, atomically moves the session to `Compromised`, records
  failed `session_refresh_reuse_detected` evidence, and queues durable `session_revoked` provider work;
- provider references and tokens are absent from authentication evidence, the outbox envelope and API contracts;
- existing self-revocation, expiry, lock and tenant-isolation behavior remains intact.

This is not a claim that the whole slice or epic is complete. P1-E02-D07 still requires approved platform
security floors, and the real Keycloak refresh/revocation adapter remains a provider and production gate.

## Files to review

- `src/Services/IdentityAccess/src/CoreBanking.IdentityAccess.Infrastructure/Identity/SessionSecurityService.cs`
- `src/Services/IdentityAccess/tests/CoreBanking.IdentityAccess.UnitTests/Persistence/SessionSecurityDatabaseTests.cs`
- `docs/implementation/phase-1-banking-foundation/02-identity-roles-and-permissions.md`
- `docs/implementation/phase-1-banking-foundation/slice-ledger.csv`
- `docs/implementation/phase-1-banking-foundation/slice-ledger.md`

## Evidence

| Check | Result |
| --- | --- |
| Identity/Access test-project build | Passed; 0 warnings, 0 errors |
| Non-database Identity/Access tests | 174 passed, 0 failed |
| Focused `SessionSecurityDatabaseTests` | 23 passed, 0 failed against local PostgreSQL |
| Solution formatting/analyzer gate | `dotnet format CoreBanking.sln --verify-no-changes --no-restore`; 0 files formatted |
| Schema migration | None required; the session, evidence and outbox fields already exist |

Focused reproduction:

```powershell
dotnet build src/Services/IdentityAccess/tests/CoreBanking.IdentityAccess.UnitTests/CoreBanking.IdentityAccess.UnitTests.csproj --no-restore
dotnet test src/Services/IdentityAccess/tests/CoreBanking.IdentityAccess.UnitTests/CoreBanking.IdentityAccess.UnitTests.csproj --no-build --no-restore --filter "Category!=Database&Category!=Integration"
# With the local application-principal connection configured:
dotnet test src/Services/IdentityAccess/tests/CoreBanking.IdentityAccess.UnitTests/CoreBanking.IdentityAccess.UnitTests.csproj --no-build --no-restore --filter "FullyQualifiedName~SessionSecurityDatabaseTests"
dotnet format CoreBanking.sln --verify-no-changes --no-restore
```

## Reviewer checklist

- [ ] The successful refresh evidence is sufficient and privacy-minimised.
- [ ] Reuse detection commits the compromised state, evidence and provider work atomically.
- [ ] The `session_revoked` version 1 envelope preserves `status: revoked`, with `reasonCode` as an additive field.
- [ ] No provider reference, refresh token or bearer token is persisted or emitted.
- [ ] The remaining P1-E02-D07 and Keycloak integration gates are accurately described and not waived.

## Decision

- [ ] Approve this increment with the named gates remaining open.
- [ ] Request changes: record instructions below.

Reviewer: ____________________  Date: ____________________

Change request:

> Pending reviewer input.

