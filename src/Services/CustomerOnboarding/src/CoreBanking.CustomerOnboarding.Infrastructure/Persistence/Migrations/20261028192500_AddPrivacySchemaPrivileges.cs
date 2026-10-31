using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.CustomerOnboarding.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Least-privilege grants for the <c>privacy</c> schema introduced with consent evidence. Default
    /// privileges are per schema, so the new schema is granted explicitly. Consent rows are append-only
    /// because the service has no update or delete path for them, following the same convention as the other
    /// evidence tables. Decision: ADR-0004, P1-E03-D12.
    /// </summary>
    [DbContext(typeof(CustomerOnboardingDbContext))]
    [Migration("20261028192500_AddPrivacySchemaPrivileges")]
    public sealed class AddPrivacySchemaPrivileges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $migration$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'customeronboarding_app') THEN
                        RAISE NOTICE 'The customeronboarding_app role does not exist; privacy schema privileges were not installed.';
                        RETURN;
                    END IF;

                    IF NOT EXISTS (SELECT 1 FROM pg_namespace WHERE nspname = 'privacy') THEN
                        RAISE NOTICE 'The privacy schema does not exist; privacy schema privileges were not installed.';
                        RETURN;
                    END IF;

                    REVOKE ALL ON SCHEMA privacy FROM PUBLIC;
                    GRANT USAGE ON SCHEMA privacy TO customeronboarding_app;

                    GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA privacy TO customeronboarding_app;

                    ALTER DEFAULT PRIVILEGES FOR ROLE customeronboarding_migrator IN SCHEMA privacy
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

                    IF NOT EXISTS (SELECT 1 FROM pg_namespace WHERE nspname = 'privacy') THEN
                        RETURN;
                    END IF;

                    ALTER DEFAULT PRIVILEGES FOR ROLE customeronboarding_migrator IN SCHEMA privacy
                        REVOKE SELECT, INSERT, UPDATE, DELETE ON TABLES FROM customeronboarding_app;

                    REVOKE ALL ON ALL TABLES IN SCHEMA privacy FROM customeronboarding_app;
                    REVOKE ALL ON SCHEMA privacy FROM customeronboarding_app;
                END
                $migration$;
                """);
        }
    }
}
