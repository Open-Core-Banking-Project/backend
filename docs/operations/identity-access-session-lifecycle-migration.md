# Identity and Access session lifecycle and security lock migration

Migration: `20261027150150_AddSessionLifecycleAndSecurityLocks`

Scope: P1 identity session lifecycle and security-lock schema. ADR-0004 (database per service and
tenant isolation), ADR-0008 (authentication provider token model) and ADR-0010 (audit evidence).
Procedure: `docs/operations/database-migrations.md`. Privileges:
`docs/operations/database-privileges.md`.

## Data impact

`identity.sessions` was created without the lifecycle columns the authentication services already
use. The migration adds:

| Column | Type | Rule |
| --- | --- | --- |
| `status` | `text` | Not null; one of `Active`, `Refreshed`, `Revoked`, `Compromised`, `Expired` |
| `last_activity_at` | `timestamptz` | Not null |
| `provider_session_fingerprint` | `varchar(64)` | Not null; unique per institution |
| `concurrency_version` | `bigint` | Not null; `>= 0`; optimistic concurrency token |
| `provider_revocation_requested_at` | `timestamptz` | Nullable |
| `revocation_event_id` | `text` | Nullable |

It also creates `security.locks` (`institution_id`, `security_lock_id`, `identity_id`, `scope`,
`reason_code`, `created_at`, `expires_at`, `released_at`) with a tenant-scoped primary key, an index
on `(institution_id, identity_id, scope)`, and checks that a lock cannot expire or be released before
it was created. No foreign key to `identity.identities` is declared: lock evidence must survive an
identity delete, matching `security.authentication_events`.

## Backfill

Existing rows were created before sessions were bound to a provider session, so each is classified
from evidence it already carries:

- `status` = `Revoked` when `revoked_at` is set; otherwise `Expired` when `expires_at <= now()`;
  otherwise `Active`.
- `last_activity_at` = `COALESCE(revoked_at, issued_at)`.
- `concurrency_version` = `0`, because no optimistic write has been recorded for the row.
- `provider_revocation_requested_at` and `revocation_event_id` stay null. Nothing in the domain
  proves a provider revocation was requested for a legacy row, so the migration does not invent one.
- `provider_session_fingerprint` = `'legacy:'` plus the first 32 hexadecimal characters of
  `sha256(institution_id || ':' || session_id)`.

A real fingerprint is the 64-character lowercase hexadecimal SHA-256 digest produced by
`IdentityLifecycleService.FingerprintProviderSubject`, so a `legacy:` marker can never equal a real
provider fingerprint, can never match a provider session, is unique per row (keeping the
tenant-unique index satisfiable) and stays inside the column width. The marker is deterministic: a
re-applied backfill produces the same value.

## Consequences for legacy sessions

A legacy session cannot prove continuity with the provider. The existing refresh path compares the
confirmed provider fingerprint with the stored one and, on a mismatch, marks the session
`Compromised` and revokes it. For a legacy row that is deliberate fail-closed behaviour, not an
accident of the marker: the platform will not extend a session whose provider binding it never
recorded. Expect legacy sessions to require re-authentication. Do not rewrite a marker to a value
that looks like a real fingerprint.

A session whose own expiry has elapsed is also refused before any state change, so a refresh cannot
resurrect a session the platform has stopped honouring.

## Lock uniqueness and the maintenance sweep

Migration `20261027174230_AddActiveSecurityLockUniqueness` enforces **one unreleased lock per
institution, identity and scope** with the partial unique index `ux_locks_unreleased_scope`
(`WHERE released_at IS NULL`), replacing the non-unique index on the same columns. A predicate cannot
evaluate expiry, so "currently locked" is expressed as "unreleased":

- Acquisition closes a lapsed lock at its expiry instant before inserting the new one, so an expired
  lock that nobody released cannot block reacquisition or violate the index.
- Recording a lock while one is still active keeps the existing lock, returns its identifier and
  writes the attempt as authentication evidence, so a duplicate cannot be created by a retry.
- `SessionSecurityService.ReleaseExpiredLocksAsync` closes lapsed locks at the expiry instant in
  batches, and `ExpireLapsedSessionsAsync` moves sessions whose window has passed from `Active` or
  `Refreshed` to `Expired`, writing one authentication event per transition.
- `SessionLifecycleWorker` runs both passes in the worker process. Configuration section
  `IdentityLifecycle`: `SweepIntervalSeconds` (default 300) and `SweepBatchSize` (default 200). Each
  pass drains until a batch is not full, so a backlog clears without one unbounded transaction.
- Listing reports a session whose expiry has elapsed as `expired` even before the sweep records the
  transition, so a caller never sees a lapsed session described as active.

Both passes read across the tenant boundary (`IgnoreQueryFilters`) because they are system
maintenance, following the outbox relay precedent; no request path may do this. A migration
validation step closes lapsed locks and then fails with a count if a genuine duplicate remains, so
ambiguity is surfaced instead of resolved by guesswork.

## Validation

The migration validates the backfill in the database before tightening the schema and fails with the
offending counts. It refuses to guess about dirty data; see the diagnosis section of
`docs/operations/database-migrations.md`.

## Rollout

1. Back up `identity_access_db` and verify the backup.
2. Apply the migration with `identityaccess_migrator`.
3. Run the session lifecycle and lock verification queries in `docs/operations/database-migrations.md`.
4. Deploy the Identity and Access build that reads the new columns and writes locks.
5. Run `scripts/db-verify.ps1`: `SessionSecurityDatabaseTests` covers creation, refresh, reuse
   detection, expiry, listing, revocation and its idempotency, provider revocation requests,
   concurrency, tenant isolation, and lock creation, expiry, release and reacquisition.

## Rollback and forward fix

`Down` drops the six columns and the lock table, so backfilled lifecycle state is lost and legacy
rows return to their pre-migration shape. The synthetic fingerprint and derived status cannot be
reversed into the absent columns that preceded them; only the schema is reverted. Once a deployment
has written real lifecycle state, prefer an append-only forward-fix migration and preserve
`security.locks` evidence.
