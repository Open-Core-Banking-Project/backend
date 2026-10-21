using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.CoreLedger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSavingsProductConfigurationDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "product");

            migrationBuilder.CreateTable(
                name: "account_products",
                schema: "product",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    product_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    family = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    configuration_set_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    created_by_actor_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_products", x => new { x.institution_id, x.product_id });
                    table.CheckConstraint("ck_account_products_family", "family IN ('savings')");
                    table.ForeignKey(
                        name: "FK_account_products_configuration_sets_institution_id_configur~",
                        columns: x => new { x.institution_id, x.configuration_set_id },
                        principalSchema: "institution",
                        principalTable: "configuration_sets",
                        principalColumns: new[] { "institution_id", "configuration_set_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_account_products_institution_id_code",
                schema: "product",
                table: "account_products",
                columns: new[] { "institution_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_account_products_institution_id_configuration_set_id",
                schema: "product",
                table: "account_products",
                columns: new[] { "institution_id", "configuration_set_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_products",
                schema: "product");
        }
    }
}
