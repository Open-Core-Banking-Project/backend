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
        "identity.identities",
        "identity.service_clients",
        "identity.sessions",
        "integration.dead_letters",
        "integration.idempotency_records",
        "integration.inbox_messages",
        "integration.outbox_messages",
        "platform.foundation_records",
        "security.authentication_events",
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
    public void The_permission_catalogue_is_the_only_table_without_an_institution_column()
    {
        string[] notTenantOwned = CreateModel()
            .GetEntityTypes()
            .Where(entity => !typeof(ITenantOwnedEntity).IsAssignableFrom(entity.ClrType))
            .Select(entity => entity.ClrType.FullName!)
            .ToArray();

        Assert.Equal(["CoreBanking.IdentityAccess.Infrastructure.Persistence.Access.PermissionRecord"], notTenantOwned);
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
                "identity.permission.assign",
                "identity.role.create",
                "institution.tenant.create",
                "platform.foundation.execute",
            ],
            seeded);
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
