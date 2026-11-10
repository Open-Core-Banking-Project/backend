using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.IdentityAccess.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddImmutableRoleVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "role_version",
                schema: "access",
                table: "identity_roles",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "role_versions",
                schema: "access",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "text", nullable: false),
                    role_id = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    role_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    role_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    approval_request_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    workflow_decision_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    published_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_versions", x => new { x.institution_id, x.role_id, x.version });
                    table.ForeignKey(
                        name: "FK_role_versions_roles_institution_id_role_id",
                        columns: x => new { x.institution_id, x.role_id },
                        principalSchema: "access",
                        principalTable: "roles",
                        principalColumns: new[] { "institution_id", "role_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "role_version_permissions",
                schema: "access",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "text", nullable: false),
                    role_id = table.Column<string>(type: "text", nullable: false),
                    role_version = table.Column<long>(type: "bigint", nullable: false),
                    permission_code = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_version_permissions", x => new { x.institution_id, x.role_id, x.role_version, x.permission_code });
                    table.ForeignKey(
                        name: "FK_role_version_permissions_permissions_permission_code",
                        column: x => x.permission_code,
                        principalSchema: "access",
                        principalTable: "permissions",
                        principalColumn: "permission_code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_role_version_permissions_role_versions_institution_id_role_~",
                        columns: x => new { x.institution_id, x.role_id, x.role_version },
                        principalSchema: "access",
                        principalTable: "role_versions",
                        principalColumns: new[] { "institution_id", "role_id", "version" },
                        onDelete: ReferentialAction.Restrict);
                });

            // Preserve every already-active role and assignment. Older rows predate Workflow
            // evidence, so their synthetic legacy references are explicit and collision-safe;
            // versions published by the new path retain their real request, hash and decision.
            migrationBuilder.Sql(
                """
                INSERT INTO access.role_versions
                    (institution_id, role_id, version, role_code, role_name, content_hash,
                     approval_request_id, workflow_decision_reference, published_by, published_at)
                SELECT roles.institution_id,
                       roles.role_id,
                       roles.version,
                       roles.role_code,
                       roles.role_name,
                       COALESCE(
                           roles.approval_content_hash,
                           md5(roles.institution_id || '|' || roles.role_id || '|' || roles.version::text) ||
                           md5(roles.role_code || '|' || roles.role_name)),
                       COALESCE(roles.approval_request_id, 'legacy-' || md5(roles.institution_id || '|' || roles.role_id)),
                       COALESCE(roles.workflow_decision_reference, 'legacy-' || md5(roles.role_id || '|' || roles.institution_id)),
                       COALESCE(roles.decided_by, 'migration'),
                       COALESCE(roles.published_at, roles.updated_at)
                FROM access.roles AS roles
                WHERE roles.status = 'Active';

                INSERT INTO access.role_version_permissions
                    (institution_id, role_id, role_version, permission_code)
                SELECT permissions.institution_id,
                       permissions.role_id,
                       roles.version,
                       permissions.permission_code
                FROM access.role_permissions AS permissions
                JOIN access.roles AS roles
                  ON roles.institution_id = permissions.institution_id
                 AND roles.role_id = permissions.role_id
                WHERE roles.status = 'Active';

                UPDATE access.identity_roles AS assignments
                   SET role_version = roles.version
                  FROM access.roles AS roles
                 WHERE roles.institution_id = assignments.institution_id
                   AND roles.role_id = assignments.role_id;

                ALTER TABLE access.identity_roles ALTER COLUMN role_version DROP DEFAULT;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_identity_roles_institution_id_role_id_role_version",
                schema: "access",
                table: "identity_roles",
                columns: new[] { "institution_id", "role_id", "role_version" });

            migrationBuilder.CreateIndex(
                name: "IX_role_version_permissions_permission_code",
                schema: "access",
                table: "role_version_permissions",
                column: "permission_code");

            migrationBuilder.CreateIndex(
                name: "IX_role_versions_institution_id_workflow_decision_reference",
                schema: "access",
                table: "role_versions",
                columns: new[] { "institution_id", "workflow_decision_reference" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_identity_roles_role_versions_institution_id_role_id_role_ve~",
                schema: "access",
                table: "identity_roles",
                columns: new[] { "institution_id", "role_id", "role_version" },
                principalSchema: "access",
                principalTable: "role_versions",
                principalColumns: new[] { "institution_id", "role_id", "version" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_identity_roles_role_versions_institution_id_role_id_role_ve~",
                schema: "access",
                table: "identity_roles");

            migrationBuilder.DropTable(
                name: "role_version_permissions",
                schema: "access");

            migrationBuilder.DropTable(
                name: "role_versions",
                schema: "access");

            migrationBuilder.DropIndex(
                name: "IX_identity_roles_institution_id_role_id_role_version",
                schema: "access",
                table: "identity_roles");

            migrationBuilder.DropColumn(
                name: "role_version",
                schema: "access",
                table: "identity_roles");
        }
    }
}
