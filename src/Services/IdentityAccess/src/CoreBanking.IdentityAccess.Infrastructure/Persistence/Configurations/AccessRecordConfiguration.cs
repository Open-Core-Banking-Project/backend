using System;
using System.Linq;
using CoreBanking.IdentityAccess.Infrastructure.Persistence.Access;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreBanking.IdentityAccess.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mappings for the <c>access</c> schema. Roles, grants, scopes, conditions, conflicts, temporary
/// grants and entitlement versions are institution-scoped; only the permission catalogue is
/// platform-owned, because a permission code means the same thing in every institution.
/// Decision: ADR-0004, ADR-0009.
/// </summary>
internal sealed class RoleRecordConfiguration : IEntityTypeConfiguration<RoleRecord>
{
    public void Configure(EntityTypeBuilder<RoleRecord> builder)
    {
        builder.ToTable("roles", "access");
        builder.HasKey(role => new { role.InstitutionId, role.RoleId });

        builder.Property(role => role.InstitutionId).HasColumnName("institution_id");
        builder.Property(role => role.RoleId).HasColumnName("role_id");
        builder.Property(role => role.RoleCode).HasColumnName("role_code");
        builder.Property(role => role.RoleName).HasColumnName("role_name");
        builder.Property(role => role.Version).HasColumnName("version");
        builder.Property(role => role.Status).HasColumnName("status").HasConversion<string>();
        builder.Property(role => role.CreatedAt).HasColumnName("created_at");
        builder.Property(role => role.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(role => new { role.InstitutionId, role.RoleCode }).IsUnique();
    }
}

internal sealed class PermissionRecordConfiguration : IEntityTypeConfiguration<PermissionRecord>
{
    public void Configure(EntityTypeBuilder<PermissionRecord> builder)
    {
        builder.ToTable("permissions", "access");
        builder.HasKey(permission => permission.PermissionCode);

        builder.Property(permission => permission.PermissionCode).HasColumnName("permission_code");
        builder.Property(permission => permission.Description).HasColumnName("description");
        builder.Property(permission => permission.IntroducedAt).HasColumnName("introduced_at");

        // The foundation catalogue of epic 03: synthetic administrative permissions that prove the
        // model. This is reference data, not the production permission catalogue, and it grants no
        // wildcard. A new permission is added by a later migration.
        builder.HasData(FoundationPermissions.Select(permission => new PermissionRecord
        {
            PermissionCode = permission.Code,
            Description = permission.Description,
            IntroducedAt = FoundationPermissionIntroducedAt,
        }));
    }

    private static readonly DateTimeOffset FoundationPermissionIntroducedAt = new(2026, 10, 16, 0, 0, 0, TimeSpan.Zero);

