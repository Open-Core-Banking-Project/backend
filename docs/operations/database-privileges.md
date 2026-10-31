# Database privileges

How each service's database is exposed to its own principals, and how to verify the result.
Decision: ADR-0004 (database per service and tenant isolation). The migrations that implement this
are the `AddApplicationPrincipalPrivileges` migration in each service's Infrastructure project.

## Two principals per database

| Principal | Purpose | What it may do |
| --- | --- | --- |
| `<service>_migrator` | Owns the database and its schemas; used by `dotnet ef database update` and by deployment | `USAGE` and `CREATE` on the service schemas, DDL, ownership of every object it creates, `CONNECT` |
| `<service>_app` | The runtime principal the service connects with | `CONNECT`, `USAGE` on the service schemas, `SELECT`/`INSERT`/`UPDATE`/`DELETE` on data tables, `USAGE`/`SELECT` on sequences, `EXECUTE` on functions. Append-only tables: `SELECT` and `INSERT` only. No DDL, no ownership, no role membership |

`deploy/compose/postgres/init/01-create-databases.sh` creates both principals for local development
with `NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT` and no inherited membership. Production
provisioning must produce the same attributes; nothing in the repository grants `SUPERUSER`,
`CREATEDB`, `CREATEROLE`, `BYPASSRLS` or database ownership to an application principal, and nothing
grants role membership to it.

The application principal is never a member of the migration principal. Membership would let a
compromised application `SET ROLE` and change the schema, so it is refused even where a deployment
would find it convenient.

## Service databases and schemas

| Service | Database | Mutable schemas | Append-only schema |
| --- | --- | --- | --- |
| Core Ledger | `core_ledger_db` | `institution`, `product`, `integration` | `audit` |
| Customer Onboarding | `customer_onboarding_db` | `platform`, `integration` | — |
| Identity and Access | `identity_access_db` | `identity`, `access`, `security`, `platform`, `integration` | `audit` |
| Payments and VTU | `payments_vtu_db` | `platform`, `integration` | — |
| Channels and Engagement | `channels_engagement_db` | `platform`, `integration` | — |
| Operations and Workflow | `operations_workflow_db` | `platform`, `workflow`, `integration` | — |
| Risk and Reporting | `risk_reporting_db` | `platform`, `integration` | — |
| Integration Hub | `integration_hub_db` | `platform`, `partner`, `provider`, `integration` | — |
| Audit and Reconciliation | `audit_reconciliation_db` | `platform`, `integration` | `audit` |

Schema responsibilities are bounded contexts, not sharing mechanisms. `integration` holds the
outbox, inbox, idempotency and dead-letter tables that every service needs for ADR-0007;
`platform` holds the institution boundary record; `audit` holds evidence. A service reads and writes
only its own database, and only the Core Ledger context authorises balance changes.

## Exceptions to plain read/write

- `audit.*` is append-only. The application principal holds `SELECT` and `INSERT`; `UPDATE`,
  `DELETE` and `TRUNCATE` are revoked, so a compromised application cannot rewrite evidence.
  `audit.events`, `audit.authorisation_events`, `audit.operator_actions` and
  `audit.institution_audit_events` are in this class.
- No service declares a sequence or a stored function today. The privilege migrations still set
  `USAGE, SELECT` on sequences and `EXECUTE` on functions so a future object does not silently break
  the runtime principal, and `db-verify.ps1` fails if a data table arrives without privileges.
- Privileges are per service database. A service's application principal is denied `CONNECT` on
  every other service's database, so a cross-service query fails before it reaches a table.

## Existing objects versus default privileges

Both halves are required, and neither substitutes for the other:

- `GRANT ... ON ALL TABLES IN SCHEMA ...` covers the objects that exist when the migration runs.
  A database that was migrated before privileges were declared has tables with no application
  grants, and `ALTER DEFAULT PRIVILEGES` does not repair that.
- `ALTER DEFAULT PRIVILEGES FOR ROLE <service>_migrator IN SCHEMA ...` covers objects the migration
  principal creates later. It applies only to objects created **after** the statement, only for the
  named role, and only for the object type named in the statement — tables, sequences and functions
  are separate grants.

