using System;
using System.Collections.Generic;
using System.Linq;
using CoreBanking.BuildingBlocks.Application.Persistence;
using CoreBanking.BuildingBlocks.Application.Tenancy;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.IdentityAccess.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace CoreBanking.IdentityAccess.UnitTests.Persistence;

/// <summary>
/// Structural rules of the <c>identity_access_db</c> model. These tests build the model without
/// opening a connection, so they run everywhere and fail when a table loses its institution
/// column, its query filter or its tenant-aware foreign key.
/// Decision: ADR-0004.
/// </summary>
[Trait("Category", "Unit")]
public sealed class IdentityAccessModelTests
{
    private static readonly string[] ExpectedTables =
    [
        "access.conflict_rules",
        "access.entitlement_versions",
        "access.identity_roles",
        "access.permission_conditions",
        "access.permissions",
        "access.resource_scopes",
        "access.role_permissions",
        "access.roles",
        "access.temporary_grants",
        "audit.authorisation_events",
        "audit.operator_actions",
        "identity.identities",
        "identity.identity_links",
        "identity.institution_memberships",
        "identity.service_clients",
        "identity.sessions",
        "integration.dead_letters",
        "integration.idempotency_records",
        "integration.inbox_messages",
        "integration.outbox_messages",
        "platform.foundation_records",
        "platform.institution_boundaries",
        "security.authentication_events",
        "security.authentication_policy_versions",
        "security.locks",
    ];

    [Fact]
    public void The_model_matches_the_documented_tables()
    {
        string[] actual = CreateModel()
            .GetEntityTypes()
            .Select(entity => $"{entity.GetSchema()}.{entity.GetTableName()}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(ExpectedTables, actual);
    }

    [Fact]
    public void The_outbox_persists_w3c_context_for_background_publication()
    {
        IEntityType outbox = CreateModel().GetEntityTypes()
            .Single(entity => entity.GetTableName() == "outbox_messages");
        StoreObjectIdentifier table = StoreObjectIdentifier.Table("outbox_messages", "integration");

        Assert.Equal("trace_parent", outbox.FindProperty("TraceParent")!.GetColumnName(table));
        Assert.Equal("trace_state", outbox.FindProperty("TraceState")!.GetColumnName(table));
    }

    [Fact]
    public void Every_tenant_owned_entity_carries_the_institution_column_in_its_primary_key()
    {
        foreach (IEntityType entity in TenantOwned(CreateModel()))
        {
            string[] keyColumns = entity.FindPrimaryKey()!.Properties.Select(property => property.Name).ToArray();

            Assert.Contains("InstitutionId", keyColumns);
            Assert.False(
                entity.FindProperty("InstitutionId")!.IsNullable,
                $"{entity.ClrType.Name} must have a non-null institution column.");
        }
    }

    [Fact]
    public void Every_tenant_owned_entity_defines_a_query_filter()
    {
        foreach (IEntityType entity in TenantOwned(CreateModel()))
        {
            Assert.NotEmpty(entity.GetDeclaredQueryFilters());
        }
    }

    [Fact]
    public void Permission_catalogue_and_append_only_audit_tables_are_not_query_filtered()
    {
        string[] notTenantOwned = CreateModel()
            .GetEntityTypes()
            .Where(entity => !typeof(ITenantOwnedEntity).IsAssignableFrom(entity.ClrType))
            .Select(entity => entity.ClrType.FullName!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "CoreBanking.IdentityAccess.Infrastructure.Persistence.Access.PermissionRecord",
                "CoreBanking.IdentityAccess.Infrastructure.Persistence.Security.AuthorisationAuditRecord",
            ],
            notTenantOwned);
    }

