using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.CoreLedger.Domain.Ledger;
using CoreBanking.CoreLedger.Infrastructure.Accounts;
using CoreBanking.CoreLedger.Infrastructure.Ledger;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CoreBanking.CoreLedger.UnitTests.Persistence;

/// <summary>Real PostgreSQL reversal, replay and contention evidence for P1-E05-S9.</summary>
[Trait("Category", "Database")]
public sealed class LedgerReversalDatabaseTests
{
    private static readonly ActorId Actor = ActorId.From("ledger-reversal-actor");

    [Fact]
    public async Task Full_reversal_commits_exact_opposite_link_audit_outbox_and_replays_once()
    {
        CoreLedgerDatabaseScope scope = new();
        FixedTimeProvider time = new(CoreLedgerDatabaseScope.SeededAt);
        (string source, string recipient) = await PostOriginalAsync(scope, time, "reversal");
        await SeedBusinessDateAsync(scope);
        FullLedgerReversalCommand command = ReversalCommand(scope, "reversal", source, recipient);

        LedgerPostingResult first = await ReverseAsync(scope, time, command);
        LedgerPostingResult replay = await ReverseAsync(scope, time, command);

        Assert.False(first.Replayed);
        Assert.True(replay.Replayed);
        await using CoreLedgerDbContext verification = scope.CreateContext();
        FinancialReversalRecord link = await verification.FinancialReversals.SingleAsync(
            record => record.OriginalFinancialTransactionId == "reversal-original-transaction");
        Assert.Equal("reversal-reversal-transaction", link.ReversalFinancialTransactionId);
        Assert.Equal(75m, link.ReversedAmount);
        Assert.Equal(0m, link.RemainingAfter);
        Assert.Equal("customer_refund", link.ReasonCode);
        Assert.Equal(2, await verification.JournalEntries.CountAsync(
            record => record.FinancialTransactionId == "reversal-original-transaction"
                || record.FinancialTransactionId == "reversal-reversal-transaction"));
        Assert.Equal(1, await verification.OutboxMessages.CountAsync(
            record => record.EventType == "financial_transaction_reversed"
                && record.BusinessReference == "reversal-original-transaction"));
        Assert.Equal(1, await verification.InstitutionAuditEvents.CountAsync(
            record => record.Action == "ledger.transaction.reverse"
                && record.ResourceId == "reversal-reversal-transaction"
                && record.Outcome == "succeeded"));
        await Assert.ThrowsAnyAsync<Exception>(() => verification.Database.ExecuteSqlRawAsync(
            "UPDATE ledger.financial_reversals SET reason_code = 'tampered' WHERE institution_id = {0} AND financial_reversal_id = {1}",
            scope.Institution.Value,
            link.FinancialReversalId));
        LedgerBalanceRecord sourceBalance = await verification.LedgerBalances.SingleAsync(
            record => record.LedgerAccountId == source);
        LedgerBalanceRecord recipientBalance = await verification.LedgerBalances.SingleAsync(
            record => record.LedgerAccountId == recipient);
        Assert.Equal(-100m, sourceBalance.NetBalance);
        Assert.Equal(0m, recipientBalance.NetBalance);
    }

    [Fact]
    public async Task Concurrent_different_keys_cannot_reverse_the_same_original_twice()
    {
        CoreLedgerDatabaseScope scope = new();
        FixedTimeProvider time = new(CoreLedgerDatabaseScope.SeededAt);
        (string source, string recipient) = await PostOriginalAsync(scope, time, "concurrent-reversal");
        await SeedBusinessDateAsync(scope);
        FullLedgerReversalCommand first = ReversalCommand(scope, "concurrent-reversal-a", source, recipient) with
        {
            OriginalFinancialTransactionId = "concurrent-reversal-original-transaction",
        };
        FullLedgerReversalCommand second = ReversalCommand(scope, "concurrent-reversal-b", source, recipient) with
        {
            OriginalFinancialTransactionId = "concurrent-reversal-original-transaction",
        };

        Exception?[] outcomes = await Task.WhenAll(
            CaptureReverseAsync(scope, time, first),
            CaptureReverseAsync(scope, time, second));

        Assert.Single(outcomes, outcome => outcome is null);
        LedgerPostingException refusal = Assert.Single(outcomes.OfType<LedgerPostingException>());
        Assert.Equal("financial_transaction_already_reversed", refusal.Code);
        await using CoreLedgerDbContext verification = scope.CreateContext();
        Assert.Equal(1, await verification.FinancialReversals.CountAsync(
            record => record.OriginalFinancialTransactionId == "concurrent-reversal-original-transaction"));
    }

