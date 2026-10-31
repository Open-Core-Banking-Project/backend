using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.IdentityAccess.Contracts;
using CoreBanking.IdentityAccess.Infrastructure.Identity;
using CoreBanking.IdentityAccess.Infrastructure.Persistence;
using CoreBanking.IdentityAccess.Infrastructure.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CoreBanking.IdentityAccess.UnitTests.Persistence;

/// <summary>
/// Service client administration and validation against a real <c>identity_access_db</c>.
/// Excluded from the default run; executed by <c>scripts/db-verify.ps1</c>.
/// Decision: ADR-0008; slice 13.
/// </summary>
[Trait("Category", "Database")]
public sealed class ServiceClientDatabaseTests : IDisposable
{
    private const string ConnectionStringVariableName = "ConnectionStrings__IdentityAccess";

    private readonly string _connectionString;
    private readonly InstitutionId _institution = InstitutionId.From("client-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly InstitutionId _otherInstitution = InstitutionId.From("client-other-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly ActorId _admin = ActorId.From("client-admin-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly TestTimeProvider _time = new(new DateTimeOffset(2026, 10, 28, 13, 0, 0, TimeSpan.Zero));

    public ServiceClientDatabaseTests()
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
    public async Task A_registered_client_is_active_and_its_reference_is_not_returned()
    {
        await using IdentityAccessDbContext context = CreateContext(_institution);

        ServiceClientResultV1 result = await CreateService(context).RegisterAsync(
            _institution,
            _admin,
            "Core ledger client",
            "corebanking.identity-access",
            "vault://identity/core-ledger",
            _time.GetUtcNow().AddDays(30),
            CancellationToken.None);

        Assert.Equal(ServiceClientContractV1.StatusActive, result.Status);
        Assert.Equal("corebanking.identity-access", result.Audience);

        await using IdentityAccessDbContext verification = CreateContext(_institution);
        ServiceClientRecord record = await verification.ServiceClients.SingleAsync();
        Assert.Equal("vault://identity/core-ledger", record.CredentialReference);
        Assert.DoesNotContain(record.CredentialReference, result.ToString() ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rotation_keeps_the_previous_reference_until_the_overlap_ends()
    {
        string clientId;
        await using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            clientId = (await CreateService(context).RegisterAsync(
                _institution, _admin, "Rotating client", "corebanking.identity-access",
                "vault://identity/v1", _time.GetUtcNow().AddDays(30), CancellationToken.None)).ServiceClientId;
        }

        await using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            await CreateService(context).RotateAsync(
                _institution, clientId, "vault://identity/v2", _time.GetUtcNow().AddDays(60),
                _time.GetUtcNow().AddHours(2), CancellationToken.None);
        }

        await using IdentityAccessDbContext verification = CreateContext(_institution);
        ServiceClientRecord record = await verification.ServiceClients.SingleAsync();
        Assert.Equal("vault://identity/v2", record.CredentialReference);
        Assert.Equal("vault://identity/v1", record.PreviousCredentialReference);
        Assert.Equal(_time.GetUtcNow().AddHours(2), record.PreviousValidUntil);
    }

    [Fact]
    public async Task An_expired_client_is_not_usable()
    {
        string clientId;
        await using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            clientId = (await CreateService(context).RegisterAsync(
                _institution, _admin, "Expiring client", "corebanking.identity-access",
                "vault://identity/short", _time.GetUtcNow().AddMinutes(5), CancellationToken.None)).ServiceClientId;
        }

        Assert.True(await IsUsableAsync(clientId));

        _time.Advance(TimeSpan.FromMinutes(6));

        Assert.False(await IsUsableAsync(clientId));
    }

    [Fact]
    public async Task A_revoked_client_is_not_usable_and_cannot_rotate()
    {
        string clientId;
        await using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            clientId = (await CreateService(context).RegisterAsync(
                _institution, _admin, "Revoked client", "corebanking.identity-access",
                "vault://identity/revoked", _time.GetUtcNow().AddDays(30), CancellationToken.None)).ServiceClientId;
        }

        await using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            await CreateService(context).RevokeAsync(_institution, clientId, "Compromised credential reference", CancellationToken.None);
        }

        Assert.False(await IsUsableAsync(clientId));

        await using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            ServiceClientAdministrationException exception = await Assert.ThrowsAsync<ServiceClientAdministrationException>(() =>
                CreateService(context).RotateAsync(
                    _institution, clientId, "vault://identity/next", _time.GetUtcNow().AddDays(30), null, CancellationToken.None));
            Assert.Equal(ServiceClientContractV1.AlreadyRevoked, exception.Code);
        }
    }

    [Fact]
    public async Task A_client_in_another_institution_is_invisible()
    {
        string clientId;
        await using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            clientId = (await CreateService(context).RegisterAsync(
                _institution, _admin, "Tenant client", "corebanking.identity-access",
                "vault://identity/tenant", _time.GetUtcNow().AddDays(30), CancellationToken.None)).ServiceClientId;
        }

        await using (IdentityAccessDbContext other = CreateContext(_otherInstitution))
        {
            ServiceClientAdministrationException exception = await Assert.ThrowsAsync<ServiceClientAdministrationException>(() =>
                CreateService(other).GetAsync(clientId, CancellationToken.None));
            Assert.Equal(ServiceClientContractV1.NotFound, exception.Code);
        }

        await using IdentityAccessDbContext otherValidation = CreateContext(_otherInstitution);
        bool usable = await CreateService(otherValidation).IsUsableAsync(
            new AuthenticatedActor
            {
                Id = ActorId.From(clientId),
                Type = ActorType.Service,
                InstitutionId = _otherInstitution,
                EntitlementVersion = 0,
                Strength = AuthenticationStrength.SingleFactor,
            },
            CancellationToken.None);

        Assert.False(usable);
    }

    public void Dispose()
    {
        using IdentityAccessDbContext context = CreateContext(_institution);
        context.ServiceClients.ExecuteDelete();
    }

    private async Task<bool> IsUsableAsync(string clientId)
    {
        await using IdentityAccessDbContext context = CreateContext(_institution);
        return await CreateService(context).IsUsableAsync(
            new AuthenticatedActor
            {
                Id = ActorId.From(clientId),
                Type = ActorType.Service,
                InstitutionId = _institution,
                EntitlementVersion = 0,
                Strength = AuthenticationStrength.SingleFactor,
            },
            CancellationToken.None);
    }

    private ServiceClientAdministrationService CreateService(IdentityAccessDbContext context) => new(context, _time);

    private IdentityAccessDbContext CreateContext(InstitutionId institution)
    {
        AmbientTenantContext tenant = new();
        tenant.Establish(institution);

        DbContextOptionsBuilder<IdentityAccessDbContext> options = new();
        options.UseNpgsql(_connectionString);

        return new IdentityAccessDbContext(options.Options, tenant);
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;

        public TestTimeProvider(DateTimeOffset start) => _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }
}
