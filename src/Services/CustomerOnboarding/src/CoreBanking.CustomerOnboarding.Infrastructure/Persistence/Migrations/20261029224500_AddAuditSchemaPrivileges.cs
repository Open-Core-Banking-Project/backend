using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.CustomerOnboarding.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Grants for the append-only <c>audit</c> schema. The application principal may read and append but never
    /// update, delete or truncate audit evidence; the effective-privilege suite asserts exactly that. Other
    /// privileges are deliberately absent so a compromised application path cannot rewrite history.
    /// Decision: ADR-0004, P1-E03-D14.
    /// </summary>
    [DbContext(typeof(CustomerOnboardingDbContext))]
    [Migration("20261029224500_AddAuditSchemaPrivileges")]
    public sealed class AddAuditSchemaPrivileges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $migration$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'customeronboarding_app') THEN
                        RAISE NOTICE 'The customeronboarding_app role does not exist; audit privileges were not installed.';
                        RETURN;
                    END IF;

                    IF NOT EXISTS (SELECT 1 FROM pg_namespace WHERE nspname = 'audit') THEN
                        RAISE NOTICE 'The audit schema does not exist; audit privileges were not installed.';
                        RETURN;
                    END IF;

                    REVOKE ALL ON SCHEMA audit FROM PUBLIC;
                    GRANT USAGE ON SCHEMA audit TO customeronboarding_app;

                    REVOKE ALL ON ALL TABLES IN SCHEMA audit FROM customeronboarding_app;
                    GRANT SELECT, INSERT ON ALL TABLES IN SCHEMA audit TO customeronboarding_app;

                    ALTER DEFAULT PRIVILEGES FOR ROLE customeronboarding_migrator IN SCHEMA audit
                        GRANT SELECT, INSERT ON TABLES TO customeronboarding_app;
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

                    IF NOT EXISTS (SELECT 1 FROM pg_namespace WHERE nspname = 'audit') THEN
                        RETURN;
                    END IF;

                    ALTER DEFAULT PRIVILEGES FOR ROLE customeronboarding_migrator IN SCHEMA audit
                        REVOKE SELECT, INSERT ON TABLES FROM customeronboarding_app;

                    REVOKE ALL ON ALL TABLES IN SCHEMA audit FROM customeronboarding_app;
                    REVOKE ALL ON SCHEMA audit FROM customeronboarding_app;
                END
                $migration$;
                """);
        }
    }
}
