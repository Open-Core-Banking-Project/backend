using System;
using System.Threading.Tasks;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Xunit;

namespace CoreBanking.CoreLedger.UnitTests.Persistence;

/// <summary>Real PostgreSQL evidence for P1-E05 slice 4's commit-time and immutability guards.</summary>
[Trait("Category", "Database")]
public sealed class LedgerPostingDatabaseTests : IDisposable
{
    private readonly CoreLedgerDatabaseScope _scope = new();

    public void Dispose() => _scope.Dispose();

    [Fact]
    public async Task Balanced_journal_passes_the_deferred_commit_guard()
    {
        await using CoreLedgerDbContext context = _scope.CreateContext();
        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync();
        AddPostedJournal(context, "balanced", 100m, 100m);

        await context.SaveChangesAsync();
        await context.Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE");

        Assert.Equal(2, await context.JournalLines.CountAsync());
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Unbalanced_lines_are_rejected_by_the_deferred_database_guard()
    {
        await using CoreLedgerDbContext context = _scope.CreateContext();
        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync();
        AddPostedJournal(context, "unbalanced", 100m, 99m, headerTotal: 100m);
        await context.SaveChangesAsync();

        PostgresException exception = await Assert.ThrowsAsync<PostgresException>(
            () => context.Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Posted_transaction_without_a_journal_is_rejected_at_commit()
    {
        await using CoreLedgerDbContext context = _scope.CreateContext();
        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync();
        AddPostedTransaction(context, "orphan");
        await context.SaveChangesAsync();

        PostgresException exception = await Assert.ThrowsAsync<PostgresException>(
            () => context.Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Database_rejects_update_and_delete_of_posted_journal_evidence()
    {
        await AssertMutationRejectedAsync(
            "UPDATE ledger.journal_lines SET amount = 1 WHERE institution_id = {0} AND journal_line_id = {1}",
            "immutable-update");
        await AssertMutationRejectedAsync(
            "DELETE FROM ledger.journal_entries WHERE institution_id = {0} AND journal_entry_id = {1}",
            "immutable-delete");
    }

    [Fact]
    public async Task Scoped_transaction_and_journal_references_are_unique()
    {
        await using CoreLedgerDbContext context = _scope.CreateContext();
        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync();
        AddPostedJournal(context, "first", 100m, 100m, transactionReference: "TX-UNIQUE", journalReference: "JRN-UNIQUE");
        AddPostedJournal(context, "second", 100m, 100m, transactionReference: "TX-UNIQUE", journalReference: "JRN-SECOND");

        DbUpdateException exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, ((PostgresException)exception.InnerException!).SqlState);
        await transaction.RollbackAsync();
    }

    private async Task AssertMutationRejectedAsync(string sql, string suffix)
    {
        await using CoreLedgerDbContext context = _scope.CreateContext();
        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync();
        AddPostedJournal(context, suffix, 100m, 100m);
        await context.SaveChangesAsync();
        await context.Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE");

        Exception exception = await Assert.ThrowsAnyAsync<Exception>(() => context.Database.ExecuteSqlRawAsync(
            sql,
            _scope.Institution.Value,
            suffix + (sql.StartsWith("UPDATE", StringComparison.Ordinal) ? "-line-1" : "-journal")));

        Assert.True(
            exception is PostgresException
            {
                SqlState: PostgresErrorCodes.ObjectNotInPrerequisiteState or PostgresErrorCodes.InsufficientPrivilege,
            }
            or NpgsqlException
            {
                InnerException: PostgresException
                {
                    SqlState: PostgresErrorCodes.ObjectNotInPrerequisiteState or PostgresErrorCodes.InsufficientPrivilege,
                },
            },
            exception.ToString());
        await transaction.RollbackAsync();
    }

    private void AddPostedJournal(
        CoreLedgerDbContext context,
        string suffix,
        decimal debit,
        decimal credit,
        decimal? headerTotal = null,
        string? transactionReference = null,
        string? journalReference = null)
    {
        DateTimeOffset now = CoreLedgerDatabaseScope.SeededAt;
        DateOnly date = DateOnly.FromDateTime(now.UtcDateTime);
        string transactionId = suffix + "-transaction";
        string journalId = suffix + "-journal";
        decimal total = headerTotal ?? debit;

        AddPostedTransaction(context, suffix, transactionReference);
        context.JournalEntries.Add(new JournalEntryRecord
        {
            InstitutionId = _scope.Institution.Value,
            JournalEntryId = journalId,
            FinancialTransactionId = transactionId,
            JournalReference = journalReference ?? suffix + "-JRN",
            MappingVersionId = "synthetic-mapping-v1",
            LegalEntityCode = "synthetic-entity",
            CurrencyCode = "NGN",
            Status = "posted",
            TotalDebits = total,
            TotalCredits = total,
            TransactionTimestamp = now,
            BusinessDate = date,
            PostingDate = date,
            ValueDate = date,
            CreatedAt = now,
        });
        context.JournalLines.AddRange(
            Line(suffix + "-line-1", journalId, 1, "debit-account", "debit", debit, now),
            Line(suffix + "-line-2", journalId, 2, "credit-account", "credit", credit, now));
    }

    private void AddPostedTransaction(CoreLedgerDbContext context, string suffix, string? transactionReference = null)
    {
        DateTimeOffset now = CoreLedgerDatabaseScope.SeededAt;
        DateOnly date = DateOnly.FromDateTime(now.UtcDateTime);
        context.FinancialTransactions.Add(new FinancialTransactionRecord
        {
            InstitutionId = _scope.Institution.Value,
            FinancialTransactionId = suffix + "-transaction",
            TransactionReference = transactionReference ?? suffix + "-TX",
            SourceService = "database-tests",
            SourceReference = suffix + "-source",
            EventCode = "synthetic_test.v1",
            RequestHash = new string('a', 64),
            Status = "posted",
            TransactionTimestamp = now,
            BusinessDate = date,
            PostingDate = date,
            ValueDate = date,
            Version = 1,
            CreatedAt = now,
            PostedAt = now,
        });
    }

    private JournalLineRecord Line(
        string lineId,
        string journalId,
        int sequence,
        string ledgerAccountId,
        string direction,
        decimal amount,
        DateTimeOffset createdAt) => new()
        {
            InstitutionId = _scope.Institution.Value,
            JournalLineId = lineId,
            JournalEntryId = journalId,
            Sequence = sequence,
            LedgerAccountId = ledgerAccountId,
            LegalEntityCode = "synthetic-entity",
            CurrencyCode = "NGN",
            Direction = direction,
            ComponentType = "principal",
            Amount = amount,
            CreatedAt = createdAt,
        };
}
