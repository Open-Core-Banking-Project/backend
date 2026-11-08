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
/// Dormancy, reactivation and the closure ordering against a real <c>core_ledger_db</c>: the business-date
/// assessment, the recorded-not-assessable outcome, the dormancy transition and its reactivation, and the
/// proposal, approval and re-check sequence that may close an account but never forces a balance to zero.
/// Executed by <c>scripts/db-verify.ps1</c>. Decisions: P1-E04-D12, P1-E04-D13.
/// </summary>
[Trait("Category", "Database")]
public sealed class AccountDormancyClosureDatabaseTests : IDisposable
{
    private static readonly ActorId Maker = ActorId.From("db-maker");
    private static readonly ActorId Checker = ActorId.From("db-checker");
    private static readonly DateOnly OpenedOn =
        DateOnly.FromDateTime(CoreLedgerDatabaseScope.SeededAt.UtcDateTime);

    private readonly CoreLedgerDatabaseScope _scope = new();
    private readonly FixedTimeProvider _time;

    public AccountDormancyClosureDatabaseTests() =>
        _time = new FixedTimeProvider(CoreLedgerDatabaseScope.SeededAt);

    public void Dispose() => _scope.Dispose();

    [Fact]
    public async Task An_account_below_the_threshold_is_recorded_as_not_dormant_and_keeps_its_status()
    {
        (string accountId, long version) = await AccountDatabaseScenario.OpenAndActivateAccountAsync(
            _scope, "application-dormancy-1", "customer-dormancy-1", Maker, _time);
        DateOnly businessDate = OpenedOn.AddDays(10);

        AmbientTenantContext tenantContext = _scope.NewTenant();
        await using CoreLedgerDbContext context = _scope.CreateContext(tenantContext);

        DormancyAssessmentResult assessment = await CreateDormancyService(
                context,
                tenantContext,
                businessDate,
                DormancyRuleLookup.Found(Rule(thresholdDays: 30), "dormancy-version-1", new string('a', 64)))
            .AssessAsync(Assess(accountId, "dormancy-assess-1", businessDate), CancellationToken.None);

        Assert.Equal(nameof(DormancyDecision.NotDormant), assessment.Decision);
        Assert.False(assessment.TransitionPermitted);
        Assert.Equal(10, assessment.InactivityDays);
        Assert.Equal(30, assessment.ThresholdDays);

        await using CoreLedgerDbContext verification = _scope.CreateContext();
        AccountRecord account = await verification.Accounts.SingleAsync(record => record.AccountId == accountId);
        Assert.Equal(nameof(AccountStatus.Active), account.Status);
        Assert.Equal(version, account.Version);
        Assert.Single(await verification.AccountDormancyAssessments.ToArrayAsync());
    }

    [Fact]
    public async Task An_account_past_the_threshold_becomes_dormant_and_is_reactivated_alone()
    {
        (string accountId, long activeVersion) = await AccountDatabaseScenario.OpenAndActivateAccountAsync(
            _scope, "application-dormancy-2", "customer-dormancy-2", Maker, _time);
        DateOnly businessDate = OpenedOn.AddDays(40);

        AmbientTenantContext tenantContext = _scope.NewTenant();
        await using CoreLedgerDbContext context = _scope.CreateContext(tenantContext);
        AccountDormancyService service = CreateDormancyService(
            context,
            tenantContext,
            businessDate,
            DormancyRuleLookup.Found(Rule(thresholdDays: 30), "dormancy-version-1", new string('b', 64)));

        DormancyAssessmentResult assessment = await service.AssessAsync(
            Assess(accountId, "dormancy-assess-2", businessDate),
            CancellationToken.None);

        Assert.Equal(nameof(DormancyDecision.Dormant), assessment.Decision);
        Assert.True(assessment.TransitionPermitted);
        Assert.Equal(40, assessment.InactivityDays);

        await using CoreLedgerDbContext verification = _scope.CreateContext();
        AccountRecord dormant = await verification.Accounts.SingleAsync(record => record.AccountId == accountId);
        Assert.Equal(nameof(AccountStatus.Dormant), dormant.Status);
        Assert.Equal(activeVersion + 1, dormant.Version);
        Assert.Contains(
            await verification.OutboxMessages.ToArrayAsync(),
            record => record.EventType == "account_status_changed");

        AccountReactivatedResult reactivated = await service.ReactivateAsync(
            new ReactivateAccountCommand(
                _scope.Institution,
                Maker,
                "dormancy-reactivate-1",
                accountId,
                dormant.Version,
                "customer_request",
                null,
                null,
                "correlation-dormancy-reactivate-1"),
            CancellationToken.None);

        Assert.Equal(nameof(AccountStatus.Active), reactivated.Status);

        await using CoreLedgerDbContext afterReactivation = _scope.CreateContext();
        Assert.Equal(
            nameof(AccountStatus.Active),
            (await afterReactivation.Accounts.SingleAsync(record => record.AccountId == accountId)).Status);
    }

