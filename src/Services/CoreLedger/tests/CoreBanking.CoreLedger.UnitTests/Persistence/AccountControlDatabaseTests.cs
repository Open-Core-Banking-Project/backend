using System;
using System.Collections.Generic;
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
/// Account controls against a real <c>core_ledger_db</c>: the scoped denial, the stale-version review, the
/// dual-control release and the concurrent control-version allocation. Executed by <c>scripts/db-verify.ps1</c>.
/// Decision: P1-E04-D10.
/// </summary>
[Trait("Category", "Database")]
public sealed class AccountControlDatabaseTests : IDisposable
{
    private static readonly ActorId Maker = ActorId.From("db-maker");

    private readonly CoreLedgerDatabaseScope _scope = new();
    private readonly FixedTimeProvider _time;

    public AccountControlDatabaseTests() => _time = new FixedTimeProvider(CoreLedgerDatabaseScope.SeededAt);

    public void Dispose() => _scope.Dispose();

    [Fact]
    public async Task A_restriction_denies_only_inside_its_declared_scope()
    {
        string accountId = await AccountDatabaseScenario.OpenAccountAsync(
            _scope, "application-control-1", "customer-control-1", Maker, _time);

        AmbientTenantContext tenantContext = _scope.NewTenant();
        await using CoreLedgerDbContext context = _scope.CreateContext(tenantContext);
        AccountControlService service = CreateService(context, tenantContext);

        AccountControlChangeResult applied = await service.ApplyRestrictionAsync(
            Restriction(accountId, "restriction-1"),
            CancellationToken.None);

        Assert.Equal("active", applied.Status);
        Assert.Equal(1L, applied.ControlVersion);

        AccountControlEvaluationResult denied = await service.EvaluateAsync(
            Evaluate(accountId, "intent-1", "debit", "purchase", "pos", applied.ControlVersion),
            CancellationToken.None);
        Assert.Equal(AccountControlOutcomes.Deny, denied.Outcome);
        Assert.Contains(denied.ReasonCodes, code => code.StartsWith("control_denied", StringComparison.Ordinal));
        Assert.Single(denied.EffectiveControls);

        AccountControlEvaluationResult outsideChannel = await service.EvaluateAsync(
            Evaluate(accountId, "intent-2", "debit", "purchase", "web", applied.ControlVersion),
            CancellationToken.None);
        Assert.Equal(AccountControlOutcomes.Allow, outsideChannel.Outcome);

        AccountControlEvaluationResult outsideDirection = await service.EvaluateAsync(
            Evaluate(accountId, "intent-3", "credit", "purchase", "pos", applied.ControlVersion),
            CancellationToken.None);
        Assert.Equal(AccountControlOutcomes.Allow, outsideDirection.Outcome);

        await using CoreLedgerDbContext verification = _scope.CreateContext();
        AccountRestrictionRecord stored = await verification.AccountRestrictions.SingleAsync();
        Assert.Equal("channel_specific", stored.RestrictionTypeCode);
        Assert.Equal("active", stored.Status);
        Assert.Equal(1L, stored.ControlVersion);
    }

    [Fact]
    public async Task An_evaluation_from_a_stale_control_version_goes_to_review_without_exposing_the_decision()
    {
        string accountId = await AccountDatabaseScenario.OpenAccountAsync(
            _scope, "application-control-2", "customer-control-2", Maker, _time);

        AmbientTenantContext tenantContext = _scope.NewTenant();
        await using CoreLedgerDbContext context = _scope.CreateContext(tenantContext);
        AccountControlService service = CreateService(context, tenantContext);

        AccountControlChangeResult applied = await service.ApplyRestrictionAsync(
            Restriction(accountId, "restriction-2"),
            CancellationToken.None);

        AccountControlEvaluationResult stale = await service.EvaluateAsync(
            Evaluate(accountId, "intent-stale", "debit", "purchase", "pos", applied.ControlVersion - 1),
            CancellationToken.None);

        Assert.Equal(AccountControlOutcomes.Review, stale.Outcome);
        Assert.Equal(AccountControlOutcomes.VersionStaleCode, stale.ReasonCodes.First());
        Assert.Equal(applied.ControlVersion, stale.ControlVersion);
    }

