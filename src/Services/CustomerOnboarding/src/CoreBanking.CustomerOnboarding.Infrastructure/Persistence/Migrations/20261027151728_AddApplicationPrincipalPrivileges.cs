using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.CustomerOnboarding.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Least-privilege grants for the Customer Onboarding application principal: schema usage, data
    /// manipulation on the schemas it owns, and default privileges for objects the migration
    /// principal creates later. Nothing here grants DDL, role membership or ownership, and PUBLIC
    /// keeps nothing. Decision: ADR-0004.
    /// </summary>
    /// <remarks>
    /// Existing objects are granted explicitly because <c>ALTER DEFAULT PRIVILEGES</c> only affects
    /// objects created afterwards. The grants are skipped with a notice when the principal does not
    /// exist, so the schema can still be built on a cluster without the two-principal layout.
    ///
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
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'customeronboarding_app') THEN
                        RAISE NOTICE 'The customeronboarding_app role does not exist; application privileges for Customer Onboarding were not installed.';
                        RETURN;
                    END IF;

                    -- Schema entry first: without USAGE every object grant below is unreachable.
                    REVOKE ALL ON SCHEMA platform, integration FROM PUBLIC;
                    GRANT USAGE ON SCHEMA platform, integration TO customeronboarding_app;

                    -- Only this service's two principals may open a connection to the database.
                    EXECUTE format('REVOKE CONNECT ON DATABASE %I FROM PUBLIC', current_database());
                    EXECUTE format('GRANT CONNECT ON DATABASE %I TO %I', current_database(), 'customeronboarding_app');
                    EXECUTE format('GRANT CONNECT ON DATABASE %I TO %I', current_database(), 'customeronboarding_migrator');

                    -- Existing objects. Default privileges never reach objects that already exist.
                    GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA platform, integration TO customeronboarding_app;
                    GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA platform, integration TO customeronboarding_app;

                    -- Future objects created by the migration principal.
                    ALTER DEFAULT PRIVILEGES FOR ROLE customeronboarding_migrator IN SCHEMA platform, integration
                        GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO customeronboarding_app;
                    ALTER DEFAULT PRIVILEGES FOR ROLE customeronboarding_migrator IN SCHEMA platform, integration
                        GRANT USAGE, SELECT ON SEQUENCES TO customeronboarding_app;
                    ALTER DEFAULT PRIVILEGES FOR ROLE customeronboarding_migrator IN SCHEMA platform, integration
                        GRANT EXECUTE ON FUNCTIONS TO customeronboarding_app;
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
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'customeronboarding_app') THEN
                        RETURN;
                    END IF;

                    -- Remove the defaults first so re-running Up restores the intended state.
                    ALTER DEFAULT PRIVILEGES FOR ROLE customeronboarding_migrator IN SCHEMA platform, integration
                        REVOKE SELECT, INSERT, UPDATE, DELETE ON TABLES FROM customeronboarding_app;
                    ALTER DEFAULT PRIVILEGES FOR ROLE customeronboarding_migrator IN SCHEMA platform, integration
                        REVOKE USAGE, SELECT ON SEQUENCES FROM customeronboarding_app;
                    ALTER DEFAULT PRIVILEGES FOR ROLE customeronboarding_migrator IN SCHEMA platform, integration
                        REVOKE EXECUTE ON FUNCTIONS FROM customeronboarding_app;

                    REVOKE ALL ON ALL SEQUENCES IN SCHEMA platform, integration FROM customeronboarding_app;
                    REVOKE ALL ON ALL TABLES IN SCHEMA platform, integration FROM customeronboarding_app;
                    REVOKE ALL ON SCHEMA platform, integration FROM customeronboarding_app;
                END
                $migration$;
                """);
        }
    }
}