    [Fact]
    public async Task A_repeated_assessment_of_one_business_date_returns_the_recorded_outcome()
    {
        (string accountId, _) = await AccountDatabaseScenario.OpenAndActivateAccountAsync(
            _scope, "application-dormancy-3", "customer-dormancy-3", Maker, _time);
        DateOnly businessDate = OpenedOn.AddDays(12);

        AmbientTenantContext tenantContext = _scope.NewTenant();
        await using CoreLedgerDbContext context = _scope.CreateContext(tenantContext);
        AccountDormancyService service = CreateDormancyService(
            context,
            tenantContext,
            businessDate,
            DormancyRuleLookup.Found(Rule(thresholdDays: 30), "dormancy-version-1", new string('c', 64)));

        DormancyAssessmentResult first = await service.AssessAsync(
            Assess(accountId, "dormancy-assess-first", businessDate),
            CancellationToken.None);
        DormancyAssessmentResult second = await service.AssessAsync(
            Assess(accountId, "dormancy-assess-second", businessDate),
            CancellationToken.None);

        Assert.Equal(first.AssessmentId, second.AssessmentId);

        await using CoreLedgerDbContext verification = _scope.CreateContext();
        Assert.Single(await verification.AccountDormancyAssessments.ToArrayAsync());
    }

    [Fact]
    public async Task A_missing_rule_is_recorded_as_not_assessable_instead_of_assumed()
    {
        (string accountId, _) = await AccountDatabaseScenario.OpenAndActivateAccountAsync(
            _scope, "application-dormancy-4", "customer-dormancy-4", Maker, _time);
        DateOnly businessDate = OpenedOn.AddDays(40);

        AmbientTenantContext tenantContext = _scope.NewTenant();
        await using CoreLedgerDbContext context = _scope.CreateContext(tenantContext);

        DormancyAssessmentResult assessment = await CreateDormancyService(
                context,
                tenantContext,
                businessDate,
                DormancyRuleLookup.Rejected(AccountDormancyPolicy.RuleMissingCode))
            .AssessAsync(Assess(accountId, "dormancy-assess-4", businessDate), CancellationToken.None);

        Assert.Equal(nameof(DormancyDecision.NotAssessable), assessment.Decision);
        Assert.False(assessment.TransitionPermitted);
        Assert.Contains(AccountDormancyPolicy.RuleMissingCode, assessment.ReasonCodes);

        await using CoreLedgerDbContext verification = _scope.CreateContext();
        Assert.Equal(
            nameof(AccountStatus.Active),
            (await verification.Accounts.SingleAsync(record => record.AccountId == accountId)).Status);
    }

