using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.CoreLedger.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Least-privilege grants for the Core Ledger application principal: schema usage, data
    /// manipulation on the schemas it owns, and default privileges for objects the migration
    /// principal creates later. Nothing here grants DDL, role membership or ownership, and PUBLIC
    /// keeps nothing. Decision: ADR-0004.
    /// </summary>
    /// <remarks>
    /// Existing objects are granted explicitly because <c>ALTER DEFAULT PRIVILEGES</c> only affects
    /// objects created afterwards. The grants are skipped with a notice when the principal does not
    /// exist, so the schema can still be built on a cluster without the two-principal layout.
    /// Append-only evidence tables keep SELECT and INSERT only.
    /// </remarks>
    public partial class AddApplicationPrincipalPrivileges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $migration$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'coreledger_app') THEN
                        RAISE NOTICE 'The coreledger_app role does not exist; application privileges for Core Ledger were not installed.';
                        RETURN;
                    END IF;

                    -- Schema entry first: without USAGE every object grant below is unreachable.
                    REVOKE ALL ON SCHEMA institution, product, integration, audit FROM PUBLIC;
                    GRANT USAGE ON SCHEMA institution, product, integration, audit TO coreledger_app;

                    -- Only this service's two principals may open a connection to the database.
                    EXECUTE format('REVOKE CONNECT ON DATABASE %I FROM PUBLIC', current_database());
                    EXECUTE format('GRANT CONNECT ON DATABASE %I TO %I', current_database(), 'coreledger_app');
                    EXECUTE format('GRANT CONNECT ON DATABASE %I TO %I', current_database(), 'coreledger_migrator');

                    -- Existing objects. Default privileges never reach objects that already exist.
                    GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA institution, product, integration TO coreledger_app;
                    GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA institution, product, integration, audit TO coreledger_app;

                    -- audit is evidence: append only, so a compromised application cannot rewrite history.
                    GRANT SELECT, INSERT ON ALL TABLES IN SCHEMA audit TO coreledger_app;
                    REVOKE UPDATE, DELETE, TRUNCATE ON ALL TABLES IN SCHEMA audit FROM coreledger_app;

                    -- Future objects created by the migration principal.
                    ALTER DEFAULT PRIVILEGES FOR ROLE coreledger_migrator IN SCHEMA institution, product, integration
                        GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO coreledger_app;
                    ALTER DEFAULT PRIVILEGES FOR ROLE coreledger_migrator IN SCHEMA institution, product, integration, audit
                        GRANT USAGE, SELECT ON SEQUENCES TO coreledger_app;
                    ALTER DEFAULT PRIVILEGES FOR ROLE coreledger_migrator IN SCHEMA institution, product, integration, audit
                        GRANT EXECUTE ON FUNCTIONS TO coreledger_app;
                    ALTER DEFAULT PRIVILEGES FOR ROLE coreledger_migrator IN SCHEMA audit
                        GRANT SELECT, INSERT ON TABLES TO coreledger_app;
                END
                $migration$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $migration$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'coreledger_app') THEN
                        RETURN;
                    END IF;

                    -- Remove the defaults first so re-running Up restores the intended state.
                    ALTER DEFAULT PRIVILEGES FOR ROLE coreledger_migrator IN SCHEMA institution, product, integration
                        REVOKE SELECT, INSERT, UPDATE, DELETE ON TABLES FROM coreledger_app;
                    ALTER DEFAULT PRIVILEGES FOR ROLE coreledger_migrator IN SCHEMA institution, product, integration, audit
                        REVOKE USAGE, SELECT ON SEQUENCES FROM coreledger_app;
                    ALTER DEFAULT PRIVILEGES FOR ROLE coreledger_migrator IN SCHEMA institution, product, integration, audit
                        REVOKE EXECUTE ON FUNCTIONS FROM coreledger_app;
                    ALTER DEFAULT PRIVILEGES FOR ROLE coreledger_migrator IN SCHEMA audit
                        REVOKE SELECT, INSERT ON TABLES FROM coreledger_app;

                    REVOKE ALL ON ALL SEQUENCES IN SCHEMA institution, product, integration, audit FROM coreledger_app;
                    REVOKE ALL ON ALL TABLES IN SCHEMA institution, product, integration, audit FROM coreledger_app;
                    REVOKE ALL ON SCHEMA institution, product, integration, audit FROM coreledger_app;
                END
                $migration$;
                """);
        }
    }
}
