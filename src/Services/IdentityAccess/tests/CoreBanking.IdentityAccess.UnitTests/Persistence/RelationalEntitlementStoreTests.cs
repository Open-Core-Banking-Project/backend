using System;
using System.Linq;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.BuildingBlocks.Application.Tenancy;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.IdentityAccess.Domain.Identities;
using CoreBanking.IdentityAccess.Infrastructure.Persistence;
using CoreBanking.IdentityAccess.Infrastructure.Persistence.Access;
using CoreBanking.IdentityAccess.Infrastructure.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CoreBanking.IdentityAccess.UnitTests.Persistence;

/// <summary>
/// Exercises the relational entitlement store and the tenant boundary against a real
/// <c>identity_access_db</c>. These tests need the local infrastructure stack and are excluded
/// from the default run; <c>scripts/db-verify.ps1</c> applies the migrations and then runs them as
/// the application principal, which also proves that principal can work with its own schema.
/// Decision: ADR-0004, ADR-0009.
/// </summary>
[Trait("Category", "Database")]
public sealed class RelationalEntitlementStoreTests : IDisposable
{
    private const string ConnectionStringVariableName = "ConnectionStrings__IdentityAccess";
    private const string PermissionCode = "platform.foundation.execute";

    private readonly string _connectionString;
    private readonly InstitutionId _institution;
    private readonly InstitutionId _otherInstitution;
    private readonly ActorId _actor = ActorId.From("test-actor");