    [Fact]
    public async Task Same_key_with_changed_evidence_conflicts_without_a_second_reversal()
    {
        CoreLedgerDatabaseScope scope = new();
        FixedTimeProvider time = new(CoreLedgerDatabaseScope.SeededAt);
        (string source, string recipient) = await PostOriginalAsync(scope, time, "hash-conflict");
        await SeedBusinessDateAsync(scope);
        FullLedgerReversalCommand original = ReversalCommand(scope, "hash-conflict", source, recipient);
        await ReverseAsync(scope, time, original);

        LedgerPostingException conflict = await Assert.ThrowsAsync<LedgerPostingException>(() =>
            ReverseAsync(scope, time, original with { EvidenceReference = "different-case-reference" }));

        Assert.Equal("ledger_idempotency_conflict", conflict.Code);
        await using CoreLedgerDbContext verification = scope.CreateContext();
        Assert.Equal(1, await verification.FinancialReversals.CountAsync(
            record => record.OriginalFinancialTransactionId == "hash-conflict-original-transaction"));
        Assert.Equal(1, await verification.InstitutionAuditEvents.CountAsync(
            record => record.Action == "ledger.transaction.reverse"
                && record.Outcome == "rejected"
                && record.Reason == "ledger_idempotency_conflict"));
    }

    [Fact]
    public async Task Uncertain_provider_evidence_is_refused_and_audited_without_a_posting()
    {
        CoreLedgerDatabaseScope scope = new();
        FixedTimeProvider time = new(CoreLedgerDatabaseScope.SeededAt);
        (string source, string recipient) = await PostOriginalAsync(scope, time, "uncertain-reversal");
        await SeedBusinessDateAsync(scope);
        FullLedgerReversalCommand command = ReversalCommand(scope, "uncertain-reversal", source, recipient) with
        {
            OriginalFinancialTransactionId = "uncertain-reversal-original-transaction",
            FinalityEvidence = ReversalFinalityEvidence.ProviderUncertain,
        };

        LedgerPostingException refusal = await Assert.ThrowsAsync<LedgerPostingException>(
            () => ReverseAsync(scope, time, command));

        Assert.Equal("financial_reversal_original_not_final", refusal.Code);
        await using CoreLedgerDbContext verification = scope.CreateContext();
        Assert.False(await verification.FinancialReversals.AnyAsync(
            record => record.OriginalFinancialTransactionId == command.OriginalFinancialTransactionId));
        Assert.False(await verification.FinancialTransactions.AnyAsync(
            record => record.FinancialTransactionId == command.ReversalFinancialTransactionId));
        Assert.Equal(1, await verification.InstitutionAuditEvents.CountAsync(
            record => record.Action == "ledger.transaction.reverse"
                && record.ResourceId == command.OriginalFinancialTransactionId
                && record.Outcome == "rejected"
                && record.Reason == "financial_reversal_original_not_final"));
    }

    private static async Task<(string Source, string Recipient)> PostOriginalAsync(
        CoreLedgerDatabaseScope scope,
        TimeProvider time,
        string prefix)
    {
        string source = await OpenLedgerAccountAsync(scope, prefix + "-source", time);
        string recipient = await OpenLedgerAccountAsync(scope, prefix + "-recipient", time);
        await FundAsync(scope, source, 100m);
        AmbientTenantContext tenant = scope.NewTenant();
        await using CoreLedgerDbContext context = scope.CreateContext(tenant);
        await PostingService(context, tenant, time).PostAsync(
            PostingCommand(scope, prefix + "-original", source, recipient),
            CancellationToken.None);
        return (source, recipient);
    }

    private static async Task<string> OpenLedgerAccountAsync(
        CoreLedgerDatabaseScope scope,
        string suffix,
        TimeProvider time)
    {
        string accountId = await AccountDatabaseScenario.OpenAccountAsync(
            scope,
            suffix + "-application",
            suffix + "-customer",
            Actor,
            time);
        await using CoreLedgerDbContext context = scope.CreateContext();
        return await context.LedgerAccounts
            .Where(record => record.AccountId == accountId)
            .Select(record => record.LedgerAccountId)
            .SingleAsync();
    }

    private static async Task FundAsync(CoreLedgerDatabaseScope scope, string ledgerAccountId, decimal amount)
    {
        await using CoreLedgerDbContext context = scope.CreateContext();
        LedgerBalanceRecord balance = await context.LedgerBalances.SingleAsync(
            record => record.LedgerAccountId == ledgerAccountId);
        balance.CreditTotal = amount;
        balance.NetBalance = -amount;
        balance.Version++;
        balance.AsOf = CoreLedgerDatabaseScope.SeededAt;
        await context.SaveChangesAsync();
    }

