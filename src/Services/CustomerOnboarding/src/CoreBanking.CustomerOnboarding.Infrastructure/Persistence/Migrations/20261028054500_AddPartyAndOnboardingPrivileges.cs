using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.CustomerOnboarding.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Least-privilege grants for the <c>party</c> and <c>onboarding</c> schemas. The earlier
    /// privilege migration only covered the schemas that existed then, and default privileges are
    /// per schema, so the new schemas are granted explicitly. Revisions stay append-only through the
    /// trigger installed with the table, not through the absence of a grant, so the application
    /// principal keeps the same uniform rights as everywhere else. Decision: ADR-0004, epic 03.
    /// </summary>
    [DbContext(typeof(CustomerOnboardingDbContext))]
    [Migration("20261028054500_AddPartyAndOnboardingPrivileges")]
    public sealed class AddPartyAndOnboardingPrivileges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $migration$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'customeronboarding_app') THEN
                        RAISE NOTICE 'The customeronboarding_app role does not exist; party and onboarding privileges were not installed.';
                        RETURN;
                    END IF;

                    REVOKE ALL ON SCHEMA party, onboarding FROM PUBLIC;
                    GRANT USAGE ON SCHEMA party, onboarding TO customeronboarding_app;

                    GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA party, onboarding TO customeronboarding_app;
                    GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA party, onboarding TO customeronboarding_app;

                    ALTER DEFAULT PRIVILEGES FOR ROLE customeronboarding_migrator IN SCHEMA party, onboarding
                        GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO customeronboarding_app;
                    ALTER DEFAULT PRIVILEGES FOR ROLE customeronboarding_migrator IN SCHEMA party, onboarding
                        GRANT USAGE, SELECT ON SEQUENCES TO customeronboarding_app;
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

                    ALTER DEFAULT PRIVILEGES FOR ROLE customeronboarding_migrator IN SCHEMA party, onboarding
                        REVOKE SELECT, INSERT, UPDATE, DELETE ON TABLES FROM customeronboarding_app;
                    ALTER DEFAULT PRIVILEGES FOR ROLE customeronboarding_migrator IN SCHEMA party, onboarding
                        REVOKE USAGE, SELECT ON SEQUENCES FROM customeronboarding_app;

                    REVOKE ALL ON ALL SEQUENCES IN SCHEMA party, onboarding FROM customeronboarding_app;
                    REVOKE ALL ON ALL TABLES IN SCHEMA party, onboarding FROM customeronboarding_app;
                    REVOKE ALL ON SCHEMA party, onboarding FROM customeronboarding_app;
                END
                $migration$;
                """);
        }
    }
}
