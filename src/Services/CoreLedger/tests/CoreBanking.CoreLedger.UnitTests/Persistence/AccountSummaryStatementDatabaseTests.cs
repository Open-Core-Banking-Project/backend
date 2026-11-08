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
/// Account summaries and statement authorisation against a real <c>core_ledger_db</c>. The summary carries the
/// ledger's answer or no balance at all, and a statement that the ledger cannot answer is recorded as refused
/// instead of being built from another source. Executed by <c>scripts/db-verify.ps1</c>. Decision: P1-E04-D15.
/// </summary>
[Trait("Category", "Database")]
public sealed class AccountSummaryStatementDatabaseTests : IDisposable
{
    private static readonly ActorId Maker = ActorId.From("db-maker");
    private static readonly DateOnly BusinessDate = DateOnly.FromDateTime(CoreLedgerDatabaseScope.SeededAt.UtcDateTime);

    private readonly CoreLedgerDatabaseScope _scope = new();
    private readonly FixedTimeProvider _time;

    public AccountSummaryStatementDatabaseTests() =>
        _time = new FixedTimeProvider(CoreLedgerDatabaseScope.SeededAt);

    public void Dispose() => _scope.Dispose();

    [Fact]
    public async Task A_summary_carries_the_ledger_answer_with_its_source()
    {
        string accountId = await OpenAccountAsync("application-summary-1", "customer-summary-1");
        AmbientTenantContext tenantContext = _scope.NewTenant();
        await using CoreLedgerDbContext context = _scope.CreateContext(tenantContext);

        AccountSummaryResult summary = await new AccountSummaryService(
                context,
                tenantContext,
                new FixedLedgerAvailabilitySource(available: 750m))
            .GetAsync(_scope.Institution, accountId, CancellationToken.None);

        Assert.Equal(AccountBalanceAvailability.Available, summary.BalanceAvailability);
        Assert.NotNull(summary.Balances);
        Assert.Equal(750m, summary.Balances!.Available);
        Assert.Equal("ledger-availability-1", summary.Balances.SourceVersion);
        Assert.Equal(_scope.Institution.Value, summary.InstitutionId);
    }

    [Fact]
    public async Task A_summary_carries_no_balance_when_the_ledger_cannot_answer()
    {
        string accountId = await OpenAccountAsync("application-summary-2", "customer-summary-2");
        AmbientTenantContext tenantContext = _scope.NewTenant();
        await using CoreLedgerDbContext context = _scope.CreateContext(tenantContext);

        AccountSummaryResult summary = await new AccountSummaryService(
                context,
                tenantContext,
                new UnavailableLedgerAvailabilitySource())
            .GetAsync(_scope.Institution, accountId, CancellationToken.None);

        Assert.Equal(AccountBalanceAvailability.Unavailable, summary.BalanceAvailability);
        Assert.Null(summary.Balances);
        Assert.Equal(0m, summary.OutstandingLienAmount);
        Assert.Equal(0, summary.ActiveBlockCount);
    }

    [Fact]
    public async Task A_statement_the_ledger_cannot_answer_is_recorded_as_refused_without_an_artifact()
    {
        string accountId = await OpenAccountAsync("application-statement-1", "customer-statement-1");
        AmbientTenantContext tenantContext = _scope.NewTenant();
        await using CoreLedgerDbContext context = _scope.CreateContext(tenantContext);
        AccountStatementService service = CreateService(context, tenantContext, StatementQuery.Unavailable());

        AccountStatementRequestResult result = await service.RequestAsync(
            Statement(accountId, "statement-request-1", "pdf"),
            CancellationToken.None);

        Assert.Equal(AccountStatementService.RefusedStatus, result.Status);
        Assert.Contains(AccountStatementPolicy.SourceUnavailableCode, result.RefusalCodes);
        Assert.Null(result.ArtifactReference);

        await using CoreLedgerDbContext verification = _scope.CreateContext();
        AccountStatementRequestRecord stored = await verification.AccountStatementRequests.SingleAsync();
        Assert.Equal(AccountStatementService.RefusedStatus, stored.Status);
        Assert.Equal(AccountStatementPolicy.SourceUnavailableCode, stored.RefusalCodes);

        StatementArtifactAuthorisationResult authorisation = await service.AuthoriseArtifactAsync(
            new AuthoriseStatementArtifactCommand(
                _scope.Institution,
                Maker,
                "statement-authorise-1",
                accountId,
                "statement-request-1",
                "customer",
                "correlation-statement-authorise-1"),
            CancellationToken.None);

        Assert.Equal(AccountStatementService.RefusedStatus, authorisation.Status);
        Assert.Equal(string.Empty, authorisation.ArtifactReference);
    }

