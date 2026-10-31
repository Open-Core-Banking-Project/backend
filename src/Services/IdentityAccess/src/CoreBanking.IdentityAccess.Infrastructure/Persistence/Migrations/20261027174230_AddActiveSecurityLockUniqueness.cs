using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.IdentityAccess.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Enforces one unreleased security lock per institution, identity and scope.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A partial unique index is the only constraint that can express "a scope is locked" because a
    /// predicate cannot evaluate expiry. Lapsed locks are therefore closed first, by setting
    /// <c>released_at</c> to the instant the lock stopped applying, and any remaining duplicate is a
    /// genuine concurrent lock that needs an operator decision, so the migration fails with counts
    /// instead of silently discarding evidence.
    /// </para>
    /// <para>
    /// The index replaces the non-unique index on the same columns: an equality lookup on
    /// institution, identity and scope resolves through this index, so keeping both would be a
    /// duplicate. The reopen in <c>Down</c> does not reverse the closure of lapsed locks, because
    /// restoring "unreleased" on a lock whose window has passed would contradict the expiry rule.
    /// </para>
    /// </remarks>
    public partial class AddActiveSecurityLockUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_locks_institution_id_identity_id_scope",
                schema: "security",
                table: "locks");

            // A lock whose window has passed is not holding anything. Closing it here is the same
            // deterministic transformation the maintenance sweep applies to live rows.
            migrationBuilder.Sql(
                """
                UPDATE security.locks
                SET released_at = expires_at
                WHERE released_at IS NULL
                  AND expires_at IS NOT NULL
                  AND expires_at <= now();
                """);

            migrationBuilder.Sql(
                """
                DO $migration$
                DECLARE
                    duplicate_scopes bigint;
                BEGIN
                    SELECT count(*) INTO duplicate_scopes
                    FROM (
                        SELECT institution_id, identity_id, scope
                        FROM security.locks
                        WHERE released_at IS NULL
                        GROUP BY institution_id, identity_id, scope
                        HAVING count(*) > 1
                    ) duplicates;

                    IF duplicate_scopes > 0 THEN
                        RAISE EXCEPTION
                            'Security lock uniqueness failed: % institution, identity and scope combinations hold more than one unreleased lock. Release the duplicates before applying AddActiveSecurityLockUniqueness.',
                            duplicate_scopes;
                    END IF;
                END
                $migration$;
                """);

            migrationBuilder.CreateIndex(
                name: "ux_locks_unreleased_scope",
                schema: "security",
                table: "locks",
                columns: new[] { "institution_id", "identity_id", "scope" },
                unique: true,
                filter: "released_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_locks_unreleased_scope",
                schema: "security",
                table: "locks");

            migrationBuilder.CreateIndex(
                name: "IX_locks_institution_id_identity_id_scope",
                schema: "security",
                table: "locks",
                columns: new[] { "institution_id", "identity_id", "scope" });
        }
    }
}
