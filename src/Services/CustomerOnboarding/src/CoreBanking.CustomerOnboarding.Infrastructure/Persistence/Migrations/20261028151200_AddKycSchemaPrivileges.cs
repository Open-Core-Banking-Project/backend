using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.CustomerOnboarding.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Least-privilege grants for the <c>kyc</c> schema introduced with document reference metadata.
    /// Default privileges are per schema, so the new schema is granted explicitly; the
    /// <c>onboarding</c> tables added in the same change are covered by its existing default privileges.
    /// Decision: ADR-0004, P1-E03-D01.
    /// </summary>
    [DbContext(typeof(CustomerOnboardingDbContext))]
    [Migration("20261028151200_AddKycSchemaPrivileges")]
    public sealed class AddKycSchemaPrivileges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $migration$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'customeronboarding_app') THEN
                        RAISE NOTICE 'The customeronboarding_app role does not exist; kyc schema privileges were not installed.';
                        RETURN;
                    END IF;

                    IF NOT EXISTS (SELECT 1 FROM pg_namespace WHERE nspname = 'kyc') THEN
                        RAISE NOTICE 'The kyc schema does not exist; kyc schema privileges were not installed.';
                        RETURN;
                    END IF;

                    REVOKE ALL ON SCHEMA kyc FROM PUBLIC;
                    GRANT USAGE ON SCHEMA kyc TO customeronboarding_app;

                    GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA kyc TO customeronboarding_app;

                    ALTER DEFAULT PRIVILEGES FOR ROLE customeronboarding_migrator IN SCHEMA kyc
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

                    IF NOT EXISTS (SELECT 1 FROM pg_namespace WHERE nspname = 'kyc') THEN
                        RETURN;
                    END IF;

                    ALTER DEFAULT PRIVILEGES FOR ROLE customeronboarding_migrator IN SCHEMA kyc
                        REVOKE SELECT, INSERT, UPDATE, DELETE ON TABLES FROM customeronboarding_app;

                    REVOKE ALL ON ALL TABLES IN SCHEMA kyc FROM customeronboarding_app;
                    REVOKE ALL ON SCHEMA kyc FROM customeronboarding_app;
                END
                $migration$;
                """);
        }
    }
}
