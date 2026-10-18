using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.IdentityAccess.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Grants the Identity and Access application principal access to the schemas this service
    /// owns. The initialisation script can only set default privileges for the schemas that exist
    /// when the cluster is created, so the per-service schemas are granted here as part of the
    /// reviewed schema change. No superuser is granted to the application principal and PUBLIC
    /// keeps nothing. Decision: ADR-0004.
    /// </summary>
    public partial class GrantApplicationPrincipalPrivileges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "REVOKE ALL ON SCHEMA identity, access, security FROM PUBLIC;");

            migrationBuilder.Sql(
                "GRANT USAGE ON SCHEMA identity, access, security TO identityaccess_app;");

            migrationBuilder.Sql(
                "GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA identity, access, security TO identityaccess_app;");

            migrationBuilder.Sql(
                "ALTER DEFAULT PRIVILEGES FOR ROLE identityaccess_migrator IN SCHEMA identity, access, security "
                + "GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO identityaccess_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER DEFAULT PRIVILEGES FOR ROLE identityaccess_migrator IN SCHEMA identity, access, security "
                + "REVOKE SELECT, INSERT, UPDATE, DELETE ON TABLES FROM identityaccess_app;");

            migrationBuilder.Sql(
                "REVOKE ALL ON SCHEMA identity, access, security FROM identityaccess_app;");
        }
    }
}