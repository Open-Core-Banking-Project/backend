using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.CustomerOnboarding.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeterministicMatchLookupTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "birth_date_lookup_token",
                schema: "party",
                table: "individuals",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "lookup_token",
                schema: "party",
                table: "addresses",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "ix_party_individuals_institution_birth_lookup",
                schema: "party",
                table: "individuals",
                columns: new[] { "institution_id", "birth_date_lookup_token" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_party_individuals_birth_lookup_token",
                schema: "party",
                table: "individuals",
                sql: "char_length(birth_date_lookup_token) IN (0, 64)");

            migrationBuilder.CreateIndex(
                name: "ix_party_addresses_institution_lookup",
                schema: "party",
                table: "addresses",
                columns: new[] { "institution_id", "lookup_token" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_party_addresses_lookup_token",
                schema: "party",
                table: "addresses",
                sql: "char_length(lookup_token) IN (0, 64)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_party_individuals_institution_birth_lookup",
                schema: "party",
                table: "individuals");

            migrationBuilder.DropCheckConstraint(
                name: "ck_party_individuals_birth_lookup_token",
                schema: "party",
                table: "individuals");

            migrationBuilder.DropIndex(
                name: "ix_party_addresses_institution_lookup",
                schema: "party",
                table: "addresses");

            migrationBuilder.DropCheckConstraint(
                name: "ck_party_addresses_lookup_token",
                schema: "party",
                table: "addresses");

            migrationBuilder.DropColumn(
                name: "birth_date_lookup_token",
                schema: "party",
                table: "individuals");

            migrationBuilder.DropColumn(
                name: "lookup_token",
                schema: "party",
                table: "addresses");
        }
    }
}
