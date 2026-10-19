using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.CustomerOnboarding.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialInstitutionBoundary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "integration");

            migrationBuilder.EnsureSchema(
                name: "platform");

            migrationBuilder.CreateTable(
                name: "dead_letters",
                schema: "integration",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "text", nullable: false),
                    dead_letter_id = table.Column<string>(type: "text", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false),
                    event_id = table.Column<string>(type: "text", nullable: false),
                    event_type = table.Column<string>(type: "text", nullable: true),
                    payload = table.Column<string>(type: "text", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    repaired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dead_letters", x => new { x.institution_id, x.dead_letter_id });
                });

            migrationBuilder.CreateTable(
                name: "idempotency_records",
                schema: "integration",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "text", nullable: false),
                    operation = table.Column<string>(type: "text", nullable: false),
                    initiator = table.Column<string>(type: "text", nullable: false),
                    idempotency_key = table.Column<string>(type: "text", nullable: false),
                    request_fingerprint = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    result = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_idempotency_records", x => new { x.institution_id, x.operation, x.initiator, x.idempotency_key });
                });

            migrationBuilder.CreateTable(
                name: "inbox_messages",
                schema: "integration",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "text", nullable: false),
                    consumer = table.Column<string>(type: "text", nullable: false),
                    event_id = table.Column<string>(type: "text", nullable: false),
                    event_type = table.Column<string>(type: "text", nullable: false),
                    schema_version = table.Column<int>(type: "integer", nullable: false),
                    outcome = table.Column<string>(type: "text", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbox_messages", x => new { x.institution_id, x.consumer, x.event_id });
                });

            migrationBuilder.CreateTable(
                name: "institution_boundaries",
                schema: "platform",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    provisioning_plan_version = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source_event_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    initialised_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_institution_boundaries", x => x.institution_id);
                    table.CheckConstraint("ck_institution_boundaries_plan_version", "provisioning_plan_version > 0");
                    table.CheckConstraint("ck_institution_boundaries_status", "status IN ('provisioning', 'active', 'suspended')");
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "integration",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "text", nullable: false),
                    message_id = table.Column<string>(type: "text", nullable: false),
                    event_id = table.Column<string>(type: "text", nullable: false),
                    event_type = table.Column<string>(type: "text", nullable: false),
                    schema_version = table.Column<int>(type: "integer", nullable: false),
                    topic = table.Column<string>(type: "text", nullable: false),
                    business_reference = table.Column<string>(type: "text", nullable: false),
                    correlation_id = table.Column<string>(type: "text", nullable: false),
                    causation_id = table.Column<string>(type: "text", nullable: true),
                    trace_parent = table.Column<string>(type: "text", nullable: true),
                    trace_state = table.Column<string>(type: "text", nullable: true),
                    producer = table.Column<string>(type: "text", nullable: false),
                    envelope = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => new { x.institution_id, x.message_id });
                });

            migrationBuilder.CreateIndex(
                name: "IX_dead_letters_repaired_at_created_at",
                schema: "integration",
                table: "dead_letters",
                columns: new[] { "repaired_at", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_idempotency_records_status_created_at",
                schema: "integration",
                table: "idempotency_records",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_institution_boundaries_source_event_id",
                schema: "platform",
                table: "institution_boundaries",
                column: "source_event_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_institution_id_event_id",
                schema: "integration",
                table: "outbox_messages",
                columns: new[] { "institution_id", "event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_status_next_attempt_at",
                schema: "integration",
                table: "outbox_messages",
                columns: new[] { "status", "next_attempt_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dead_letters",
                schema: "integration");

            migrationBuilder.DropTable(
                name: "idempotency_records",
                schema: "integration");

            migrationBuilder.DropTable(
                name: "inbox_messages",
                schema: "integration");

            migrationBuilder.DropTable(
                name: "institution_boundaries",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "integration");
        }
    }
}
