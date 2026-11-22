using System;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.CoreLedger.Domain.Ledger;
using CoreBanking.CoreLedger.Infrastructure.Ledger;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace CoreBanking.CoreLedger.UnitTests.Persistence;

/// <summary>Separate-connection contention evidence for P1-E05 slice 5.</summary>
[Trait("Category", "Database")]
public sealed class FinancialIdempotencyDatabaseTests : IDisposable
{
    private readonly CoreLedgerDatabaseScope _scope = new();
    private readonly FixedTimeProvider _time = new(CoreLedgerDatabaseScope.SeededAt);

    public void Dispose() => _scope.Dispose();

    [Fact]
    public async Task Same_scope_and_hash_replays_the_committed_outcome()
    {
        PostingIdempotencyScope scope = Scope("same-hash");
        string hash = Hash("100");
        await RecordOutcomeAsync(scope, hash, "transaction-1");

        await using CoreLedgerDbContext context = _scope.CreateContext();
        FinancialIdempotencyClaim replay = await Service(context).LookupAsync(scope, hash, CancellationToken.None);

        Assert.Equal(FinancialIdempotencyClaimKind.Replayed, replay.Kind);
        Assert.Equal("transaction-1", replay.Result);
    }

    [Fact]
    public async Task Same_scope_with_a_different_hash_is_a_stable_conflict()
    {
        PostingIdempotencyScope scope = Scope("conflict");
        await RecordOutcomeAsync(scope, Hash("100"), "transaction-1");

        await using CoreLedgerDbContext context = _scope.CreateContext();
        FinancialIdempotencyClaim conflict = await Service(context).LookupAsync(
            scope,
            Hash("101"),
            CancellationToken.None);

        Assert.Equal(FinancialIdempotencyClaimKind.Conflict, conflict.Kind);
        Assert.Null(conflict.Result);
    }

    [Fact]
    public async Task Concurrent_duplicate_claims_produce_one_claim_and_one_replay()
    {
        PostingIdempotencyScope scope = Scope("concurrent");
        string hash = Hash("250");

        AmbientTenantContext firstTenant = _scope.NewTenant();
        AmbientTenantContext secondTenant = _scope.NewTenant();
        await using CoreLedgerDbContext firstContext = _scope.CreateContext(firstTenant);
        await using CoreLedgerDbContext secondContext = _scope.CreateContext(secondTenant);
        await using IDbContextTransaction firstTransaction = await firstContext.Database.BeginTransactionAsync();
        await using IDbContextTransaction secondTransaction = await secondContext.Database.BeginTransactionAsync();

        FinancialIdempotencyClaim firstClaim = await Service(firstContext, firstTenant).ClaimAsync(
            scope,
            hash,
            CancellationToken.None);
        Task<FinancialIdempotencyClaim> secondClaimTask = Service(secondContext, secondTenant).ClaimAsync(
            scope,
            hash,
            CancellationToken.None);

        await Task.Delay(100);
        Assert.False(secondClaimTask.IsCompleted);

        await Service(firstContext, firstTenant).StageOutcomeAsync(scope, hash, "transaction-concurrent", CancellationToken.None);
        await firstContext.SaveChangesAsync();
        await firstTransaction.CommitAsync();

        FinancialIdempotencyClaim secondClaim = await secondClaimTask;
        await secondTransaction.CommitAsync();

        Assert.Equal(FinancialIdempotencyClaimKind.Claimed, firstClaim.Kind);
        Assert.Equal(FinancialIdempotencyClaimKind.Replayed, secondClaim.Kind);
        Assert.Equal("transaction-concurrent", secondClaim.Result);
    }

    [Fact]
    public async Task Claim_requires_the_caller_owned_database_transaction()
    {
        await using CoreLedgerDbContext context = _scope.CreateContext();

        LedgerPostingException exception = await Assert.ThrowsAsync<LedgerPostingException>(() =>
            Service(context).ClaimAsync(Scope("no-transaction"), Hash("1"), CancellationToken.None));

        Assert.Equal("financial_transaction_required", exception.Code);
    }

    private async Task RecordOutcomeAsync(PostingIdempotencyScope scope, string hash, string outcome)
    {
        AmbientTenantContext tenant = _scope.NewTenant();
        await using CoreLedgerDbContext context = _scope.CreateContext(tenant);
        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync();
        FinancialIdempotencyService service = Service(context, tenant);

        FinancialIdempotencyClaim claim = await service.ClaimAsync(scope, hash, CancellationToken.None);
        Assert.Equal(FinancialIdempotencyClaimKind.Claimed, claim.Kind);
        await service.StageOutcomeAsync(scope, hash, outcome, CancellationToken.None);
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private FinancialIdempotencyService Service(CoreLedgerDbContext context, AmbientTenantContext? tenant = null) =>
        new(context, tenant ?? _scope.NewTenant(), _time);

    private PostingIdempotencyScope Scope(string key) => PostingIdempotencyScope.Create(
        _scope.Institution.Value,
        "ledger.transaction.post",
        "payments-vtu",
        key);

    private static string Hash(string amount) => CanonicalFinancialRequest.Create(
        $$"""{"amount":{{amount}},"currency":"NGN"}""").Hash;
}
