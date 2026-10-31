using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace CoreBanking.IdentityAccess.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIdentityMembershipAndProviderLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Phase 0 identities did not carry an explicit customer/staff/agent/corporate/admin
            // kind. Inventing one during migration could elevate or misclassify access, so an
            // installation with legacy identities must run an approved mapping/import first.
            migrationBuilder.Sql(
                """
                DO $migration$
                BEGIN
                    IF EXISTS (SELECT 1 FROM identity.identities) THEN
                        RAISE EXCEPTION 'Identity kind mapping is required before applying AddIdentityMembershipAndProviderLinks.';
                    END IF;
                END
                $migration$;
                """);

            migrationBuilder.AddColumn<long>(
                name: "concurrency_version",
                schema: "identity",
                table: "identities",
                type: "bigint",
                nullable: false);

            migrationBuilder.AddColumn<string>(
                name: "identity_type",
                schema: "identity",
                table: "identities",
                type: "text",
                nullable: false);

            migrationBuilder.CreateTable(
                name: "identity_links",
                schema: "identity",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "text", nullable: false),
                    identity_link_id = table.Column<string>(type: "text", nullable: false),
                    identity_id = table.Column<string>(type: "text", nullable: false),
                    provider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    realm = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    subject_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_links", x => new { x.institution_id, x.identity_link_id });
                    table.CheckConstraint("ck_identity_links_status", "status IN ('Active', 'Suspended', 'Archived')");
                    table.ForeignKey(
                        name: "FK_identity_links_identities_institution_id_identity_id",
                        columns: x => new { x.institution_id, x.identity_id },
                        principalSchema: "identity",
                        principalTable: "identities",
                        principalColumns: new[] { "institution_id", "identity_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "institution_memberships",
                schema: "identity",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "text", nullable: false),
                    membership_id = table.Column<string>(type: "text", nullable: false),
                    identity_id = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    concurrency_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_institution_memberships", x => new { x.institution_id, x.membership_id });
                    table.CheckConstraint("ck_institution_memberships_concurrency_version", "concurrency_version > 0");
                    table.CheckConstraint("ck_institution_memberships_status", "status IN ('Pending', 'Active', 'Locked', 'Suspended', 'Disabled', 'Closed')");
                    table.ForeignKey(
                        name: "FK_institution_memberships_identities_institution_id_identity_~",
                        columns: x => new { x.institution_id, x.identity_id },
                        principalSchema: "identity",
                        principalTable: "identities",
                        principalColumns: new[] { "institution_id", "identity_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "access",
                table: "permissions",
                columns: new[] { "permission_code", "description", "introduced_at" },
                values: new object[,]
                {
                    { "identity.identity.create", "Register an institution identity and membership", new DateTimeOffset(new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { "identity.identity.update", "Change an institution identity lifecycle status", new DateTimeOffset(new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { "identity.identity.view", "View an institution identity", new DateTimeOffset(new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) }
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_identities_concurrency_version",
                schema: "identity",
                table: "identities",
                sql: "concurrency_version > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_identities_identity_type",
                schema: "identity",
                table: "identities",
                sql: "identity_type IN ('Customer', 'Staff', 'Agent', 'CorporateUser', 'Administrator', 'Service')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_identities_status",
                schema: "identity",
                table: "identities",
                sql: "status IN ('Pending', 'Active', 'Locked', 'Suspended', 'Disabled', 'Closed')");

            migrationBuilder.CreateIndex(
                name: "IX_identity_links_institution_id_identity_id",
                schema: "identity",
                table: "identity_links",
                columns: new[] { "institution_id", "identity_id" });

            migrationBuilder.CreateIndex(
                name: "IX_identity_links_institution_id_provider_realm_subject_finger~",
                schema: "identity",
                table: "identity_links",
                columns: new[] { "institution_id", "provider", "realm", "subject_fingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_institution_memberships_institution_id_identity_id",
                schema: "identity",
                table: "institution_memberships",
                columns: new[] { "institution_id", "identity_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "identity_links",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "institution_memberships",
                schema: "identity");

            migrationBuilder.DropCheckConstraint(
                name: "ck_identities_concurrency_version",
                schema: "identity",
                table: "identities");

            migrationBuilder.DropCheckConstraint(
                name: "ck_identities_identity_type",
                schema: "identity",
                table: "identities");

            migrationBuilder.DropCheckConstraint(
                name: "ck_identities_status",
                schema: "identity",
                table: "identities");

            migrationBuilder.DeleteData(
                schema: "access",
                table: "permissions",
                keyColumn: "permission_code",
                keyValue: "identity.identity.create");

            migrationBuilder.DeleteData(
                schema: "access",
                table: "permissions",
                keyColumn: "permission_code",
                keyValue: "identity.identity.update");

            migrationBuilder.DeleteData(
                schema: "access",
                table: "permissions",
                keyColumn: "permission_code",
                keyValue: "identity.identity.view");

            migrationBuilder.DropColumn(
                name: "concurrency_version",
                schema: "identity",
                table: "identities");

            migrationBuilder.DropColumn(
                name: "identity_type",
                schema: "identity",
                table: "identities");
        }
    }
}
