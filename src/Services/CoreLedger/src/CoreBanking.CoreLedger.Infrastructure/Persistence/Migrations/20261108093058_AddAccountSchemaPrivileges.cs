using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.CoreLedger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountSchemaPrivileges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $migration$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'coreledger_app') THEN
                        RAISE NOTICE 'The coreledger_app role does not exist; application privileges for the account schema were not installed.';
                        RETURN;
                    END IF;

                    REVOKE ALL ON SCHEMA account FROM PUBLIC;
                    GRANT USAGE ON SCHEMA account TO coreledger_app;

                    GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA account TO coreledger_app;
                    GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA account TO coreledger_app;

                    ALTER DEFAULT PRIVILEGES FOR ROLE coreledger_migrator IN SCHEMA account
                        GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO coreledger_app;
                    ALTER DEFAULT PRIVILEGES FOR ROLE coreledger_migrator IN SCHEMA account
                        GRANT USAGE, SELECT ON SEQUENCES TO coreledger_app;
                    ALTER DEFAULT PRIVILEGES FOR ROLE coreledger_migrator IN SCHEMA account
                        GRANT EXECUTE ON FUNCTIONS TO coreledger_app;
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

                    ALTER DEFAULT PRIVILEGES FOR ROLE coreledger_migrator IN SCHEMA account
                        REVOKE SELECT, INSERT, UPDATE, DELETE ON TABLES FROM coreledger_app;
                    ALTER DEFAULT PRIVILEGES FOR ROLE coreledger_migrator IN SCHEMA account
                        REVOKE USAGE, SELECT ON SEQUENCES FROM coreledger_app;
                    ALTER DEFAULT PRIVILEGES FOR ROLE coreledger_migrator IN SCHEMA account
                        REVOKE EXECUTE ON FUNCTIONS FROM coreledger_app;

                    REVOKE ALL ON ALL SEQUENCES IN SCHEMA account FROM coreledger_app;
                    REVOKE ALL ON ALL TABLES IN SCHEMA account FROM coreledger_app;
                    REVOKE ALL ON SCHEMA account FROM coreledger_app;
                END
                $migration$;
                """);
        }
    }
}
