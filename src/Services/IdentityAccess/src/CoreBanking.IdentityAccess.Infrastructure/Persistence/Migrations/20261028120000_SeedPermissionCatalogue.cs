using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using CoreBanking.IdentityAccess.Infrastructure.Persistence;

#nullable disable

namespace CoreBanking.IdentityAccess.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Seeds the approved Phase 1 permission catalogue (P1-E02-D09). Reference data only: every
    /// identifier is already enforced in code, permissions are permanent, and the insert is
    /// idempotent so a re-run on an existing database changes nothing.
    /// </summary>
    [DbContext(typeof(IdentityAccessDbContext))]
    [Migration("20261028120000_SeedPermissionCatalogue")]
    public partial class SeedPermissionCatalogue : Migration
    {
        private const string Codes = """
            'platform.foundation.execute',
            'platform.institution.create',
            'platform.institution.activate',
            'platform.institution.suspend',
            'platform.provisioning.repair',
            'institution.tenant.create',
            'institution.region.manage',
            'institution.branch.create',
            'institution.branch.activate',
            'institution.branch.suspend',
            'institution.calendar.manage',
            'institution.business_date.advance',
            'identity.role.create',
            'identity.permission.assign',
            'identity.access.review',
            'identity.identity.create',
            'identity.identity.update',
            'identity.identity.view',
            'identity.approval.review',
            'identity.mfa.manage',
            'audit.event.view',
            'product.version.update',
            'product.version.simulate',
            'product.version.publish',
            'product.version.approve',
            'customer.application.create',
            'customer.application.update',
            'customer.application.submit',
            'customer.application.review',
            'customer.application.view',
            'integration.api_client.manage',
            'integration.credential_reference.manage'
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
