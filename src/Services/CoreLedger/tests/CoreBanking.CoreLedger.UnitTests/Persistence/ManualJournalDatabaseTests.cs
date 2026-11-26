using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.CoreLedger.Application.Ledger;
using CoreBanking.CoreLedger.Contracts;
using CoreBanking.CoreLedger.Domain.Institutions;
using CoreBanking.CoreLedger.Domain.Ledger;
using CoreBanking.CoreLedger.Infrastructure.Accounts;
using CoreBanking.CoreLedger.Infrastructure.Ledger;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CoreBanking.CoreLedger.UnitTests.Persistence;

/// <summary>Real PostgreSQL evidence for manual-journal authority, quorum and atomic posting. Decision: P1-E05-D11.</summary>
[Trait("Category", "Database")]
public sealed class ManualJournalDatabaseTests
{
    private static readonly ActorId Maker = ActorId.From("manual-journal-maker");

    [Fact]
    public async Task Draft_and_submit_emit_one_approval_request_and_replay()
    {
        CoreLedgerDatabaseScope scope = new();
        FixedTimeProvider time = new(CoreLedgerDatabaseScope.SeededAt);
        JournalFixture fixture = await PrepareAsync(scope, time, "draft");
        await using CoreLedgerDbContext context = scope.CreateContext(scope.NewTenant());

        ManualJournalResult draft = await Service(context, scope, time).CreateDraftAsync(
            DraftCommand(scope, "draft", fixture),
            CancellationToken.None);
        ManualJournalResult replay = await Service(context, scope, time).CreateDraftAsync(
            DraftCommand(scope, "draft", fixture),
            CancellationToken.None);

        Assert.Equal("draft", draft.Status);
        Assert.Equal(2, draft.RequiredApprovers);
        Assert.False(draft.RequiresFinanceHead);
        Assert.False(draft.Replayed);
        Assert.True(replay.Replayed);
        Assert.Equal(draft.RequestHash, replay.RequestHash);

        ManualJournalResult submitted = await Service(context, scope, time).SubmitAsync(
            new SubmitManualJournalCommand(
                scope.Institution,
                Maker,
                draft.ManualJournalId,
                draft.Version,
                "manual-journal-approval-request",
                "manual-journal-source-request",
                "draft-submit-correlation"),
            CancellationToken.None);
        ManualJournalResult submitReplay = await Service(context, scope, time).SubmitAsync(
            new SubmitManualJournalCommand(
                scope.Institution,
                Maker,
                draft.ManualJournalId,
                draft.Version,
                "manual-journal-approval-request",
                "manual-journal-source-request",
                "draft-submit-correlation"),
            CancellationToken.None);

        Assert.Equal("submitted", submitted.Status);
        Assert.True(submitReplay.Replayed);
        Assert.Equal(1, await CountOutboxAsync(context, "approval_requested"));
        Assert.Equal(1, await CountAuditAsync(context, "ledger.manual_journal.submit"));
    }