    [Fact]
    public async Task A_settled_account_closes_only_through_the_proposal_approval_and_close_sequence()
    {
        (string accountId, _) = await AccountDatabaseScenario.OpenAndActivateAccountAsync(
            _scope, "application-closure-1", "customer-closure-1", Maker, _time);
        MutableLedgerClosureConditionSource conditions = new(Settled());

        AmbientTenantContext tenantContext = _scope.NewTenant();
        await using CoreLedgerDbContext context = _scope.CreateContext(tenantContext);
        AccountClosureService service = CreateClosureService(context, tenantContext, conditions);

        AccountClosureProposalResult proposal = await service.ProposeAsync(
            Propose(accountId, "closure-request-1"),
            CancellationToken.None);
        Assert.Equal("conditions_met", proposal.Status);
        Assert.Empty(proposal.RefusalCodes);

        await using (CoreLedgerDbContext unchanged = _scope.CreateContext())
        {
            Assert.Equal(
                nameof(AccountStatus.Active),
                (await unchanged.Accounts.SingleAsync(record => record.AccountId == accountId)).Status);
        }

        AccountClosureProposalResult submitted = await service.SubmitForApprovalAsync(
            new SubmitAccountClosureProposalCommand(
                _scope.Institution,
                Maker,
                "closure-submit-1",
                accountId,
                proposal.ProposalId,
                1L,
                "closure-request-1",
                "closure-source-key-1",
                "correlation-closure-submit-1"),
            CancellationToken.None);
        Assert.Equal("pending_approval", submitted.Status);
        Assert.Equal(2L, submitted.Version);

        string decision = await service.ApplyDecisionAsync(
            _scope.Institution,
            proposal.ProposalId,
            "closure-request-1",
            submitted.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
            submitted.ConditionSnapshotHash,
            approved: true,
            Checker.Value,
            CoreLedgerDatabaseScope.SeededAt,
            "correlation-closure-decision-1",
            CancellationToken.None);
        Assert.StartsWith("approved:", decision, StringComparison.Ordinal);

        await using CoreLedgerDbContext verification = _scope.CreateContext();
        AccountRecord closed = await verification.Accounts.SingleAsync(record => record.AccountId == accountId);
        Assert.Equal(nameof(AccountStatus.Closed), closed.Status);

        AccountClosureProposalRecord stored = await verification.AccountClosureProposals.SingleAsync();
        Assert.Equal("closed", stored.Status);
        Assert.NotNull(stored.ClosedAt);
        Assert.Equal(Checker.Value, stored.ApprovedByActorId);

        OutboxRecord[] facts = await verification.OutboxMessages.ToArrayAsync();
        Assert.Contains(facts, record => record.EventType == "account_closure_approved" || record.EventType == "account_status_changed");
        Assert.Contains(facts, record => record.EventType == "account_closed");
    }

    [Fact]
    public async Task An_unsettled_balance_is_recorded_as_a_refusal_and_cannot_be_submitted()
    {
        (string accountId, _) = await AccountDatabaseScenario.OpenAndActivateAccountAsync(
            _scope, "application-closure-2", "customer-closure-2", Maker, _time);
        MutableLedgerClosureConditionSource conditions = new(Settled() with { LedgerBalance = 50m, AvailableBalance = 50m });

        AmbientTenantContext tenantContext = _scope.NewTenant();
        await using CoreLedgerDbContext context = _scope.CreateContext(tenantContext);
        AccountClosureService service = CreateClosureService(context, tenantContext, conditions);

        AccountClosureProposalResult proposal = await service.ProposeAsync(
            Propose(accountId, "closure-request-2"),
            CancellationToken.None);
        Assert.Equal("conditions_failed", proposal.Status);
        Assert.Contains(AccountClosurePolicy.BalanceNotSettledCode, proposal.RefusalCodes);

        AccountApplicationRejectedException refused = await Assert.ThrowsAsync<AccountApplicationRejectedException>(() =>
            service.SubmitForApprovalAsync(
                new SubmitAccountClosureProposalCommand(
                    _scope.Institution,
                    Maker,
                    "closure-submit-2",
                    accountId,
                    proposal.ProposalId,
                    1L,
                    "closure-request-2",
                    "closure-source-key-2",
                    "correlation-closure-submit-2"),
                CancellationToken.None));
        Assert.Equal(AccountClosurePolicy.ProposalConditionsNotMetCode, refused.Code);

        await using CoreLedgerDbContext verification = _scope.CreateContext();
        Assert.Equal(
            nameof(AccountStatus.Active),
            (await verification.Accounts.SingleAsync(record => record.AccountId == accountId)).Status);
    }

