using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.CoreLedger.Application.Accounts;
using CoreBanking.CoreLedger.Application.Products;
using CoreBanking.CoreLedger.Domain.Accounts;
using CoreBanking.CoreLedger.Infrastructure.Accounts;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CoreBanking.CoreLedger.UnitTests.Persistence;

/// <summary>
/// Number reservation, account creation and ledger handoff against a real <c>core_ledger_db</c>.
/// The contention case runs concurrent openings so the PostgreSQL row lock, not an in-memory model,
/// decides which sequence each account receives. Executed by <c>scripts/db-verify.ps1</c>.
/// Decisions: P1-E04-D06, P1-E04-D09.
/// </summary>
[Trait("Category", "Database")]
public sealed class AccountOpeningDatabaseTests : IDisposable
{
    private const int ConcurrentOpenings = 6;

    private static readonly ActorId Maker = ActorId.From("db-maker");

    private readonly CoreLedgerDatabaseScope _scope = new();
    private readonly FixedTimeProvider _time;

    public AccountOpeningDatabaseTests() => _time = new FixedTimeProvider(CoreLedgerDatabaseScope.SeededAt);

    public void Dispose() => _scope.Dispose();

    [Fact]
    public async Task Opening_an_approved_application_reserves_one_number_and_one_ledger_account()
    {
        (string applicationId, long version) = await AccountDatabaseScenario.CreateApprovedApplicationAsync(
            _scope, "application-open-1", "customer-open-1", Maker, _time);

        AmbientTenantContext tenantContext = _scope.NewTenant();
        await using CoreLedgerDbContext context = _scope.CreateContext(tenantContext);
        AccountOpeningService service = AccountDatabaseScenario.CreateOpeningService(context, tenantContext, _time);

        AccountOpenedResult opened = await service.OpenAsync(
            new OpenAccountCommand(_scope.Institution, Maker, "open-1", applicationId, version, "correlation-open-1"),
            CancellationToken.None);

        Assert.Equal(applicationId, opened.ApplicationId);
        Assert.Equal("initialised", opened.LedgerInitialisationStatus);
        Assert.Equal("****0001", opened.MaskedAccountNumber);

        await using CoreLedgerDbContext verification = _scope.CreateContext();
        AccountRecord account = await verification.Accounts.SingleAsync();
        Assert.Equal("Open", account.Status);
        Assert.Equal("initialised", account.LedgerInitialisationStatus);
        Assert.Equal("00000001", account.AccountNumber);
        Assert.NotNull(account.LedgerAccountReference);
        Assert.Equal(opened.MaskedAccountNumber, account.MaskedAccountNumber);
        Assert.Equal(FixedAccountNumberSchemeSource.SchemeVersionId, account.SchemeVersionId);

        LedgerAccountRecord ledgerAccount = await verification.LedgerAccounts.SingleAsync();
        Assert.Equal(account.AccountId, ledgerAccount.AccountId);
        Assert.Equal(account.LedgerAccountReference, ledgerAccount.LedgerAccountId);
        Assert.Equal("legal-entity-1", ledgerAccount.LegalEntityCode);
        Assert.Equal("chart-version-1", ledgerAccount.ChartVersionId);
        Assert.Equal("2100", ledgerAccount.GlAccountCode);

        AccountStatusHistoryRecord history = await verification.AccountStatusHistory.SingleAsync();
        Assert.Equal("account_opened", history.ReasonCode);

        OutboxRecord openedEvent = Assert.Single(await verification.OutboxMessages.ToArrayAsync());
        Assert.Equal("account_opened", openedEvent.EventType);
        Assert.Equal(account.AccountId, openedEvent.BusinessReference);
        Assert.DoesNotContain(account.AccountNumber, openedEvent.Envelope, StringComparison.Ordinal);

        AccountApplicationRecord application = await verification.AccountApplications
            .SingleAsync(record => record.AccountApplicationId == applicationId);
        Assert.Equal(nameof(AccountApplicationStatus.Open), application.Status);
    }