    [Fact]
    public async Task Two_distinct_approvals_post_one_balanced_journal_with_audit_and_outbox()
    {
        CoreLedgerDatabaseScope scope = new();
        FixedTimeProvider time = new(CoreLedgerDatabaseScope.SeededAt);
        JournalFixture fixture = await PrepareAsync(scope, time, "post");
        await using CoreLedgerDbContext context = scope.CreateContext(scope.NewTenant());
        ManualJournalService service = Service(context, scope, time);
        ManualJournalResult submitted = await SubmitAsync(service, scope, "post", fixture);

        string awaiting = await service.ApplyDecisionAsync(
            scope.Institution,
            submitted.ManualJournalId,
            "manual-journal-approval-request",
            submitted.Version.ToString(CultureInfo.InvariantCulture),
            submitted.RequestHash,
            approved: true,
            "finance-checker-one",
            ManualJournalApprovalV1.RequiredPermission,
            "entitlement-1",
            CoreLedgerDatabaseScope.SeededAt,
            "decision-1",
            "post-correlation",
            CancellationToken.None);

        Assert.Equal("awaiting_quorum", awaiting);
        Assert.Equal("submitted", (await service.GetAsync(scope.Institution, submitted.ManualJournalId, CancellationToken.None)).Status);

        string posted = await service.ApplyDecisionAsync(
            scope.Institution,
            submitted.ManualJournalId,
            "manual-journal-approval-request",
            submitted.Version.ToString(CultureInfo.InvariantCulture),
            submitted.RequestHash,
            approved: true,
            "finance-checker-two",
            "ledger.manual_journal.approve",
            "entitlement-1",
            CoreLedgerDatabaseScope.SeededAt,
            "decision-2",
            "post-correlation",
            CancellationToken.None);

        Assert.Equal("posted", posted);
        Assert.Equal(
            "already_posted",
            await service.ApplyDecisionAsync(
                scope.Institution,
                submitted.ManualJournalId,
                "manual-journal-approval-request",
                submitted.Version.ToString(CultureInfo.InvariantCulture),
                submitted.RequestHash,
                approved: true,
                "finance-checker-two",
                "ledger.manual_journal.approve",
                "entitlement-1",
                CoreLedgerDatabaseScope.SeededAt,
                "decision-2",
                "post-correlation",
                CancellationToken.None));

        await using CoreLedgerDbContext verification = scope.CreateContext();
        ManualJournalRequestRecord request = await verification.ManualJournalRequests.SingleAsync(
            record => record.ManualJournalId == submitted.ManualJournalId);
        Assert.Equal("posted", request.Status);
        Assert.Equal(2, request.RequiredApprovers);
        Assert.NotNull(request.FinancialTransactionId);
        Assert.Equal(2, await verification.ManualJournalApprovals.CountAsync(
            record => record.ManualJournalId == submitted.ManualJournalId));
        Assert.Equal(1, await verification.FinancialTransactions.CountAsync(
            record => record.FinancialTransactionId == submitted.ManualJournalId));
        Assert.Equal(2, await verification.JournalLines.CountAsync(
            record => record.JournalEntryId == submitted.ManualJournalId + "-J"));
        Assert.Equal(2, await verification.LedgerMovements.CountAsync(
            record => record.JournalLineId.StartsWith(submitted.ManualJournalId)));
        Assert.Equal(1, await verification.OutboxMessages.CountAsync(
            record => record.EventType == "manual_journal_posted"
                && record.BusinessReference == submitted.ManualJournalId));
        Assert.Equal(1, await verification.InstitutionAuditEvents.CountAsync(
            record => record.Action == "ledger.manual_journal.post"
                && record.ResourceId == submitted.ManualJournalId
                && record.Outcome == "succeeded"));

        LedgerBalanceRecord customer = await verification.LedgerBalances.SingleAsync(
            record => record.LedgerAccountId == fixture.CustomerLedgerAccountId);
        LedgerBalanceRecord cash = await verification.LedgerBalances.SingleAsync(
            record => record.LedgerAccountId == fixture.CashLedgerAccountId);
        Assert.Equal(-50m, customer.NetBalance);
        Assert.Equal(50m, cash.NetBalance);
    }