    [Fact]
    public async Task A_ledger_answer_outside_the_freshness_ceiling_is_refused()
    {
        (string accountId, _) = await AccountDatabaseScenario.OpenAndActivateAccountAsync(
            _scope, "application-closure-3", "customer-closure-3", Maker, _time);
        MutableLedgerClosureConditionSource conditions = new(
            Settled() with { AsOf = CoreLedgerDatabaseScope.SeededAt.AddMinutes(-2) });

        AmbientTenantContext tenantContext = _scope.NewTenant();
        await using CoreLedgerDbContext context = _scope.CreateContext(tenantContext);
        AccountClosureService service = CreateClosureService(context, tenantContext, conditions);

        AccountClosureProposalResult proposal = await service.ProposeAsync(
            Propose(accountId, "closure-request-3"),
            CancellationToken.None);

        Assert.Equal("conditions_failed", proposal.Status);
        Assert.Contains(AccountClosurePolicy.BalanceSourceStaleCode, proposal.RefusalCodes);
    }

    [Fact]
    public async Task Approval_with_an_unsettled_answer_leaves_the_account_pending_closure_until_a_settled_recheck()
    {
        (string accountId, _) = await AccountDatabaseScenario.OpenAndActivateAccountAsync(
            _scope, "application-closure-4", "customer-closure-4", Maker, _time);
        MutableLedgerClosureConditionSource conditions = new(Settled());

        AmbientTenantContext tenantContext = _scope.NewTenant();
        await using CoreLedgerDbContext context = _scope.CreateContext(tenantContext);
        AccountClosureService service = CreateClosureService(context, tenantContext, conditions);

        AccountClosureProposalResult proposal = await service.ProposeAsync(
            Propose(accountId, "closure-request-4"),
            CancellationToken.None);
        AccountClosureProposalResult submitted = await service.SubmitForApprovalAsync(
            new SubmitAccountClosureProposalCommand(
                _scope.Institution,
                Maker,
                "closure-submit-4",
                accountId,
                proposal.ProposalId,
                1L,
                "closure-request-4",
                "closure-source-key-4",
                "correlation-closure-submit-4"),
            CancellationToken.None);

        // The balance moved after the proposal was submitted, so approval must not close the account.
        conditions.Current = Settled() with { LedgerBalance = 25m, AvailableBalance = 25m };

        string decision = await service.ApplyDecisionAsync(
            _scope.Institution,
            proposal.ProposalId,
            "closure-request-4",
            submitted.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
            submitted.ConditionSnapshotHash,
            approved: true,
            Checker.Value,
            CoreLedgerDatabaseScope.SeededAt,
            "correlation-closure-decision-4",
            CancellationToken.None);
        Assert.StartsWith("approved_recheck_required:", decision, StringComparison.Ordinal);

        await using (CoreLedgerDbContext pending = _scope.CreateContext())
        {
            Assert.Equal(
                nameof(AccountStatus.PendingClosure),
                (await pending.Accounts.SingleAsync(record => record.AccountId == accountId)).Status);
            AccountClosureProposalRecord stored = await pending.AccountClosureProposals.SingleAsync();
            Assert.Equal("pending_closure", stored.Status);
            Assert.Contains(AccountClosurePolicy.BalanceNotSettledCode, stored.RefusalCodes);
            Assert.Equal(4L, stored.Version);
        }

        conditions.Current = Settled();
        AccountClosureProposalResult rechecked = await service.RecheckAsync(
            new RecheckAccountClosureCommand(
                _scope.Institution,
                Maker,
                "closure-recheck-4",
                accountId,
                proposal.ProposalId,
                4L,
                "correlation-closure-recheck-4"),
            CancellationToken.None);

        Assert.Equal("closed", rechecked.Status);

        await using CoreLedgerDbContext verification = _scope.CreateContext();
        Assert.Equal(
            nameof(AccountStatus.Closed),
            (await verification.Accounts.SingleAsync(record => record.AccountId == accountId)).Status);
    }