    [Fact]
    public void A_foreign_key_to_a_tenant_owned_entity_includes_the_institution_column()
    {
        IModel model = CreateModel();
        int checkedForeignKeys = 0;

        foreach (IEntityType entity in TenantOwned(model))
        {
            foreach (IForeignKey foreignKey in entity.GetForeignKeys())
            {
                if (!typeof(ITenantOwnedEntity).IsAssignableFrom(foreignKey.PrincipalEntityType.ClrType))
                {
                    continue;
                }

                Assert.Contains("InstitutionId", foreignKey.Properties.Select(property => property.Name));
                checkedForeignKeys++;
            }
        }

        Assert.True(checkedForeignKeys >= 8, $"Only {checkedForeignKeys} tenant-aware foreign keys were found.");
    }

    [Fact]
    public void The_foundation_permission_catalogue_is_seeded()
    {
        IEntityType permissions = CreateModel().FindEntityType(
            "CoreBanking.IdentityAccess.Infrastructure.Persistence.Access.PermissionRecord")!;

        string[] seeded = permissions.GetSeedData()
            .Select(row => (string)row["PermissionCode"]!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "audit.event.view",
                "identity.access.review",
                "identity.identity.create",
                "identity.identity.update",
                "identity.identity.view",
                "identity.permission.assign",
                "identity.role.create",
                "institution.tenant.create",
                "platform.foundation.execute",
            ],
            seeded);
    }

    [Fact]
    public void Institution_boundary_is_unique_and_retains_the_source_event()
    {
        IEntityType boundary = CreateModel().GetEntityTypes()
            .Single(entity => entity.GetTableName() == "institution_boundaries");

        Assert.Equal(["InstitutionId"], boundary.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Contains(boundary.GetIndexes(), index =>
            index.IsUnique
            && index.Properties.Select(property => property.Name).SequenceEqual(["SourceEventId"]));
    }

    [Fact]
    public void Memberships_and_provider_links_are_tenant_filtered_and_cannot_reference_another_tenant_identity()
    {
        IModel model = CreateModel();
        IEntityType membership = model.GetEntityTypes()
            .Single(entity => entity.GetTableName() == "institution_memberships");
        IEntityType providerLink = model.GetEntityTypes()
            .Single(entity => entity.GetTableName() == "identity_links");

        Assert.NotEmpty(membership.GetDeclaredQueryFilters());
        Assert.NotEmpty(providerLink.GetDeclaredQueryFilters());
        Assert.Contains(membership.GetForeignKeys(), foreignKey =>
            foreignKey.Properties.Select(property => property.Name)
                .SequenceEqual(["InstitutionId", "IdentityId"]));
        Assert.Contains(providerLink.GetForeignKeys(), foreignKey =>
            foreignKey.Properties.Select(property => property.Name)
                .SequenceEqual(["InstitutionId", "IdentityId"]));
        Assert.Contains(providerLink.GetIndexes(), index =>
            index.IsUnique
            && index.Properties.Select(property => property.Name)
                .SequenceEqual(["InstitutionId", "Provider", "Realm", "SubjectFingerprint"]));
    }

    private static IEnumerable<IEntityType> TenantOwned(IModel model) =>
        model.GetEntityTypes().Where(entity => typeof(ITenantOwnedEntity).IsAssignableFrom(entity.ClrType));

    private static IModel CreateModel()
    {
        DbContextOptionsBuilder<IdentityAccessDbContext> options = new();
        options.UseNpgsql("Host=127.0.0.1;Port=5432;Database=model_only;Username=model_only;Password=model_only");

        using IdentityAccessDbContext context = new(options.Options, new UnestablishedTenantContext());

        // The runtime model is read-optimized and drops seed data, so the design-time model is used.
        return context.GetService<IDesignTimeModel>().Model;
    }

    /// <summary>A context with no institution, which is the state a query filter must fail closed in.</summary>
    private sealed class UnestablishedTenantContext : ITenantContext
    {
        public InstitutionId? CurrentInstitution => null;

        public void Establish(InstitutionId institutionId) => throw new NotSupportedException("No institution is available to this context.");

        public void Clear()
        {
        }
    }
}
