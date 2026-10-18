using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.AuditReconciliation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUntrustedMessageQuarantine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "untrusted_message_quarantine",
                schema: "integration",
                columns: table => new
                {
                    transport = table.Column<string>(type: "text", nullable: false),
                    stream = table.Column<string>(type: "text", nullable: false),
                    partition = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<long>(type: "bigint", nullable: false),
                    message_key = table.Column<string>(type: "text", nullable: true),
                    payload = table.Column<string>(type: "text", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    quarantined_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    repaired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_untrusted_message_quarantine", x => new { x.transport, x.stream, x.partition, x.position });
                });

            migrationBuilder.CreateIndex(
                name: "IX_untrusted_message_quarantine_repaired_at_quarantined_at",
                schema: "integration",
                table: "untrusted_message_quarantine",
                columns: new[] { "repaired_at", "quarantined_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "untrusted_message_quarantine",
                schema: "integration");
        }
    }
}