The `AddApplicationPrincipalPrivileges` migrations therefore grant the existing objects, install the
defaults, and revoke from `PUBLIC` in one reviewed change. The statements are guarded by a
`pg_roles` lookup: on a cluster without the two-principal layout the migration records a notice and
changes nothing rather than failing the deployment.

Revoking from `PUBLIC` is not sufficient on its own. An object owner, a role the application is a
member of, or a direct grant still confers the privilege, which is why verification asks PostgreSQL
for *effective* privileges rather than reading grant statements.

## Verify effective privileges

The repository asserts them in `tests/CoreBanking.SecurityTests`, run by
`scripts/db-verify.ps1` after every database is migrated. Each test connects as the application
principal and asks PostgreSQL:

```sql
SELECT nspname, has_schema_privilege(current_user, nspname, 'USAGE'),
       has_schema_privilege(current_user, nspname, 'CREATE')
FROM pg_namespace WHERE nspname = ANY (ARRAY['identity','access','security','platform','integration','audit']);

SELECT c.relname,
       has_table_privilege(current_user, c.oid, 'SELECT'),
       has_table_privilege(current_user, c.oid, 'INSERT'),
       has_table_privilege(current_user, c.oid, 'UPDATE'),
       has_table_privilege(current_user, c.oid, 'DELETE'),
       has_table_privilege(current_user, c.oid, 'TRUNCATE')
FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE n.nspname = 'audit' AND c.relkind = 'r';

SELECT r.rolsuper, r.rolcreatedb, r.rolcreaterole, r.rolbypassrls,
       pg_has_role(current_user, 'identityaccess_migrator', 'member'),
       (SELECT count(*) FROM pg_namespace WHERE nspowner = r.oid),
       (SELECT count(*) FROM pg_class WHERE relowner = r.oid)
FROM pg_roles r WHERE r.rolname = current_user;

SELECT datname, has_database_privilege(current_user, datname, 'CONNECT')
FROM pg_database WHERE datname = ANY (ARRAY['core_ledger_db','identity_access_db', /* … */]);
```

Expected: `USAGE` true and `CREATE` false on every service schema; `SELECT`/`INSERT`/`UPDATE`/
`DELETE` true and `TRUNCATE` false on data tables; `SELECT`/`INSERT` true and `UPDATE`/`DELETE`/
`TRUNCATE` false on audit tables; every role attribute false, no membership of the migration role, no
schema or object ownership; `CONNECT` true only for the service's own database.

For a manual check in one database:

```sql
SET ROLE identityaccess_app;
SELECT has_table_privilege(current_user, 'identity.sessions', 'UPDATE');
CREATE TABLE identity.probe (id integer);   -- must fail with 42501
RESET ROLE;
```

Local verification:

```powershell
powershell -File scripts/infra.ps1 -Action up
powershell -File scripts/db-verify.ps1
dotnet test tests/CoreBanking.SecurityTests --configuration Release --filter Category=Database
```

## Deploy and roll back privilege changes

Deploying:

1. Read the generated SQL with `migrations script` and confirm it contains no `GRANT` to `PUBLIC`,
   no `WITH GRANT OPTION`, no `SUPERUSER`, and no default privileges granted to a role other than the
   service's own application principal.
2. Apply with the migration principal, then run the effective-privilege queries above against the
   deployed database before starting the new service build.
3. Deploy the service and confirm its health checks and its own database tests pass.

The `Down` of every privilege migration removes the defaults and the grants it installed. It is a
deployment rollback aid, not a data operation:

- Reverting a privilege migration leaves the application principal unable to read its own data. Roll
  back the service build first, or expect the service to fail until `Up` is applied again.
- Revoking `CONNECT` from `PUBLIC` is deliberately not reversed, because restoring it would widen
  access on a running cluster. A rollback to the previous topology is an explicit operator decision.
- Removing a grant does not remove a privilege obtained through ownership or role membership. If an
  application principal is ever found to be an owner or a member, fix that directly; the migration
  statements will not do it.

## Relationship to database integration tests

`scripts/db-verify.ps1` runs the database tests as the application principal on purpose, so a missing
grant surfaces as a failed test rather than as a production permission error. If a test project is
added for a service, it is picked up automatically because the script runs
`Category=Database`/`Category=Integration` for every service test project it finds.
