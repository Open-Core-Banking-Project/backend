using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.CoreLedger.Application.Accounts;
using CoreBanking.CoreLedger.Domain.Accounts;
using CoreBanking.CoreLedger.Infrastructure.Accounts;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CoreBanking.CoreLedger.UnitTests.Persistence;

/// <summary>
/// The lien lifecycle against a real <c>core_ledger_db</c>: immutable claimed amount, separate outstanding,
/// append-only movements, the uncovered acknowledgement and the dual-control release. Executed by
/// <c>scripts/db-verify.ps1</c>. Decision: P1-E04-D11.
/// </summary>
[Trait("Category", "Database")]
public sealed class AccountLienDatabaseTests : IDisposable
{
    private static readonly ActorId Maker = ActorId.From("db-maker");
    private static readonly ActorId Checker = ActorId.From("db-checker");

    private readonly CoreLedgerDatabaseScope _scope = new();
    private readonly FixedTimeProvider _time;

    public AccountLienDatabaseTests() => _time = new FixedTimeProvider(CoreLedgerDatabaseScope.SeededAt);

    public void Dispose() => _scope.Dispose();

    [Fact]
    public async Task A_lien_records_an_immutable_amount_and_the_ledger_availability_position()
    {
        string accountId = await OpenAccountAsync("application-lien-1", "customer-lien-1");
        AmbientTenantContext tenantContext = _scope.NewTenant();
        await using CoreLedgerDbContext context = _scope.CreateContext(tenantContext);
        AccountLienService service = CreateService(context, tenantContext, available: 1000m);

        AccountLienResult created = await service.CreateAsync(
            Lien(accountId, "lien-1", 200m, "customer"),
            CancellationToken.None);

        Assert.Equal("active", created.Status);
        Assert.Equal(200m, created.Amount);
        Assert.Equal(200m, created.OutstandingAmount);
        Assert.Equal("customer", created.PriorityCode);
        Assert.False(created.Availability.ExceedsAvailable);
        Assert.Equal(200m, created.Availability.TotalOutstandingLiens);
        Assert.Equal(1000m, created.Availability.ReportedAvailable);
        Assert.Equal(0m, created.Availability.UncoveredLienAmount);

        await using CoreLedgerDbContext verification = _scope.CreateContext();
        AccountLienRecord stored = await verification.AccountLiens.SingleAsync();
        Assert.Equal(200m, stored.Amount);
        Assert.Equal(200m, stored.OutstandingAmount);
        Assert.Equal("NGN", stored.CurrencyCode);
    }

    [Fact]
    public async Task An_uncovered_lien_needs_an_explicit_approver_acknowledgement()
    {
        string accountId = await OpenAccountAsync("application-lien-2", "customer-lien-2");
        AmbientTenantContext tenantContext = _scope.NewTenant();
        await using CoreLedgerDbContext context = _scope.CreateContext(tenantContext);
        AccountLienService service = CreateService(context, tenantContext, available: 100m);

        AccountApplicationRejectedException refused = await Assert.ThrowsAsync<AccountApplicationRejectedException>(() =>
            service.CreateAsync(Lien(accountId, "lien-uncovered", 500m, "customer"), CancellationToken.None));
        Assert.Equal(AccountLienPolicy.UncoveredCode, refused.Code);

        AccountLienResult acknowledged = await service.CreateAsync(
            Lien(accountId, "lien-acknowledged", 500m, "customer") with { UncoveredAcknowledgedByApproverId = true },
            CancellationToken.None);

        Assert.True(acknowledged.Availability.ExceedsAvailable);
        Assert.Equal(400m, acknowledged.Availability.UncoveredLienAmount);
    }

    [Fact]
    public async Task A_partial_release_is_an_append_only_movement_and_never_edits_the_claim()
    {
        string accountId = await OpenAccountAsync("application-lien-3", "customer-lien-3");
        AmbientTenantContext tenantContext = _scope.NewTenant();
        await using CoreLedgerDbContext context = _scope.CreateContext(tenantContext);
        AccountLienService service = CreateService(context, tenantContext, available: 1000m);

        AccountLienResult created = await service.CreateAsync(
            Lien(accountId, "lien-3", 200m, "customer"),
            CancellationToken.None);

        AccountLienResult released = await service.ReleaseAsync(
            new ReleaseAccountLienCommand(
                _scope.Institution,
                Maker,
                "lien-release-1",
                accountId,
                created.LienId,
                50m,
                "customer_request",
                null,
                null,
                1L,
                "correlation-lien-release-1"),
            CancellationToken.None);

        Assert.Equal(200m, released.Amount);
        Assert.Equal(150m, released.OutstandingAmount);

        await using CoreLedgerDbContext verification = _scope.CreateContext();
        AccountLienRecord stored = await verification.AccountLiens.SingleAsync();
        Assert.Equal(200m, stored.Amount);
        Assert.Equal(150m, stored.OutstandingAmount);

        AccountLienMovementRecord movement = await verification.AccountLienMovements.SingleAsync();
        Assert.Equal(50m, movement.Amount);
        Assert.Equal(150m, movement.OutstandingAfter);
    }

