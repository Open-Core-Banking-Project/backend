using CoreBanking.IdentityAccess.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.IdentityAccess.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Adds the three trusted-device permissions approved in P1-E02-D11. Reference data only: the identifiers
    /// are enforced in code, the insert is idempotent and no role template is granted them here.
    /// </summary>
    [DbContext(typeof(IdentityAccessDbContext))]
    [Migration("20261031164500_SeedDevicePermissions")]
    public partial class SeedDevicePermissions : Migration
    {
        private const string Codes = """
            'identity.device.manage',
            'identity.device.view',
            'identity.device.revoke'
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                $$"""
                INSERT INTO access.permissions (permission_code, description, introduced_at)
                SELECT code, code, now()
                FROM unnest(ARRAY[{{Codes}}]) AS code
                ON CONFLICT (permission_code) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                $$"""
                DELETE FROM access.permissions
                WHERE permission_code IN ({{Codes}})
                  AND NOT EXISTS (
                      SELECT 1 FROM access.role_permissions
                      WHERE role_permissions.permission_code = permissions.permission_code);
                """);
        }
    }
}