    public RelationalEntitlementStoreTests()
    {
        string? configured = Environment.GetEnvironmentVariable(ConnectionStringVariableName);
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                $"Set {ConnectionStringVariableName} to the application principal connection string, or run scripts/db-verify.ps1.");
        }

        _connectionString = configured;
        string suffix = Guid.NewGuid().ToString("N")[..12];
        _institution = InstitutionId.From($"test-institution-{suffix}");
        _otherInstitution = InstitutionId.From($"other-institution-{suffix}");
    }

    [Fact]
    public void An_assigned_role_produces_one_grant_scoped_to_the_institution()
    {
        SeedActor(_institution);
        SeedRoleWithPermission(_institution, "role-a");

        EntitlementSnapshot? snapshot = CreateStore(_institution).FindSnapshot(_actor, _institution);

        Assert.NotNull(snapshot);
        PermissionGrant grant = Assert.Single(snapshot!.Grants);
        Assert.Equal(PermissionCode, grant.Permission.Value);
        Assert.Equal(_institution, grant.Scope.InstitutionId);
    }

    [Fact]
    public void An_actor_without_an_entitlement_version_has_no_snapshot()
    {
        SeedIdentity(_institution);

        Assert.Null(CreateStore(_institution).FindSnapshot(_actor, _institution));
    }

    [Fact]
    public void Grants_of_another_institution_are_invisible()
    {
        SeedActor(_institution);
        SeedRoleWithPermission(_institution, "role-a");
        SeedActor(_otherInstitution);
        SeedRoleWithPermission(_otherInstitution, "role-b");

        EntitlementSnapshot? snapshot = CreateStore(_institution).FindSnapshot(_actor, _institution);

        Assert.NotNull(snapshot);
        PermissionGrant grant = Assert.Single(snapshot!.Grants);
        Assert.Equal(_institution, grant.Scope.InstitutionId);
    }

    [Fact]
    public void A_query_without_an_established_institution_reads_nothing()
    {
        SeedActor(_institution);
        SeedRoleWithPermission(_institution, "role-a");

        using IdentityAccessDbContext context = CreateContext(institution: null);

        Assert.Empty(context.EntitlementVersions.ToList());
        Assert.Empty(context.IdentityRoles.ToList());
        Assert.Equal(0L, new RelationalEntitlementStore(context, TimeProvider.System).GetCurrentVersion(_actor, _institution));
    }

    [Fact]
    public void Incrementing_the_entitlement_version_invalidates_the_issued_token()
    {
        SeedActor(_institution);
        SeedRoleWithPermission(_institution, "role-a");

        long issuedVersion = CreateStore(_institution).GetCurrentVersion(_actor, _institution);
        Assert.Equal(1L, issuedVersion);

        using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            EntitlementVersionRecord version = context.EntitlementVersions.Single();
            version.Version = 2L;
            version.UpdatedAt = DateTimeOffset.UtcNow;
            context.SaveChanges();
        }

        AuthorisationPolicy policy = new(
            CreateStore(_institution),
            CreateStore(_institution),
            TimeProvider.System,
            TimeSpan.FromSeconds(60));

        AuthorisationDecision decision = policy.Evaluate(new AuthorisationRequest
        {
            Requirement = PermissionRequirement.For(PermissionIdentifier.Parse(PermissionCode)),
            Scope = ResourceScope.ForInstitution(_institution),
            Actor = new AuthenticatedActor
            {
                Id = _actor,
                Type = ActorType.Human,
                InstitutionId = _institution,
                EntitlementVersion = issuedVersion,
                Strength = AuthenticationStrength.MultiFactor,
            },
        });

        Assert.False(decision.IsAllowed);
        Assert.Equal(DenialReason.StaleEntitlement, decision.Reason);
    }

    [Fact]
    public void An_expired_role_assignment_is_not_a_grant()
    {
        SeedActor(_institution);
        SeedRoleWithPermission(_institution, "role-a");

        using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            IdentityRoleRecord assignment = context.IdentityRoles.Single();
            assignment.ValidTo = DateTimeOffset.UtcNow.AddMinutes(-1);
            context.SaveChanges();
        }

        EntitlementSnapshot? snapshot = CreateStore(_institution).FindSnapshot(_actor, _institution);

        Assert.NotNull(snapshot);
        Assert.Empty(snapshot!.Grants);
    }

    public void Dispose()
    {
        Discard(_institution);
        Discard(_otherInstitution);
    }

    private RelationalEntitlementStore CreateStore(InstitutionId institution) =>
        new(CreateContext(institution), TimeProvider.System);

    private IdentityAccessDbContext CreateContext(InstitutionId? institution)
    {
        AmbientTenantContext tenant = new();
        if (institution is not null)
        {
            tenant.Establish(institution);
        }

        DbContextOptionsBuilder<IdentityAccessDbContext> options = new();
        options.UseNpgsql(_connectionString);

        return new IdentityAccessDbContext(options.Options, tenant);
    }

    private void SeedIdentity(InstitutionId institution)
    {
        using IdentityAccessDbContext context = CreateContext(institution);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        context.Identities.Add(new IdentityRecord
        {
            InstitutionId = institution.Value,
            IdentityId = _actor.Value,
            ActorType = ActorType.Human,
            DisplayName = "Relational store test actor",
            IdentityType = IdentityKind.Staff,
            Status = IdentityState.Active,
            CreatedAt = now,
            UpdatedAt = now,
            ConcurrencyVersion = 1,
        });

        context.SaveChanges();
    }

    private void SeedEntitlementVersion(InstitutionId institution, long version)
    {
        using IdentityAccessDbContext context = CreateContext(institution);

        context.EntitlementVersions.Add(new EntitlementVersionRecord
        {
            InstitutionId = institution.Value,
            IdentityId = _actor.Value,
            Version = version,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        context.SaveChanges();
    }

    private void SeedActor(InstitutionId institution)
    {
        SeedIdentity(institution);
        SeedEntitlementVersion(institution, 1L);
    }

    private void SeedRoleWithPermission(InstitutionId institution, string roleId)
    {
        using IdentityAccessDbContext context = CreateContext(institution);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        context.Roles.Add(new RoleRecord
        {
            InstitutionId = institution.Value,
            RoleId = roleId,
            RoleCode = roleId,
            RoleName = roleId,
            Version = 1L,
            Status = RecordStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        });

        context.RolePermissions.Add(new RolePermissionRecord
        {
            InstitutionId = institution.Value,
            RoleId = roleId,
            PermissionCode = PermissionCode,
        });

        context.IdentityRoles.Add(new IdentityRoleRecord
        {
            InstitutionId = institution.Value,
            IdentityId = _actor.Value,
            RoleId = roleId,
            ValidFrom = now.AddMinutes(-5),
            AssignedBy = "test",
        });

        context.SaveChanges();
    }

    private void Discard(InstitutionId institution)
    {
        using IdentityAccessDbContext context = CreateContext(institution);

        context.TemporaryGrants.ExecuteDelete();
        context.IdentityRoles.ExecuteDelete();
        context.RolePermissions.ExecuteDelete();
        context.PermissionConditions.ExecuteDelete();
        context.Roles.ExecuteDelete();
        context.Sessions.ExecuteDelete();
        context.ServiceClients.ExecuteDelete();
        context.EntitlementVersions.ExecuteDelete();
        context.Identities.ExecuteDelete();
    }
}