    [Fact]
    public async Task A_release_above_the_outstanding_amount_is_refused()
    {
        string accountId = await OpenAccountAsync("application-lien-4", "customer-lien-4");
        AmbientTenantContext tenantContext = _scope.NewTenant();
        await using CoreLedgerDbContext context = _scope.CreateContext(tenantContext);
        AccountLienService service = CreateService(context, tenantContext, available: 1000m);

        AccountLienResult created = await service.CreateAsync(
            Lien(accountId, "lien-4", 200m, "customer"),
            CancellationToken.None);

        AccountApplicationRejectedException refused = await Assert.ThrowsAsync<AccountApplicationRejectedException>(() =>
            service.ReleaseAsync(
                new ReleaseAccountLienCommand(
                    _scope.Institution,
                    Maker,
                    "lien-release-over",
                    accountId,
                    created.LienId,
                    500m,
                    "customer_request",
                    null,
                    null,
                    1L,
                    "correlation-lien-release-over"),
                CancellationToken.None));

        Assert.Equal(AccountLienPolicy.OverReleaseCode, refused.Code);

        await using CoreLedgerDbContext verification = _scope.CreateContext();
        Assert.Equal(200m, (await verification.AccountLiens.SingleAsync()).OutstandingAmount);
        Assert.Empty(verification.AccountLienMovements);
    }

    [Fact]
    public async Task A_statutory_lien_needs_a_second_approver_to_release()
    {
        string accountId = await OpenAccountAsync("application-lien-5", "customer-lien-5");
        AmbientTenantContext tenantContext = _scope.NewTenant();
        await using CoreLedgerDbContext context = _scope.CreateContext(tenantContext);
        AccountLienService service = CreateService(context, tenantContext, available: 1000m);

        AccountLienResult created = await service.CreateAsync(
            Lien(accountId, "lien-5", 200m, "statutory_first") with { AuthorityReference = "AUTHORITY-1" },
            CancellationToken.None);

        AccountApplicationRejectedException missingApproval = await Assert.ThrowsAsync<AccountApplicationRejectedException>(() =>
            service.ReleaseAsync(
                new ReleaseAccountLienCommand(
                    _scope.Institution,
                    Maker,
                    "lien-release-no-approval",
                    accountId,
                    created.LienId,
                    200m,
                    "authority_lifted",
                    "AUTHORITY-1",
                    null,
                    1L,
                    "correlation-lien-release-2"),
                CancellationToken.None));
        Assert.Equal("account_lien_release_requires_approval", missingApproval.Code);

        AccountApplicationRejectedException sameMaker = await Assert.ThrowsAsync<AccountApplicationRejectedException>(() =>
            service.ReleaseAsync(
                new ReleaseAccountLienCommand(
                    _scope.Institution,
                    Maker,
                    "lien-release-same-maker",
                    accountId,
                    created.LienId,
                    200m,
                    "authority_lifted",
                    "AUTHORITY-1",
                    "APPROVAL-1",
                    1L,
                    "correlation-lien-release-3"),
                CancellationToken.None));
        Assert.Equal("account_lien_dual_control_required", sameMaker.Code);

        AccountLienResult released = await service.ReleaseAsync(
            new ReleaseAccountLienCommand(
                _scope.Institution,
                Checker,
                "lien-release-checker",
                accountId,
                created.LienId,
                200m,
                "authority_lifted",
                "AUTHORITY-1",
                "APPROVAL-1",
                1L,
                "correlation-lien-release-4"),
            CancellationToken.None);

        Assert.Equal(0m, released.OutstandingAmount);
        Assert.Equal("released", released.Status);
    }

    private async Task<string> OpenAccountAsync(string applicationId, string customerId) =>
        await AccountDatabaseScenario.OpenAccountAsync(_scope, applicationId, customerId, Maker, _time);

    private AccountLienService CreateService(
        CoreLedgerDbContext context,
        AmbientTenantContext tenantContext,
        decimal available) =>
        new(
            context,
            new IdempotencyService(context, _time),
            tenantContext,
            _time,
            new FixedLedgerAvailabilitySource(available));

    private CreateAccountLienCommand Lien(string accountId, string key, decimal amount, string priorityCode) =>
        new(
            _scope.Institution,
            Maker,
            key,
            accountId,
            "request-" + key,
            amount,
            "NGN",
            priorityCode,
            1,
            "customer_request",
            null,
            CoreLedgerDatabaseScope.SeededAt,
            null,
            false,
            "correlation-" + key);

    private sealed class FixedLedgerAvailabilitySource : ILedgerAvailabilitySource
    {
        private readonly decimal _available;

        internal FixedLedgerAvailabilitySource(decimal available) => _available = available;

        public Task<LedgerAvailabilitySnapshot> GetAsync(
            InstitutionId institutionId,
            string accountId,
            string currencyCode,
            CancellationToken cancellationToken) =>
            Task.FromResult(new LedgerAvailabilitySnapshot(
                _available,
                0m,
                _available,
                "ledger-availability-1",
                CoreLedgerDatabaseScope.SeededAt));
    }
}