    private static readonly (string Code, string Description)[] FoundationPermissions =
    [
        ("platform.foundation.execute", "Execute the platform foundation operation"),
        ("institution.tenant.create", "Create an institution tenant"),
        ("identity.role.create", "Create an institution-scoped role"),
        ("identity.permission.assign", "Assign a permission to a role or an identity"),
        ("identity.access.review", "Review institution access"),
        ("identity.identity.create", "Register an institution identity and membership"),
        ("identity.identity.view", "View an institution identity"),
        ("identity.identity.update", "Change an institution identity lifecycle status"),
        ("audit.event.view", "View audit evidence"),
    ];
}

internal sealed class RolePermissionRecordConfiguration : IEntityTypeConfiguration<RolePermissionRecord>
{
    public void Configure(EntityTypeBuilder<RolePermissionRecord> builder)
    {
        builder.ToTable("role_permissions", "access");
        builder.HasKey(grant => new { grant.InstitutionId, grant.RoleId, grant.PermissionCode });

        builder.Property(grant => grant.InstitutionId).HasColumnName("institution_id");
        builder.Property(grant => grant.RoleId).HasColumnName("role_id");
        builder.Property(grant => grant.PermissionCode).HasColumnName("permission_code");

        builder.HasOne<RoleRecord>()
            .WithMany()
            .HasForeignKey(grant => new { grant.InstitutionId, grant.RoleId })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<PermissionRecord>()
            .WithMany()
            .HasForeignKey(grant => grant.PermissionCode)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class IdentityRoleRecordConfiguration : IEntityTypeConfiguration<IdentityRoleRecord>
{
    public void Configure(EntityTypeBuilder<IdentityRoleRecord> builder)
    {
        builder.ToTable("identity_roles", "access");
        builder.HasKey(assignment => new { assignment.InstitutionId, assignment.IdentityId, assignment.RoleId });

        builder.Property(assignment => assignment.InstitutionId).HasColumnName("institution_id");
        builder.Property(assignment => assignment.IdentityId).HasColumnName("identity_id");
        builder.Property(assignment => assignment.RoleId).HasColumnName("role_id");
        builder.Property(assignment => assignment.RegionId).HasColumnName("region_id");
        builder.Property(assignment => assignment.BranchId).HasColumnName("branch_id");
        builder.Property(assignment => assignment.ChannelId).HasColumnName("channel_id");
        builder.Property(assignment => assignment.ValidFrom).HasColumnName("valid_from");
        builder.Property(assignment => assignment.ValidTo).HasColumnName("valid_to");
        builder.Property(assignment => assignment.AssignedBy).HasColumnName("assigned_by");

        builder.HasIndex(assignment => new { assignment.InstitutionId, assignment.RoleId });

        builder.HasOne<Identity.IdentityRecord>()
            .WithMany()
            .HasForeignKey(assignment => new { assignment.InstitutionId, assignment.IdentityId })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<RoleRecord>()
            .WithMany()
            .HasForeignKey(assignment => new { assignment.InstitutionId, assignment.RoleId })
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ResourceScopeRecordConfiguration : IEntityTypeConfiguration<ResourceScopeRecord>
{
    public void Configure(EntityTypeBuilder<ResourceScopeRecord> builder)
    {
        builder.ToTable("resource_scopes", "access");
        builder.HasKey(scope => new { scope.InstitutionId, scope.ResourceScopeId });

        builder.Property(scope => scope.InstitutionId).HasColumnName("institution_id");
        builder.Property(scope => scope.ResourceScopeId).HasColumnName("resource_scope_id");
        builder.Property(scope => scope.ScopeType).HasColumnName("scope_type");
        builder.Property(scope => scope.ScopeValue).HasColumnName("scope_value");
        builder.Property(scope => scope.ParentResourceScopeId).HasColumnName("parent_resource_scope_id");

        builder.HasIndex(scope => new { scope.InstitutionId, scope.ScopeType, scope.ScopeValue });

        builder.HasOne<ResourceScopeRecord>()
            .WithMany()
            .HasForeignKey(scope => new { scope.InstitutionId, scope.ParentResourceScopeId })
            .HasConstraintName("fk_resource_scopes_parent")
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PermissionConditionRecordConfiguration : IEntityTypeConfiguration<PermissionConditionRecord>
{
    public void Configure(EntityTypeBuilder<PermissionConditionRecord> builder)
    {
        builder.ToTable("permission_conditions", "access");
        builder.HasKey(condition => new { condition.InstitutionId, condition.PermissionConditionId });

        builder.Property(condition => condition.InstitutionId).HasColumnName("institution_id");
        builder.Property(condition => condition.PermissionConditionId).HasColumnName("permission_condition_id");
        builder.Property(condition => condition.RoleId).HasColumnName("role_id");
        builder.Property(condition => condition.PermissionCode).HasColumnName("permission_code");
        builder.Property(condition => condition.ConditionType).HasColumnName("condition_type");
        builder.Property(condition => condition.ConditionValue).HasColumnName("condition_value");

        builder.HasIndex(condition => new { condition.InstitutionId, condition.RoleId });

        builder.HasOne<RoleRecord>()
            .WithMany()
            .HasForeignKey(condition => new { condition.InstitutionId, condition.RoleId })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<PermissionRecord>()
            .WithMany()
            .HasForeignKey(condition => condition.PermissionCode)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ConflictRuleRecordConfiguration : IEntityTypeConfiguration<ConflictRuleRecord>
{
    public void Configure(EntityTypeBuilder<ConflictRuleRecord> builder)
    {
        builder.ToTable("conflict_rules", "access");
        builder.HasKey(rule => new { rule.InstitutionId, rule.ConflictRuleId });

        builder.Property(rule => rule.InstitutionId).HasColumnName("institution_id");
        builder.Property(rule => rule.ConflictRuleId).HasColumnName("conflict_rule_id");
        builder.Property(rule => rule.PermissionCode).HasColumnName("permission_code");
        builder.Property(rule => rule.ConflictingPermissionCode).HasColumnName("conflicting_permission_code");
        builder.Property(rule => rule.RuleType).HasColumnName("rule_type");
        builder.Property(rule => rule.Description).HasColumnName("description");

        builder.HasOne<PermissionRecord>()
            .WithMany()
            .HasForeignKey(rule => rule.PermissionCode)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<PermissionRecord>()
            .WithMany()
            .HasForeignKey(rule => rule.ConflictingPermissionCode)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TemporaryGrantRecordConfiguration : IEntityTypeConfiguration<TemporaryGrantRecord>
{
    public void Configure(EntityTypeBuilder<TemporaryGrantRecord> builder)
    {
        builder.ToTable("temporary_grants", "access");
        builder.HasKey(grant => new { grant.InstitutionId, grant.TemporaryGrantId });

        builder.Property(grant => grant.InstitutionId).HasColumnName("institution_id");
        builder.Property(grant => grant.TemporaryGrantId).HasColumnName("temporary_grant_id");
        builder.Property(grant => grant.IdentityId).HasColumnName("identity_id");
        builder.Property(grant => grant.PermissionCode).HasColumnName("permission_code");
        builder.Property(grant => grant.RegionId).HasColumnName("region_id");
        builder.Property(grant => grant.BranchId).HasColumnName("branch_id");
        builder.Property(grant => grant.ValidFrom).HasColumnName("valid_from");
        builder.Property(grant => grant.ValidTo).HasColumnName("valid_to");
        builder.Property(grant => grant.GrantedBy).HasColumnName("granted_by");
        builder.Property(grant => grant.Reason).HasColumnName("reason");
        builder.Property(grant => grant.ExpiredAt).HasColumnName("expired_at");

        builder.HasIndex(grant => new { grant.InstitutionId, grant.IdentityId });

        builder.HasOne<Identity.IdentityRecord>()
            .WithMany()
            .HasForeignKey(grant => new { grant.InstitutionId, grant.IdentityId })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<PermissionRecord>()
            .WithMany()
            .HasForeignKey(grant => grant.PermissionCode)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class EntitlementVersionRecordConfiguration : IEntityTypeConfiguration<EntitlementVersionRecord>
{
    public void Configure(EntityTypeBuilder<EntitlementVersionRecord> builder)
    {
        builder.ToTable("entitlement_versions", "access");
        builder.HasKey(version => new { version.InstitutionId, version.IdentityId });

        builder.Property(version => version.InstitutionId).HasColumnName("institution_id");
        builder.Property(version => version.IdentityId).HasColumnName("identity_id");
        builder.Property(version => version.Version).HasColumnName("version");
        builder.Property(version => version.UpdatedAt).HasColumnName("updated_at");

        builder.HasOne<Identity.IdentityRecord>()
            .WithMany()
            .HasForeignKey(version => new { version.InstitutionId, version.IdentityId })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
