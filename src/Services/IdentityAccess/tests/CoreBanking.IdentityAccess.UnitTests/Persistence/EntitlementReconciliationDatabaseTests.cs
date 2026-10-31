using System;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.IdentityAccess.Infrastructure.Access;
using CoreBanking.IdentityAccess.Infrastructure.Persistence;
using CoreBanking.IdentityAccess.Infrastructure.Persistence.Access;
using CoreBanking.IdentityAccess.Infrastructure.Persistence.Identity;
using CoreBanking.IdentityAccess.Infrastructure.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CoreBanking.IdentityAccess.UnitTests.Persistence;

/// <summary>
/// Reconciliation detection against a real <c>identity_access_db</c>. Assertions use deltas because
/// the pass intentionally scans every institution. Excluded from the default run; executed by
/// <c>scripts/db-verify.ps1</c>. Decision: ADR-0010; slice 14.
/// </summary>
[Trait("Category", "Database")]
public sealed class EntitlementReconciliationDatabaseTests : IDisposable
{
    private const string ConnectionStringVariableName = "ConnectionStrings__IdentityAccess";

    private readonly string _connectionString;
    private readonly InstitutionId _institution = InstitutionId.From("recon-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly ActorId _identity = ActorId.From("recon-actor-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly TestTimeProvider _time = new(new DateTimeOffset(2026, 10, 28, 14, 0, 0, TimeSpan.Zero));

    public EntitlementReconciliationDatabaseTests()
    {
        string? configured = Environment.GetEnvironmentVariable(ConnectionStringVariableName);
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                $"Set {ConnectionStringVariableName} to the application principal connection string, or run scripts/db-verify.ps1.");
        }

        _connectionString = configured;
    }

    [Fact]
    public async Task The_pass_reports_each_kind_of_identity_state_that_needs_an_operator()
    {
        EntitlementReconciliationFindings before = await ReconcileAsync();

        await using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            SeedIdentity(context);
            SeedGrantWithoutVersion(context);
            SeedLapsedGrant(context);
            SeedExpiredClient(context);
            SeedStaleStepUpEvidence(context);
            await context.SaveChangesAsync(CancellationToken.None);
        }

        EntitlementReconciliationFindings after = await ReconcileAsync();

        Assert.True(after.GrantHoldersWithoutVersion > before.GrantHoldersWithoutVersion);
        Assert.True(after.ExpiredGrantsNotClosed > before.ExpiredGrantsNotClosed);
        Assert.True(after.ExpiredServiceClientsActive > before.ExpiredServiceClientsActive);
        Assert.True(after.StaleStepUpEvidence > before.StaleStepUpEvidence);
        Assert.False(after.IsClean);
    }

    public void Dispose()
    {
        using IdentityAccessDbContext context = CreateContext(_institution);
        context.StepUpEvidence.ExecuteDelete();
        context.TemporaryGrants.ExecuteDelete();
        context.ServiceClients.ExecuteDelete();
        context.IdentityRoles.ExecuteDelete();
        context.RolePermissions.ExecuteDelete();
        context.Roles.ExecuteDelete();
        context.EntitlementVersions.ExecuteDelete();
        context.Identities.ExecuteDelete();
    }

    private async Task<EntitlementReconciliationFindings> ReconcileAsync()
    {
        await using IdentityAccessDbContext context = CreateContext(null);
        return await new EntitlementReconciliationService(context).ReconcileAsync(_time.GetUtcNow(), CancellationToken.None);
    }

    private void SeedIdentity(IdentityAccessDbContext context)
    {
        DateTimeOffset now = _time.GetUtcNow();
        context.Identities.Add(new IdentityRecord
        {
            InstitutionId = _institution.Value,
            IdentityId = _identity.Value,
            ActorType = ActorType.Human,
            IdentityType = CoreBanking.IdentityAccess.Domain.Identities.IdentityKind.Staff,
            DisplayName = "Reconciliation actor",
            Status = CoreBanking.IdentityAccess.Domain.Identities.IdentityState.Active,
            CreatedAt = now,
            UpdatedAt = now,
            ConcurrencyVersion = 1,
        });
    }

    private void SeedGrantWithoutVersion(IdentityAccessDbContext context)
    {
        context.Roles.Add(new RoleRecord
        {
            InstitutionId = _institution.Value,
            RoleId = "recon-role",
            RoleCode = "recon-role",
            RoleName = "Reconciliation role",
            Version = 1,
            Status = RecordStatus.Active,
            CreatedAt = _time.GetUtcNow(),
            UpdatedAt = _time.GetUtcNow(),
        });
        context.RolePermissions.Add(new RolePermissionRecord
        {
            InstitutionId = _institution.Value,
            RoleId = "recon-role",
            PermissionCode = "audit.event.view",
        });
        context.IdentityRoles.Add(new IdentityRoleRecord
        {
            InstitutionId = _institution.Value,
            IdentityId = _identity.Value,
            RoleId = "recon-role",
            ValidFrom = _time.GetUtcNow().AddMinutes(-5),
            AssignedBy = "seed",
        });
    }

    private void SeedLapsedGrant(IdentityAccessDbContext context) =>
        context.TemporaryGrants.Add(new TemporaryGrantRecord
        {
            InstitutionId = _institution.Value,
            TemporaryGrantId = Guid.NewGuid().ToString("N"),
            IdentityId = _identity.Value,
            PermissionCode = "audit.event.view",
            ValidFrom = _time.GetUtcNow().AddHours(-2),
            ValidTo = _time.GetUtcNow().AddHours(-1),
            GrantedBy = "seed",
            Reason = "Lapsed grant left for reconciliation",
        });

    private void SeedExpiredClient(IdentityAccessDbContext context) =>
        context.ServiceClients.Add(new ServiceClientRecord
        {
            InstitutionId = _institution.Value,
            ServiceClientId = Guid.NewGuid().ToString("N"),
            ClientName = "Expired client",
            Audience = "corebanking.identity-access",
            CredentialReference = "vault://identity/expired",
            CredentialExpiresAt = _time.GetUtcNow().AddMinutes(-10),
            Status = RecordStatus.Active,
            CreatedAt = _time.GetUtcNow().AddDays(-30),
            UpdatedAt = _time.GetUtcNow().AddDays(-30),
        });

    private void SeedStaleStepUpEvidence(IdentityAccessDbContext context) =>
        context.StepUpEvidence.Add(new StepUpEvidenceRecord
        {
            InstitutionId = _institution.Value,
            EvidenceId = Guid.NewGuid().ToString("N"),
            IdentityId = _identity.Value,
            SessionId = "session-1",
            Purpose = "identity.identity.create",
            FactorKind = "totp",
            CorrelationId = "correlation-recon",
            IssuedAt = _time.GetUtcNow().AddMinutes(-10),
            ExpiresAt = _time.GetUtcNow().AddMinutes(-5),
        });

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

    private sealed class TestTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public TestTimeProvider(DateTimeOffset start) => _now = start;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
