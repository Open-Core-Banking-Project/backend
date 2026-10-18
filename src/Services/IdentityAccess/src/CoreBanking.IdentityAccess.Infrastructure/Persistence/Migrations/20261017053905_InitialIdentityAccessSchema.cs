using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace CoreBanking.IdentityAccess.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialIdentityAccessSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "security");

            migrationBuilder.EnsureSchema(
                name: "access");

            migrationBuilder.EnsureSchema(
                name: "identity");

            migrationBuilder.CreateTable(
                name: "authentication_events",
                schema: "security",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "text", nullable: false),
                    authentication_event_id = table.Column<string>(type: "text", nullable: false),
                    identity_id = table.Column<string>(type: "text", nullable: true),
                    event_type = table.Column<string>(type: "text", nullable: false),
                    outcome = table.Column<string>(type: "text", nullable: false),
                    detail = table.Column<string>(type: "text", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_authentication_events", x => new { x.institution_id, x.authentication_event_id });
                });

            migrationBuilder.CreateTable(
                name: "identities",
                schema: "identity",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "text", nullable: false),
                    identity_id = table.Column<string>(type: "text", nullable: false),
                    actor_type = table.Column<string>(type: "text", nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identities", x => new { x.institution_id, x.identity_id });
                });

            migrationBuilder.CreateTable(
                name: "permissions",
                schema: "access",
                columns: table => new
                {
                    permission_code = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    introduced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_permissions", x => x.permission_code);
                });

            migrationBuilder.CreateTable(
                name: "resource_scopes",
                schema: "access",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "text", nullable: false),
                    resource_scope_id = table.Column<string>(type: "text", nullable: false),
                    scope_type = table.Column<string>(type: "text", nullable: false),
                    scope_value = table.Column<string>(type: "text", nullable: false),
                    parent_resource_scope_id = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_resource_scopes", x => new { x.institution_id, x.resource_scope_id });
                    table.ForeignKey(
                        name: "fk_resource_scopes_parent",
                        columns: x => new { x.institution_id, x.parent_resource_scope_id },
                        principalSchema: "access",
                        principalTable: "resource_scopes",
                        principalColumns: new[] { "institution_id", "resource_scope_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                schema: "access",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "text", nullable: false),
                    role_id = table.Column<string>(type: "text", nullable: false),
                    role_code = table.Column<string>(type: "text", nullable: false),
                    role_name = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roles", x => new { x.institution_id, x.role_id });
                });

            migrationBuilder.CreateTable(
                name: "service_clients",
                schema: "identity",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "text", nullable: false),
                    service_client_id = table.Column<string>(type: "text", nullable: false),
                    client_name = table.Column<string>(type: "text", nullable: false),
                    audience = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_clients", x => new { x.institution_id, x.service_client_id });
                });

            migrationBuilder.CreateTable(
                name: "entitlement_versions",
                schema: "access",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "text", nullable: false),
                    identity_id = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entitlement_versions", x => new { x.institution_id, x.identity_id });
                    table.ForeignKey(
                        name: "FK_entitlement_versions_identities_institution_id_identity_id",
                        columns: x => new { x.institution_id, x.identity_id },
                        principalSchema: "identity",
                        principalTable: "identities",
                        principalColumns: new[] { "institution_id", "identity_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sessions",
                schema: "identity",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "text", nullable: false),
                    session_id = table.Column<string>(type: "text", nullable: false),
                    identity_id = table.Column<string>(type: "text", nullable: false),
                    device_id = table.Column<string>(type: "text", nullable: true),
                    authentication_strength = table.Column<string>(type: "text", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sessions", x => new { x.institution_id, x.session_id });
                    table.ForeignKey(
                        name: "FK_sessions_identities_institution_id_identity_id",
                        columns: x => new { x.institution_id, x.identity_id },
                        principalSchema: "identity",
                        principalTable: "identities",
                        principalColumns: new[] { "institution_id", "identity_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "conflict_rules",
                schema: "access",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "text", nullable: false),
                    conflict_rule_id = table.Column<string>(type: "text", nullable: false),
                    permission_code = table.Column<string>(type: "text", nullable: false),
                    conflicting_permission_code = table.Column<string>(type: "text", nullable: false),
                    rule_type = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conflict_rules", x => new { x.institution_id, x.conflict_rule_id });
                    table.ForeignKey(
                        name: "FK_conflict_rules_permissions_conflicting_permission_code",
                        column: x => x.conflicting_permission_code,
                        principalSchema: "access",
                        principalTable: "permissions",
                        principalColumn: "permission_code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_conflict_rules_permissions_permission_code",
                        column: x => x.permission_code,
                        principalSchema: "access",
                        principalTable: "permissions",
                        principalColumn: "permission_code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "temporary_grants",
                schema: "access",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "text", nullable: false),
                    temporary_grant_id = table.Column<string>(type: "text", nullable: false),
                    identity_id = table.Column<string>(type: "text", nullable: false),
                    permission_code = table.Column<string>(type: "text", nullable: false),
                    region_id = table.Column<string>(type: "text", nullable: true),
                    branch_id = table.Column<string>(type: "text", nullable: true),
                    valid_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    granted_by = table.Column<string>(type: "text", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_temporary_grants", x => new { x.institution_id, x.temporary_grant_id });
                    table.ForeignKey(
                        name: "FK_temporary_grants_identities_institution_id_identity_id",
                        columns: x => new { x.institution_id, x.identity_id },
                        principalSchema: "identity",
                        principalTable: "identities",
                        principalColumns: new[] { "institution_id", "identity_id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_temporary_grants_permissions_permission_code",
                        column: x => x.permission_code,
                        principalSchema: "access",
                        principalTable: "permissions",
                        principalColumn: "permission_code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "identity_roles",
                schema: "access",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "text", nullable: false),
                    identity_id = table.Column<string>(type: "text", nullable: false),
                    role_id = table.Column<string>(type: "text", nullable: false),
                    region_id = table.Column<string>(type: "text", nullable: true),
                    branch_id = table.Column<string>(type: "text", nullable: true),
                    channel_id = table.Column<string>(type: "text", nullable: true),
                    valid_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    assigned_by = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_roles", x => new { x.institution_id, x.identity_id, x.role_id });
                    table.ForeignKey(
                        name: "FK_identity_roles_identities_institution_id_identity_id",
                        columns: x => new { x.institution_id, x.identity_id },
                        principalSchema: "identity",
                        principalTable: "identities",
                        principalColumns: new[] { "institution_id", "identity_id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_identity_roles_roles_institution_id_role_id",
                        columns: x => new { x.institution_id, x.role_id },
                        principalSchema: "access",
                        principalTable: "roles",
                        principalColumns: new[] { "institution_id", "role_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "permission_conditions",
                schema: "access",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "text", nullable: false),
                    permission_condition_id = table.Column<string>(type: "text", nullable: false),
                    role_id = table.Column<string>(type: "text", nullable: false),
                    permission_code = table.Column<string>(type: "text", nullable: false),
                    condition_type = table.Column<string>(type: "text", nullable: false),
                    condition_value = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_permission_conditions", x => new { x.institution_id, x.permission_condition_id });
                    table.ForeignKey(
                        name: "FK_permission_conditions_permissions_permission_code",
                        column: x => x.permission_code,
                        principalSchema: "access",
                        principalTable: "permissions",
                        principalColumn: "permission_code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_permission_conditions_roles_institution_id_role_id",
                        columns: x => new { x.institution_id, x.role_id },
                        principalSchema: "access",
                        principalTable: "roles",
                        principalColumns: new[] { "institution_id", "role_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "role_permissions",
                schema: "access",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "text", nullable: false),
                    role_id = table.Column<string>(type: "text", nullable: false),
                    permission_code = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_permissions", x => new { x.institution_id, x.role_id, x.permission_code });
                    table.ForeignKey(
                        name: "FK_role_permissions_permissions_permission_code",
                        column: x => x.permission_code,
                        principalSchema: "access",
                        principalTable: "permissions",
                        principalColumn: "permission_code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_role_permissions_roles_institution_id_role_id",
                        columns: x => new { x.institution_id, x.role_id },
                        principalSchema: "access",
                        principalTable: "roles",
                        principalColumns: new[] { "institution_id", "role_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "access",
                table: "permissions",
                columns: new[] { "permission_code", "description", "introduced_at" },
                values: new object[,]
                {
                    { "audit.event.view", "View audit evidence", new DateTimeOffset(new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { "identity.access.review", "Review institution access", new DateTimeOffset(new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { "identity.permission.assign", "Assign a permission to a role or an identity", new DateTimeOffset(new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { "identity.role.create", "Create an institution-scoped role", new DateTimeOffset(new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { "institution.tenant.create", "Create an institution tenant", new DateTimeOffset(new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { "platform.foundation.execute", "Execute the platform foundation operation", new DateTimeOffset(new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) }
                });

            migrationBuilder.CreateIndex(
                name: "IX_authentication_events_institution_id_identity_id_occurred_at",
                schema: "security",
                table: "authentication_events",
                columns: new[] { "institution_id", "identity_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_conflict_rules_conflicting_permission_code",
                schema: "access",
                table: "conflict_rules",
                column: "conflicting_permission_code");

            migrationBuilder.CreateIndex(
                name: "IX_conflict_rules_permission_code",
                schema: "access",
                table: "conflict_rules",
                column: "permission_code");

            migrationBuilder.CreateIndex(
                name: "IX_identity_roles_institution_id_role_id",
                schema: "access",
                table: "identity_roles",
                columns: new[] { "institution_id", "role_id" });

            migrationBuilder.CreateIndex(
                name: "IX_permission_conditions_institution_id_role_id",
                schema: "access",
                table: "permission_conditions",
                columns: new[] { "institution_id", "role_id" });

            migrationBuilder.CreateIndex(
                name: "IX_permission_conditions_permission_code",
                schema: "access",
                table: "permission_conditions",
                column: "permission_code");

            migrationBuilder.CreateIndex(
                name: "IX_resource_scopes_institution_id_parent_resource_scope_id",
                schema: "access",
                table: "resource_scopes",
                columns: new[] { "institution_id", "parent_resource_scope_id" });

            migrationBuilder.CreateIndex(
                name: "IX_resource_scopes_institution_id_scope_type_scope_value",
                schema: "access",
                table: "resource_scopes",
                columns: new[] { "institution_id", "scope_type", "scope_value" });

            migrationBuilder.CreateIndex(
                name: "IX_role_permissions_permission_code",
                schema: "access",
                table: "role_permissions",
                column: "permission_code");

            migrationBuilder.CreateIndex(
                name: "IX_roles_institution_id_role_code",
                schema: "access",
                table: "roles",
                columns: new[] { "institution_id", "role_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sessions_institution_id_identity_id",
                schema: "identity",
                table: "sessions",
                columns: new[] { "institution_id", "identity_id" });

            migrationBuilder.CreateIndex(
                name: "IX_temporary_grants_institution_id_identity_id",
                schema: "access",
                table: "temporary_grants",
                columns: new[] { "institution_id", "identity_id" });

            migrationBuilder.CreateIndex(
                name: "IX_temporary_grants_permission_code",
                schema: "access",
                table: "temporary_grants",
                column: "permission_code");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "authentication_events",
                schema: "security");

            migrationBuilder.DropTable(
                name: "conflict_rules",
                schema: "access");

            migrationBuilder.DropTable(
                name: "entitlement_versions",
                schema: "access");

            migrationBuilder.DropTable(
                name: "identity_roles",
                schema: "access");

            migrationBuilder.DropTable(
                name: "permission_conditions",
                schema: "access");

            migrationBuilder.DropTable(
                name: "resource_scopes",
                schema: "access");

            migrationBuilder.DropTable(
                name: "role_permissions",
                schema: "access");

            migrationBuilder.DropTable(
                name: "service_clients",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "sessions",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "temporary_grants",
                schema: "access");

            migrationBuilder.DropTable(
                name: "roles",
                schema: "access");

            migrationBuilder.DropTable(
                name: "identities",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "permissions",
                schema: "access");
        }
    }
}
