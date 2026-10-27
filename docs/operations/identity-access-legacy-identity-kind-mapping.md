# Identity and Access legacy identity kind mapping

Repair procedure for a local development database that was created during Phase 0 and therefore
holds identities without an `identity_type`, `concurrency_version` or institution membership.

Related: `docs/operations/database-migrations.md`, `docs/operations/database-privileges.md`, and the
migration `20261026215317_AddIdentityMembershipAndProviderLinks`, which deliberately refuses to invent
a kind.

## Symptom

`dotnet ef database update` or `scripts/db-verify.ps1` fails with:

```
P0001: Identity kind mapping is required before applying AddIdentityMembershipAndProviderLinks.
```

The migration raises before it adds `identity_type` and `concurrency_version`, because the columns do
not exist yet and choosing an identity kind changes what the identity may do. The guard fires when
`identity.identities` contains **any** row, so the mapping is an export → empty → migrate → import
sequence, not an update in place.

## The Phase 0 walking-skeleton fixture

`scripts/walking-skeleton-verify.ps1` seeds one fixture identity per local environment:

| Column | Value |
| --- | --- |
| `institution_id` | `phase0-walking-skeleton` |
| `identity_id` | `phase0-admin` |
| `actor_type` | `Human` |
| `display_name` | `Phase 0 Administrator` |

Its kind is **`Administrator`** by construction, not by guesswork: the fixture exists only to hold
`platform.foundation.execute` so the walking skeleton can create one foundation record, and its role
is `phase0-administrator`. Any other legacy identity must be mapped from evidence in the identity
provider or the access records before this procedure is used; do not infer a kind from a display
name alone.

## Procedure (local development only)

Never run this against a shared or production database.

1. Back up the database and keep the export of the rows you are about to remove:

```powershell
powershell -File scripts/infra.ps1 -Action backup -Database identity_access_db
# POSTGRES_ADMIN_PASSWORD comes from deploy/compose/.env
docker exec -e "PGPASSWORD=<admin password>" corebanking-postgres pg_dump --data-only --inserts --no-owner --no-privileges --username cb_admin --dbname identity_access_db --table=identity.identities --table=access.roles --table=access.role_permissions --table=access.identity_roles --table=access.entitlement_versions > artifacts/backups/identity-access-legacy-identities.sql
```

2. Remove the legacy identities. `access.identity_roles` and `access.entitlement_versions` cascade;
   `audit.operator_actions` and `audit.authorisation_events` hold evidence by value and are left in
   place as history:

```sql
DELETE FROM identity.identities
WHERE institution_id = 'phase0-walking-skeleton' AND identity_id = 'phase0-admin';
```

3. Apply the migrations with the migration principal:

```powershell
dotnet tool run dotnet-ef database update `
  --project src/Services/IdentityAccess/src/CoreBanking.IdentityAccess.Infrastructure `
  --startup-project src/Services/IdentityAccess/src/CoreBanking.IdentityAccess.Infrastructure `
  --context IdentityAccessDbContext --configuration Release
```

4. Re-import the fixture with its mapped kind, its membership and its access records. This is the
   same seed `scripts/walking-skeleton-verify.ps1` applies, so the two stay aligned:

```sql
INSERT INTO identity.identities
    (institution_id, identity_id, actor_type, identity_type, display_name, status, created_at, updated_at, concurrency_version)
VALUES
    ('phase0-walking-skeleton', 'phase0-admin', 'Human', 'Administrator', 'Phase 0 Administrator', 'Active', now(), now(), 1);

INSERT INTO identity.institution_memberships
    (institution_id, membership_id, identity_id, status, created_at, updated_at, concurrency_version)
VALUES
    ('phase0-walking-skeleton', 'phase0-admin-membership', 'phase0-admin', 'Active', now(), now(), 1);

INSERT INTO access.identity_roles
    (institution_id, identity_id, role_id, region_id, branch_id, channel_id, valid_from, valid_to, assigned_by)
VALUES
    ('phase0-walking-skeleton', 'phase0-admin', 'phase0-admin-role', null, null, null, now() - interval '1 minute', null, 'phase0-admin');

INSERT INTO access.entitlement_versions (institution_id, identity_id, version, updated_at)
VALUES ('phase0-walking-skeleton', 'phase0-admin', 1, now());
```

5. Verify and continue:

```powershell
powershell -File scripts/db-verify.ps1 -Configuration Release
powershell -File scripts/walking-skeleton-verify.ps1 -Configuration Release
```

```sql
SELECT institution_id, identity_id, identity_type, status, concurrency_version FROM identity.identities;
SELECT count(*) FROM identity.institution_memberships;
SELECT count(*) FROM access.identity_roles;
SELECT count(*) FROM access.entitlement_versions;
```

## Alternatives

- `scripts/infra.ps1 -Action reset -Force` followed by `-Action up` gives the same result for a local
  environment that holds nothing worth keeping. It destroys every local database volume.
- A production installation with real identities needs the approved import path for its identity
  provider: map provider subjects to identities, record the kind from the authoritative source, and
  re-create memberships and access records. Do not use the shortcut above.

## Why the fixture must be re-created rather than updated

The guard runs before the new columns exist, so there is no state in which a legacy row can carry a
kind while the migration is still pending. Re-creating the fixture also proves the new model is
complete: the identity needs a membership, a positive `concurrency_version`, and a kind inside
`ck_identities_identity_type`, all of which the re-import exercises.