    private static DormancyRule Rule(int thresholdDays) =>
        DormancyRule.Create(
            "dormancy-standard",
            1,
            thresholdDays,
            noticeLeadDays: 0,
            reactivationRequiresApproval: false,
            qualifyingActivities: [new DormancyQualifyingActivity("POS_PURCHASE", "debit")]);

    private static LedgerClosureConditions Settled() =>
        new(
            "ledger-account-1",
            "ledger-snapshot-1",
            CoreLedgerDatabaseScope.SeededAt,
            "NGN",
            LedgerBalance: 0m,
            AvailableBalance: 0m,
            Holds: 0m,
            OutstandingLienAmount: 0m,
            PendingSettlements: 0);

    private AccountDormancyService CreateDormancyService(
        CoreLedgerDbContext context,
        AmbientTenantContext tenantContext,
        DateOnly businessDate,
        DormancyRuleLookup rule) =>
        new(
            context,
            new IdempotencyService(context, _time),
            tenantContext,
            _time,
            new FixedDormancyRuleSource(rule),
            new FixedBusinessDateSource(businessDate),
            new NoActivitySource());

    private AccountClosureService CreateClosureService(
        CoreLedgerDbContext context,
        AmbientTenantContext tenantContext,
        ILedgerClosureConditionSource conditions) =>
        new(context, new IdempotencyService(context, _time), tenantContext, _time, conditions);

    private AssessAccountDormancyCommand Assess(string accountId, string key, DateOnly businessDate) =>
        new(_scope.Institution, Maker, key, accountId, businessDate, "correlation-" + key);

    private CreateAccountClosureProposalCommand Propose(string accountId, string requestId) =>
        new(
            _scope.Institution,
            Maker,
            "closure-propose-" + requestId,
            accountId,
            requestId,
            "customer_request",
            null,
            LienReleaseDecisionRecorded: false,
            DisputeOpen: false,
            NoticePending: false,
            "correlation-propose-" + requestId);

    private sealed class FixedDormancyRuleSource : IDormancyRuleSource
    {
        private readonly DormancyRuleLookup _lookup;

        internal FixedDormancyRuleSource(DormancyRuleLookup lookup) => _lookup = lookup;

        public Task<DormancyRuleLookup> GetAsync(
            InstitutionId institutionId,
            DateTimeOffset asOf,
            CancellationToken cancellationToken) =>
            Task.FromResult(_lookup);
    }

    private sealed class FixedBusinessDateSource : IBusinessDateSource
    {
        private readonly DateOnly _businessDate;

        internal FixedBusinessDateSource(DateOnly businessDate) => _businessDate = businessDate;

        public Task<DateOnly> GetCurrentBusinessDateAsync(
            InstitutionId institutionId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_businessDate);
    }

    private sealed class NoActivitySource : IAccountActivitySource
    {
        public Task<AccountActivitySnapshot> GetAsync(
            InstitutionId institutionId,
            string accountId,
            string currencyCode,
            CancellationToken cancellationToken) =>
            Task.FromResult(new AccountActivitySnapshot(
                [],
                "activity-version-1",
                CoreLedgerDatabaseScope.SeededAt,
                null));
    }

    private sealed class MutableLedgerClosureConditionSource : ILedgerClosureConditionSource
    {
        internal MutableLedgerClosureConditionSource(LedgerClosureConditions current) => Current = current;

        internal LedgerClosureConditions Current { get; set; }

        public Task<LedgerClosureConditions> GetAsync(
            InstitutionId institutionId,
            string accountId,
            string currencyCode,
            CancellationToken cancellationToken) =>
            Task.FromResult(Current);
    }
}