    private static async Task SeedBusinessDateAsync(CoreLedgerDatabaseScope scope)
    {
        await using CoreLedgerDbContext context = scope.CreateContext();
        if (await context.BusinessDateControls.AnyAsync())
        {
            return;
        }

        const string calendarId = "reversal-test-calendar";
        const string calendarVersionId = "reversal-test-calendar-v1";
        context.BusinessCalendars.Add(new BusinessCalendarRecord
        {
            InstitutionId = scope.Institution.Value,
            CalendarId = calendarId,
            Name = "Reversal test calendar",
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
            CreatedByActorId = Actor.Value,
            ApprovedByActorId = "ledger-reversal-checker",
            ApprovalReference = "reversal-test-calendar-approval",
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

    private static LedgerReversalService ReversalService(
        CoreLedgerDbContext context,
        AmbientTenantContext tenant,
        TimeProvider time) =>
        new(context, PostingService(context, tenant, time), tenant, time);

    private static async Task<LedgerPostingResult> ReverseAsync(
        CoreLedgerDatabaseScope scope,
        TimeProvider time,
        FullLedgerReversalCommand command)
    {
        AmbientTenantContext tenant = scope.NewTenant();
        await using CoreLedgerDbContext context = scope.CreateContext(tenant);
        return await ReversalService(context, tenant, time).ReverseFullAsync(command, CancellationToken.None);
    }

    private static async Task<Exception?> CaptureReverseAsync(
        CoreLedgerDatabaseScope scope,
        TimeProvider time,
        FullLedgerReversalCommand command)
    {
        try
        {
            await ReverseAsync(scope, time, command);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static AtomicLedgerPostingCommand PostingCommand(
        CoreLedgerDatabaseScope scope,
        string prefix,
        string debitAccountId,
        string creditAccountId)
    {
        FinancialDates dates = FinancialDates.Create(
            CoreLedgerDatabaseScope.SeededAt,
            new DateOnly(2026, 11, 8),
            new DateOnly(2026, 11, 8),
            new DateOnly(2026, 11, 8));
        string hash = CanonicalFinancialRequest.Create("""{"amount":75,"currency":"NGN"}""").Hash;
        FinancialTransaction transaction = FinancialTransaction.CreateReadyToPost(
            prefix + "-transaction",
            prefix + "-TX",
            scope.Institution.Value,
            "payments-vtu",
            prefix + "-source-reference",
            "internal_transfer.v1",
            hash,
            dates);
        AccountingScope accountingScope = new(scope.Institution.Value, "legal-entity-1", CurrencyCode.Parse("NGN"));
        JournalEntry journal = JournalEntry.Create(
            prefix + "-journal",
            transaction.Id,
            prefix + "-JRN",
            "mapping-version-1",
            accountingScope,
            dates,
            [
                JournalLine.Create(prefix + "-line-1", 1, debitAccountId, DebitCreditIndicator.Debit,
                    AccountingComponentType.Principal, Money.Create(75m, "NGN"), accountingScope),
                JournalLine.Create(prefix + "-line-2", 2, creditAccountId, DebitCreditIndicator.Credit,
                    AccountingComponentType.Principal, Money.Create(75m, "NGN"), accountingScope),
            ]);
        return new AtomicLedgerPostingCommand(
            PostingIdempotencyScope.Create(scope.Institution.Value, "ledger.transaction.post", "payments-vtu", prefix + "-key"),
            transaction,
            journal,
            new LedgerPostingControlContext(
                "api",
                "internal_transfer",
                new Dictionary<string, long>(StringComparer.Ordinal)
                {
                    [debitAccountId] = 0,
                    [creditAccountId] = 0,
                }),
            Actor.Value,
            "service",
            prefix + "-correlation");
    }

    private static FullLedgerReversalCommand ReversalCommand(
        CoreLedgerDatabaseScope scope,
        string prefix,
        string source,
        string recipient) =>
        new(
            PostingIdempotencyScope.Create(
                scope.Institution.Value,
                "ledger.transaction.reverse",
                Actor.Value,
                prefix + "-reversal-key"),
            prefix + "-original-transaction",
            prefix + "-financial-reversal",
            prefix + "-reversal-transaction",
            prefix + "-REV-TX",
            prefix + "-reversal-journal",
            prefix + "-REV-JRN",
            new LedgerPostingControlContext(
                "operations",
                "financial_reversal",
                new Dictionary<string, long>(StringComparer.Ordinal)
                {
                    [source] = 0,
                    [recipient] = 0,
                }),
            "customer_refund",
            prefix + "-case-reference",
            ReversalFinalityEvidence.LedgerPosted,
            Actor.Value,
            "staff",
            prefix + "-reversal-correlation");
}