    [Fact]
    public async Task An_opening_replay_returns_the_same_account_without_a_second_number()
    {
        (string applicationId, long version) = await AccountDatabaseScenario.CreateApprovedApplicationAsync(
            _scope, "application-open-2", "customer-open-2", Maker, _time);
        AmbientTenantContext tenantContext = _scope.NewTenant();
        await using CoreLedgerDbContext context = _scope.CreateContext(tenantContext);
        AccountOpeningService service = AccountDatabaseScenario.CreateOpeningService(context, tenantContext, _time);
        OpenAccountCommand command =
            new(_scope.Institution, Maker, "open-2", applicationId, version, "correlation-open-2");

        AccountOpenedResult first = await service.OpenAsync(command, CancellationToken.None);
        AccountOpenedResult replayed = await service.OpenAsync(command, CancellationToken.None);

        Assert.True(replayed.Replayed);
        Assert.Equal(first.AccountId, replayed.AccountId);
        Assert.Equal(first.MaskedAccountNumber, replayed.MaskedAccountNumber);

        await using CoreLedgerDbContext verification = _scope.CreateContext();
        Assert.Equal(1, await verification.Accounts.CountAsync());
        Assert.Equal(1, await verification.AccountNumberReservations.CountAsync());
    }

    [Fact]
    public async Task Concurrent_openings_never_allocate_the_same_number()
    {
        List<(string ApplicationId, long Version)> approved = [];
        for (int index = 0; index < ConcurrentOpenings; index++)
        {
            approved.Add(await AccountDatabaseScenario.CreateApprovedApplicationAsync(
                _scope,
                "application-concurrent-" + index,
                "customer-concurrent-" + index,
                Maker,
                _time));
        }

        Task<AccountOpenedResult>[] openings = approved
            .Select((application, index) => Task.Run(async () =>
            {
                AmbientTenantContext tenantContext = _scope.NewTenant();
                await using CoreLedgerDbContext context = _scope.CreateContext(tenantContext);
                return await AccountDatabaseScenario.CreateOpeningService(context, tenantContext, _time).OpenAsync(
                    new OpenAccountCommand(
                        _scope.Institution,
                        Maker,
                        "open-concurrent-" + index,
                        application.ApplicationId,
                        application.Version,
                        "correlation-concurrent-" + index),
                    CancellationToken.None);
            }))
            .ToArray();

        AccountOpenedResult[] results = await Task.WhenAll(openings);

        Assert.Equal(ConcurrentOpenings, results.Select(result => result.MaskedAccountNumber).Distinct().Count());

        await using CoreLedgerDbContext verification = _scope.CreateContext();
        AccountRecord[] accounts = await verification.Accounts.ToArrayAsync();
        Assert.Equal(ConcurrentOpenings, accounts.Length);
        Assert.Equal(ConcurrentOpenings, accounts.Select(account => account.AccountNumber).Distinct().Count());
        Assert.Equal(ConcurrentOpenings, await verification.LedgerAccounts.CountAsync());

        string[] numbers = accounts
            .Select(account => account.AccountNumber)
            .OrderBy(number => number, StringComparer.Ordinal)
            .ToArray();
        string[] expected = Enumerable.Range(1, ConcurrentOpenings)
            .Select(sequence => sequence.ToString("00000000", System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();
        Assert.Equal(expected, numbers);

        AccountNumberReservationRecord reservation = await verification.AccountNumberReservations.SingleAsync();
        Assert.Equal(ConcurrentOpenings + 1L, reservation.NextValue);
        Assert.Equal(reservation.NextValue, reservation.HighWaterMark);
    }

    [Fact]
    public async Task An_opening_without_the_approved_status_is_refused_and_persists_no_account()
    {
        string applicationId = "application-open-3";
        long version = await AccountDatabaseScenario.CreateEligibleApplicationAsync(
            _scope, applicationId, "customer-open-3", Maker, _time);

        AmbientTenantContext tenantContext = _scope.NewTenant();
        await using CoreLedgerDbContext context = _scope.CreateContext(tenantContext);
        AccountOpeningService service = AccountDatabaseScenario.CreateOpeningService(context, tenantContext, _time);

        AccountApplicationRejectedException refused = await Assert.ThrowsAsync<AccountApplicationRejectedException>(() =>
            service.OpenAsync(
                new OpenAccountCommand(_scope.Institution, Maker, "open-3", applicationId, version, "correlation-open-3"),
                CancellationToken.None));
        Assert.Equal("account_application_not_approved", refused.Code);

        await using CoreLedgerDbContext verification = _scope.CreateContext();
        Assert.Equal(0, await verification.Accounts.CountAsync());
        Assert.Equal(0, await verification.AccountNumberReservations.CountAsync());
    }
}