    [Fact]
    public async Task The_maker_cannot_approve_and_a_stale_version_is_refused()
    {
        CoreLedgerDatabaseScope scope = new();
        FixedTimeProvider time = new(CoreLedgerDatabaseScope.SeededAt);
        JournalFixture fixture = await PrepareAsync(scope, time, "maker");
        await using CoreLedgerDbContext context = scope.CreateContext(scope.NewTenant());
        ManualJournalService service = Service(context, scope, time);
        ManualJournalResult submitted = await SubmitAsync(service, scope, "maker", fixture);

        LedgerPostingException selfApproval = await Assert.ThrowsAsync<LedgerPostingException>(() =>
            service.ApplyDecisionAsync(
                scope.Institution,
                submitted.ManualJournalId,
                "manual-journal-approval-request",
                submitted.Version.ToString(CultureInfo.InvariantCulture),
                submitted.RequestHash,
                approved: true,
                Maker.Value,
                ManualJournalApprovalV1.RequiredPermission,
                "entitlement-1",
                CoreLedgerDatabaseScope.SeededAt,
                "decision-self",
                "maker-correlation",
                CancellationToken.None));
        Assert.Equal("manual_journal_self_approval_denied", selfApproval.Code);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyDecisionAsync(
            scope.Institution,
            submitted.ManualJournalId,
            "manual-journal-approval-request",
            (submitted.Version + 5).ToString(CultureInfo.InvariantCulture),
            submitted.RequestHash,
            approved: true,
            "finance-checker-one",
            ManualJournalApprovalV1.RequiredPermission,
            "entitlement-1",
            CoreLedgerDatabaseScope.SeededAt,
            "decision-stale",
            "maker-correlation",
            CancellationToken.None));
    }

    [Fact]
    public async Task Customer_only_lines_and_unapproved_reasons_are_refused()
    {
        CoreLedgerDatabaseScope scope = new();
        FixedTimeProvider time = new(CoreLedgerDatabaseScope.SeededAt);
        JournalFixture fixture = await PrepareAsync(scope, time, "refusal");
        await using CoreLedgerDbContext context = scope.CreateContext(scope.NewTenant());
        ManualJournalService service = Service(context, scope, time);

        LedgerPostingException customerOnly = await Assert.ThrowsAsync<LedgerPostingException>(() =>
            service.CreateDraftAsync(
                new CreateManualJournalDraftCommand(
                    scope.Institution,
                    Maker,
                    "customer-only-journal",
                    "customer-only-request",
                    "customer_refund",
                    "case-refusal",
                    [
                        new ManualJournalLineCommand(fixture.CustomerLedgerAccountId, "debit", 50m, "principal"),
                        new ManualJournalLineCommand(fixture.CustomerLedgerAccountId, "credit", 50m, "principal"),
                    ],
                    "refusal-correlation"),
                CancellationToken.None));
        Assert.Equal("manual_journal_compensating_account_required", customerOnly.Code);

        LedgerPostingException reason = await Assert.ThrowsAsync<LedgerPostingException>(() =>
            service.CreateDraftAsync(
                DraftCommand(scope, "refusal", fixture) with { ReasonCode = "not_a_reason" },
                CancellationToken.None));
        Assert.Equal("manual_journal_reason_not_approved", reason.Code);
    }

    [Fact]
    public async Task Break_glass_posts_once_with_dual_control_evidence()
    {
        CoreLedgerDatabaseScope scope = new();
        FixedTimeProvider time = new(CoreLedgerDatabaseScope.SeededAt);
        JournalFixture fixture = await PrepareAsync(scope, time, "glass");
        await using CoreLedgerDbContext context = scope.CreateContext(scope.NewTenant());
        ManualJournalService service = Service(context, scope, time);

        LedgerPostingException selfAuthorised = await Assert.ThrowsAsync<LedgerPostingException>(() =>
            service.PostWithBreakGlassAsync(
                BreakGlassCommand(scope, "glass", fixture) with { DualControlActorId = Maker.Value },
                CancellationToken.None));
        Assert.Equal("manual_journal_break_glass_self_authorised", selfAuthorised.Code);

        ManualJournalResult posted = await service.PostWithBreakGlassAsync(
            BreakGlassCommand(scope, "glass", fixture),
            CancellationToken.None);

        Assert.Equal("posted", posted.Status);
        await using CoreLedgerDbContext verification = scope.CreateContext();
        ManualJournalRequestRecord request = await verification.ManualJournalRequests.SingleAsync(
            record => record.ManualJournalId == posted.ManualJournalId);
        Assert.True(request.BreakGlass);
        Assert.Equal("security-approver", request.DualControlActorId);
        Assert.NotNull(request.FinancialTransactionId);
        Assert.Equal(0, await verification.ManualJournalApprovals.CountAsync(
            record => record.ManualJournalId == posted.ManualJournalId));
        Assert.Equal(1, await verification.InstitutionAuditEvents.CountAsync(
            record => record.Action == "ledger.manual_journal.break_glass"
                && record.ResourceId == posted.ManualJournalId));
    }

    [Fact]
    public async Task A_posted_request_cannot_be_rewritten_or_deleted()
    {
        CoreLedgerDatabaseScope scope = new();
        FixedTimeProvider time = new(CoreLedgerDatabaseScope.SeededAt);
        JournalFixture fixture = await PrepareAsync(scope, time, "immutable");
        await using CoreLedgerDbContext context = scope.CreateContext(scope.NewTenant());
        ManualJournalResult posted = await Service(context, scope, time).PostWithBreakGlassAsync(
            BreakGlassCommand(scope, "immutable", fixture),
            CancellationToken.None);

        await Assert.ThrowsAnyAsync<Exception>(() => context.Database.ExecuteSqlRawAsync(
            "UPDATE ledger.manual_journal_requests SET reason_code = 'tampered' WHERE institution_id = {0} AND manual_journal_id = {1}",
            scope.Institution.Value,
            posted.ManualJournalId));
        await Assert.ThrowsAnyAsync<Exception>(() => context.Database.ExecuteSqlRawAsync(
            "DELETE FROM ledger.manual_journal_requests WHERE institution_id = {0} AND manual_journal_id = {1}",
            scope.Institution.Value,
            posted.ManualJournalId));
        await Assert.ThrowsAnyAsync<Exception>(() => context.Database.ExecuteSqlRawAsync(
            "UPDATE ledger.manual_journal_lines SET amount = 1 WHERE institution_id = {0} AND manual_journal_id = {1}",
            scope.Institution.Value,
            posted.ManualJournalId));
    }

    [Fact]
    public async Task A_missing_policy_fails_closed()
    {
        CoreLedgerDatabaseScope scope = new();
        FixedTimeProvider time = new(CoreLedgerDatabaseScope.SeededAt);
        JournalFixture fixture = await PrepareAsync(scope, time, "nopolicy", seedPolicy: false);
        await using CoreLedgerDbContext context = scope.CreateContext(scope.NewTenant());

        LedgerPostingException exception = await Assert.ThrowsAsync<LedgerPostingException>(() =>
            Service(context, scope, time).CreateDraftAsync(
                DraftCommand(scope, "nopolicy", fixture),
                CancellationToken.None));

        Assert.Equal("manual_journal_policy_missing", exception.Code);
    }

    private static ManualJournalService Service(
        CoreLedgerDbContext context,
        CoreLedgerDatabaseScope scope,
        TimeProvider time) =>
        new(
            context,
            new ManualJournalPolicySource(context),
            new ManualJournalAccountResolver(context),
            PostingService(context, scope.NewTenant(), time),
            scope.NewTenant(),
            time);

    private static AtomicLedgerPostingService PostingService(
        CoreLedgerDbContext context,
        AmbientTenantContext tenant,
        TimeProvider time) =>
        new(
            context,
            new FinancialIdempotencyService(context, tenant, time),
            new AccountControlService(context, new IdempotencyService(context, time), tenant, time),
            tenant,
            time);

    private static CreateManualJournalDraftCommand DraftCommand(
        CoreLedgerDatabaseScope scope,
        string prefix,
        JournalFixture fixture) =>
        new(
            scope.Institution,
            Maker,
            prefix + "-journal",
            prefix + "-request",
            "customer_refund",
            prefix + "-case-reference",
            [
                new ManualJournalLineCommand(fixture.CustomerLedgerAccountId, "debit", 50m, "principal"),
                new ManualJournalLineCommand(fixture.CashLedgerAccountId, "credit", 50m, "principal"),
            ],
            prefix + "-correlation");

    private static BreakGlassManualJournalCommand BreakGlassCommand(
        CoreLedgerDatabaseScope scope,
        string prefix,
        JournalFixture fixture) =>
        new(
            scope.Institution,
            Maker,
            prefix + "-glass-journal",
            prefix + "-glass-request",
            "customer_refund",
            prefix + "-glass-case",
            [
                new ManualJournalLineCommand(fixture.CustomerLedgerAccountId, "debit", 25m, "principal"),
                new ManualJournalLineCommand(fixture.CashLedgerAccountId, "credit", 25m, "principal"),
            ],
            "security-approver",
            "security-case-" + prefix,
            CoreLedgerDatabaseScope.SeededAt.AddHours(1),
            prefix + "-glass-correlation");

    private static async Task<ManualJournalResult> SubmitAsync(
        ManualJournalService service,
        CoreLedgerDatabaseScope scope,
        string prefix,
        JournalFixture fixture)
    {
        ManualJournalResult draft = await service.CreateDraftAsync(
            DraftCommand(scope, prefix, fixture),
            CancellationToken.None);
        return await service.SubmitAsync(
            new SubmitManualJournalCommand(
                scope.Institution,
                Maker,
                draft.ManualJournalId,
                draft.Version,
                "manual-journal-approval-request",
                "manual-journal-source-request",
                prefix + "-submit-correlation"),
            CancellationToken.None);
    }

    private static async Task<int> CountOutboxAsync(CoreLedgerDbContext context, string eventType) =>
        await context.OutboxMessages.CountAsync(record => record.EventType == eventType);

    private static async Task<int> CountAuditAsync(CoreLedgerDbContext context, string action) =>
        await context.InstitutionAuditEvents.CountAsync(record => record.Action == action);

    private static async Task<JournalFixture> PrepareAsync(
        CoreLedgerDatabaseScope scope,
        TimeProvider time,
        string prefix,
        bool seedPolicy = true)
    {
        await SeedChartAsync(scope);
        if (seedPolicy)
        {
            await SeedPolicyAsync(scope);
        }

        await SeedBusinessDateAsync(scope);

        string customerAccountId = await AccountDatabaseScenario.OpenAccountAsync(
            scope,
            prefix + "-customer-application",
            prefix + "-customer",
            Maker,
            time);
        string cashAccountId = await AccountDatabaseScenario.OpenAccountAsync(
            scope,
            prefix + "-cash-application",
            prefix + "-cash",
            Maker,
            time);

        await using CoreLedgerDbContext context = scope.CreateContext();
        string customerLedgerId = await context.LedgerAccounts
            .Where(record => record.AccountId == customerAccountId)
            .Select(record => record.LedgerAccountId)
            .SingleAsync();
        LedgerAccountRecord cashLedger = await context.LedgerAccounts.SingleAsync(
            record => record.AccountId == cashAccountId);
        cashLedger.GlAccountCode = "3300";
        cashLedger.NormalSide = "debit";

        LedgerBalanceRecord customerBalance = await context.LedgerBalances.SingleAsync(
            record => record.LedgerAccountId == customerLedgerId);
        customerBalance.CreditTotal = 100m;
        customerBalance.NetBalance = -100m;
        customerBalance.Version++;
        customerBalance.AsOf = CoreLedgerDatabaseScope.SeededAt;

        LedgerBalanceRecord cashBalance = await context.LedgerBalances.SingleAsync(
            record => record.LedgerAccountId == cashLedger.LedgerAccountId);
        cashBalance.NormalSide = "debit";
        cashBalance.DebitTotal = 100m;
        cashBalance.NetBalance = 100m;
        cashBalance.Version++;
        cashBalance.AsOf = CoreLedgerDatabaseScope.SeededAt;
        await context.SaveChangesAsync();

        return new JournalFixture(customerLedgerId, cashLedger.LedgerAccountId, "manual-journal-policy-version");
    }

    private static async Task SeedChartAsync(CoreLedgerDatabaseScope scope)
    {
        const string chartJson = """
            {
              "legalEntityCode": "legal-entity-1",
              "accounts": [
                { "code": "2100", "role": "customerDeposit", "normalSide": "credit", "currency": "NGN", "status": "active" },
                { "code": "3300", "role": "cashSettlement", "normalSide": "debit", "currency": "NGN", "status": "active" },
                { "code": "4400", "role": "suspense", "normalSide": "debit", "currency": "NGN", "status": "active" }
              ]
            }
            """;
        ConfigurationContent content = LedgerConfigurationContent.ValidateChart(chartJson);
        await SeedConfigurationAsync(scope, LedgerConfigurationContent.ChartArea, LedgerConfigurationContent.ChartSchema, "chart-set", "chart-version", content);
    }

    private static async Task SeedPolicyAsync(CoreLedgerDatabaseScope scope)
    {
        ConfigurationContent content = ManualJournalPolicyTests.Policy().ToConfigurationContent();
        await SeedConfigurationAsync(
            scope,
            ManualJournalPolicy.Area,
            ManualJournalPolicy.Schema,
            "policy-set",
            "manual-journal-policy-version",
            content);
    }

    private static async Task SeedConfigurationAsync(
        CoreLedgerDatabaseScope scope,
        string area,
        string schema,
        string setSuffix,
        string versionId,
        ConfigurationContent content)
    {
        DateTimeOffset effectiveFrom = CoreLedgerDatabaseScope.SeededAt.AddMinutes(-5);
        await using CoreLedgerDbContext context = scope.CreateContext();
        string configurationSetId = scope.Institution.Value + "-" + setSuffix;
        context.ConfigurationSets.Add(new ConfigurationSetRecord
        {
            InstitutionId = scope.Institution.Value,
            ConfigurationSetId = configurationSetId,
            Area = area,
            Scope = "institution",
            Version = 1,
            CreatedAt = effectiveFrom,
        });
        context.ConfigurationVersions.Add(new ConfigurationVersionRecord
        {
            InstitutionId = scope.Institution.Value,
            VersionId = versionId,
            ConfigurationSetId = configurationSetId,
            VersionNumber = 1,
            Schema = schema,
            ContentJson = content.CanonicalJson,
            ContentHash = content.Hash,
            Status = "published",
            CreatedByActorId = "db-test",
            ApprovedByActorId = "db-checker",
            ApprovalReference = area + "-approval",
            EffectiveFrom = effectiveFrom,
            EffectiveTo = null,
            PublishedAt = effectiveFrom,
            Version = 3,
            CreatedAt = effectiveFrom,
        });
        context.ConfigurationPublications.Add(new ConfigurationPublicationRecord
        {
            InstitutionId = scope.Institution.Value,
            PublicationId = setSuffix + "-publication",
            ConfigurationSetId = configurationSetId,
            VersionId = versionId,
            ContentHash = content.Hash,
            PublishedByActorId = "db-checker",
            ApprovalReference = area + "-approval",
            EffectiveFrom = effectiveFrom,
            EffectiveTo = null,
            PublishedAt = effectiveFrom,
        });
        await context.SaveChangesAsync();
    }

    private static async Task SeedBusinessDateAsync(CoreLedgerDatabaseScope scope)
    {
        await using CoreLedgerDbContext context = scope.CreateContext();
        if (await context.BusinessDateControls.AnyAsync())
        {
            return;
        }

        const string calendarId = "manual-journal-calendar";
        const string calendarVersionId = "manual-journal-calendar-v1";
        context.BusinessCalendars.Add(new BusinessCalendarRecord
        {
            InstitutionId = scope.Institution.Value,
            CalendarId = calendarId,
            Name = "Manual journal test calendar",
            Version = 1,
            CreatedAt = CoreLedgerDatabaseScope.SeededAt,
        });
        context.CalendarVersions.Add(new CalendarVersionRecord
        {
            InstitutionId = scope.Institution.Value,
            VersionId = calendarVersionId,
            CalendarId = calendarId,
            VersionNumber = 1,
            TimeZoneId = "Africa/Lagos",
            EffectiveFrom = new DateOnly(2026, 1, 1),
            EffectiveTo = new DateOnly(2026, 12, 31),
            InitialBusinessDate = new DateOnly(2026, 11, 8),
            Status = "published",
            CreatedByActorId = Maker.Value,
            ApprovedByActorId = "manual-journal-checker",
            ApprovalReference = "manual-journal-calendar-approval",
            Version = 1,
            CreatedAt = CoreLedgerDatabaseScope.SeededAt,
            PublishedAt = CoreLedgerDatabaseScope.SeededAt,
        });
        context.BusinessDateControls.Add(new BusinessDateControlRecord
        {
            InstitutionId = scope.Institution.Value,
            CurrentBusinessDate = new DateOnly(2026, 11, 8),
            CalendarVersionId = calendarVersionId,
            Version = 1,
            UpdatedAt = CoreLedgerDatabaseScope.SeededAt,
        });
        await context.SaveChangesAsync();
    }

    private sealed record JournalFixture(
        string CustomerLedgerAccountId,
        string CashLedgerAccountId,
        string PolicyVersionId);
}
