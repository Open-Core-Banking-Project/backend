using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.CustomerOnboarding.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerAuditEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.CreateTable(
                name: "customer_audit_events",
                schema: "audit",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    audit_event_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    action = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    subject_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    subject_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    detail = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_audit_events", x => new { x.institution_id, x.audit_event_id });
                });

            migrationBuilder.CreateIndex(
                name: "ix_customer_audit_events_institution_occurred",
                schema: "audit",
                table: "customer_audit_events",
                columns: new[] { "institution_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_customer_audit_events_institution_subject",
                schema: "audit",
                table: "customer_audit_events",
                columns: new[] { "institution_id", "subject_reference" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customer_audit_events",
                schema: "audit");
        }
    }
}