    [Fact]
    public async Task A_statement_request_outside_the_published_policy_is_refused_and_never_stored()
    {
        string accountId = await OpenAccountAsync("application-statement-2", "customer-statement-2");
        AmbientTenantContext tenantContext = _scope.NewTenant();
        await using CoreLedgerDbContext context = _scope.CreateContext(tenantContext);
        AccountStatementService service = CreateService(context, tenantContext, StatementQuery.Available());

        AccountApplicationRejectedException refused = await Assert.ThrowsAsync<AccountApplicationRejectedException>(() =>
            service.RequestAsync(
                Statement(accountId, "statement-request-2", "xlsx"),
                CancellationToken.None));

        Assert.Equal(AccountStatementPolicy.FormatNotSupportedCode, refused.Code);

        await using CoreLedgerDbContext verification = _scope.CreateContext();
        Assert.Empty(verification.AccountStatementRequests);
        Assert.DoesNotContain(
            await verification.OutboxMessages.ToArrayAsync(),
            record => record.EventType == "statement_requested");
    }

    [Fact]
    public async Task An_answered_statement_request_records_the_ledger_references_and_awaits_its_artifact()
    {
        string accountId = await OpenAccountAsync("application-statement-3", "customer-statement-3");
        AmbientTenantContext tenantContext = _scope.NewTenant();
        await using CoreLedgerDbContext context = _scope.CreateContext(tenantContext);
        AccountStatementService service = CreateService(context, tenantContext, StatementQuery.Available());

        AccountStatementRequestResult result = await service.RequestAsync(
            Statement(accountId, "statement-request-3", "pdf"),
            CancellationToken.None);

        Assert.Equal(AccountStatementService.AwaitingArtifactStatus, result.Status);
        Assert.Equal("ledger-query-1", result.LedgerQueryReference);
        Assert.Equal("ledger-integrity-1", result.IntegrityReference);
        Assert.Empty(result.RefusalCodes);
        Assert.Null(result.ArtifactReference);

        await using CoreLedgerDbContext verification = _scope.CreateContext();
        OutboxRecord claimed = Assert.Single(
            await verification.OutboxMessages.ToArrayAsync(),
            record => record.EventType == "statement_requested");
        Assert.Equal("statement_requested", claimed.EventType);
        Assert.Contains("ledger-query-1", claimed.Envelope, StringComparison.Ordinal);
    }

    private async Task<string> OpenAccountAsync(string applicationId, string customerId) =>
        await AccountDatabaseScenario.OpenAccountAsync(_scope, applicationId, customerId, Maker, _time);

    private AccountStatementService CreateService(
        CoreLedgerDbContext context,
        AmbientTenantContext tenantContext,
        ILedgerStatementQuerySource query) =>
        new(
            context,
            new IdempotencyService(context, _time),
            tenantContext,
            _time,
            new FixedStatementPolicySource(),
            new FixedBusinessDateSource(),
            query);

    private CreateAccountStatementRequestCommand Statement(string accountId, string key, string formatCode) =>
        new(
            _scope.Institution,
            Maker,
            key,
            accountId,
            key,
            BusinessDate,
            BusinessDate,
            formatCode,
            "on_demand",
            "customer",
            "correlation-" + key);

    private sealed class FixedStatementPolicySource : IStatementPolicySource
    {
        private static readonly StatementPolicy Policy = StatementPolicy.Create(
            "policy-1",
            1,
            maximumSpanDays: 366,
            retentionDays: 2555,
            artifactReferenceMinutes: 15,
            formats: ["pdf", "csv"],
            purposes: ["periodic", "on_demand", "receipt", "regulatory"],
            receiptFormats: ["pdf"],
            periodKinds: ["calendar_month", "quarter", "year_to_date", "custom"]);

        public Task<StatementPolicyLookup> GetAsync(
            InstitutionId institutionId,
            DateTimeOffset asOf,
            CancellationToken cancellationToken) =>
            Task.FromResult(StatementPolicyLookup.Found(Policy, "policy-version-1", Policy.ToConfigurationContent().Hash));
    }

    private sealed class FixedBusinessDateSource : IBusinessDateSource
    {
        public Task<DateOnly> GetCurrentBusinessDateAsync(
            InstitutionId institutionId,
            CancellationToken cancellationToken) =>
            Task.FromResult(BusinessDate);
    }

    private sealed class StatementQuery : ILedgerStatementQuerySource
    {
        private readonly bool _available;

        private StatementQuery(bool available) => _available = available;

        internal static StatementQuery Available() => new(true);

        internal static StatementQuery Unavailable() => new(false);

        public Task<LedgerStatementQueryResult> GetAsync(
            InstitutionId institutionId,
            string accountId,
            string currencyCode,
            DateOnly periodFrom,
            DateOnly periodTo,
            CancellationToken cancellationToken) =>
            Task.FromResult(_available
                ? new LedgerStatementQueryResult(
                    true, null, "ledger-query-1", "ledger-integrity-1", 0m, 0m, 0)
                : new LedgerStatementQueryResult(
                    false,
                    AccountStatementPolicy.SourceUnavailableCode,
                    string.Empty,
                    string.Empty,
                    null,
                    null,
                    0));
    }

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

    private sealed class UnavailableLedgerAvailabilitySource : ILedgerAvailabilitySource
    {
        public Task<LedgerAvailabilitySnapshot> GetAsync(
            InstitutionId institutionId,
            string accountId,
            string currencyCode,
            CancellationToken cancellationToken) =>
            throw new AccountApplicationRejectedException(
                "ledger_account_not_initialised",
                "The ledger has no account for this bank account.");
    }
}
