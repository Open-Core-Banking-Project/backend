using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.CustomerOnboarding.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Least-privilege grants for the <c>customer</c> schema introduced with duplicate detection. Default
    /// privileges are per schema, so a new schema is never covered by the earlier grants. The application
    /// principal keeps the same uniform rights as every other owned schema; a decided or superseded
    /// candidate set is immutable because the service has no delete path for it, not because a grant is
    /// missing (the same convention the append-only revision tables follow). Decision: ADR-0004, P1-E03-D04.
    /// </summary>
    [DbContext(typeof(CustomerOnboardingDbContext))]
    [Migration("20261028141500_AddDuplicateSchemaPrivileges")]
    public sealed class AddDuplicateSchemaPrivileges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $migration$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'customeronboarding_app') THEN
                        RAISE NOTICE 'The customeronboarding_app role does not exist; customer schema privileges were not installed.';
                        RETURN;
                    END IF;

                    IF NOT EXISTS (SELECT 1 FROM pg_namespace WHERE nspname = 'customer') THEN
                        RAISE NOTICE 'The customer schema does not exist; customer schema privileges were not installed.';
                        RETURN;
                    END IF;

                    REVOKE ALL ON SCHEMA customer FROM PUBLIC;
                    GRANT USAGE ON SCHEMA customer TO customeronboarding_app;

                    GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA customer TO customeronboarding_app;

                    ALTER DEFAULT PRIVILEGES FOR ROLE customeronboarding_migrator IN SCHEMA customer
                        GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO customeronboarding_app;
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

                    IF NOT EXISTS (SELECT 1 FROM pg_namespace WHERE nspname = 'customer') THEN
                        RETURN;
                    END IF;

                    ALTER DEFAULT PRIVILEGES FOR ROLE customeronboarding_migrator IN SCHEMA customer
                        REVOKE SELECT, INSERT, UPDATE, DELETE ON TABLES FROM customeronboarding_app;

                    REVOKE ALL ON ALL TABLES IN SCHEMA customer FROM customeronboarding_app;
                    REVOKE ALL ON SCHEMA customer FROM customeronboarding_app;
                END
                $migration$;
                """);
        }
    }
}