    [Fact]
    public async Task A_regulatory_freeze_is_released_only_with_an_approval_reference()
    {
        string accountId = await AccountDatabaseScenario.OpenAccountAsync(
            _scope, "application-control-3", "customer-control-3", Maker, _time);

        AmbientTenantContext tenantContext = _scope.NewTenant();
        await using CoreLedgerDbContext context = _scope.CreateContext(tenantContext);
        AccountControlService service = CreateService(context, tenantContext);

        AccountControlChangeResult applied = await service.ApplyFreezeAsync(
            new ApplyAccountFreezeCommand(
                _scope.Institution,
                Maker,
                "freeze-1",
                accountId,
                "request-freeze-1",
                "regulatory",
                "court_order",
                "COURT-ORDER-1",
                CoreLedgerDatabaseScope.SeededAt,
                null,
                "correlation-freeze-1"),
            CancellationToken.None);
        Assert.Equal("regulatory", applied.ControlTypeCode);

        AccountControlEvaluationResult evaluated = await service.EvaluateAsync(
            Evaluate(accountId, "intent-freeze", "debit", "purchase", "pos", applied.ControlVersion),
            CancellationToken.None);
        Assert.Equal(AccountControlOutcomes.Review, evaluated.Outcome);

        AccountApplicationRejectedException refused = await Assert.ThrowsAsync<AccountApplicationRejectedException>(() =>
            service.ReleaseAsync(
                new ReleaseAccountControlCommand(
                    _scope.Institution,
                    Maker,
                    "freeze-release-denied",
                    accountId,
                    applied.ControlId,
                    "freeze",
                    "court_order_lifted",
                    null,
                    "correlation-freeze-2"),
                CancellationToken.None));
        Assert.Equal("account_control_release_requires_approval", refused.Code);

        AccountControlChangeResult released = await service.ReleaseAsync(
            new ReleaseAccountControlCommand(
                _scope.Institution,
                Maker,
                "freeze-release-1",
                accountId,
                applied.ControlId,
                "freeze",
                "court_order_lifted",
                "APPROVAL-1",
                "correlation-freeze-3"),
            CancellationToken.None);

        Assert.Equal("released", released.Status);
        Assert.Equal(applied.ControlVersion + 1, released.ControlVersion);

        await using CoreLedgerDbContext verification = _scope.CreateContext();
        AccountFreezeRecord stored = await verification.AccountFreezes.SingleAsync();
        Assert.Equal("released", stored.Status);
        Assert.NotNull(stored.ReleasedAt);
    }

    [Fact]
    public async Task Concurrent_control_changes_never_share_a_control_version()
    {
        string accountId = await AccountDatabaseScenario.OpenAccountAsync(
            _scope, "application-control-4", "customer-control-4", Maker, _time);

        Task<AccountControlChangeResult>[] changes = Enumerable.Range(1, 4)
            .Select(index => Task.Run(async () =>
            {
                AmbientTenantContext tenantContext = _scope.NewTenant();
                await using CoreLedgerDbContext context = _scope.CreateContext(tenantContext);
                return await CreateService(context, tenantContext).ApplyRestrictionAsync(
                    Restriction(accountId, "restriction-concurrent-" + index),
                    CancellationToken.None);
            }))
            .ToArray();

        AccountControlChangeResult[] results = await Task.WhenAll(changes);

        Assert.Equal(4, results.Select(result => result.ControlVersion).Distinct().Count());

        await using CoreLedgerDbContext verification = _scope.CreateContext();
        AccountRestrictionRecord[] stored = await verification.AccountRestrictions.ToArrayAsync();
        Assert.Equal(4, stored.Length);
        Assert.Equal(4, stored.Select(record => record.ControlVersion).Distinct().Count());
    }

    private AccountControlService CreateService(CoreLedgerDbContext context, AmbientTenantContext tenantContext) =>
        new(context, new IdempotencyService(context, _time), tenantContext, _time);

    private ApplyAccountRestrictionCommand Restriction(string accountId, string key) =>
        new(
            _scope.Institution,
            Maker,
            key,
            accountId,
            "request-" + key,
            "channel_specific",
            "debit",
            ["pos"],
            [],
            "customer_request",
            null,
            CoreLedgerDatabaseScope.SeededAt,
            null,
            "correlation-" + key);

    private EvaluateAccountControlsCommand Evaluate(
        string accountId,
        string intentId,
        string direction,
        string transactionType,
        string channel,
        long knownControlVersion) =>
        new(
            _scope.Institution,
            accountId,
            intentId,
            direction,
            transactionType,
            channel,
            new MonetaryAmountView(10m, "NGN"),
            knownControlVersion,
            CoreLedgerDatabaseScope.SeededAt);
}
