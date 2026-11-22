using System;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Tenancy;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.CoreLedger.Domain.Ledger;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.CoreLedger.Infrastructure.Ledger;

public enum FinancialIdempotencyClaimKind
{
    Claimed,
    Replayed,
    Conflict,
    InProgress,
    Missing,
}

public sealed record FinancialIdempotencyClaim(FinancialIdempotencyClaimKind Kind, string? Result)
{
    public static FinancialIdempotencyClaim Claimed() => new(FinancialIdempotencyClaimKind.Claimed, null);

    public static FinancialIdempotencyClaim Replayed(string? result) => new(FinancialIdempotencyClaimKind.Replayed, result);

    public static FinancialIdempotencyClaim Conflict() => new(FinancialIdempotencyClaimKind.Conflict, null);

    public static FinancialIdempotencyClaim InProgress() => new(FinancialIdempotencyClaimKind.InProgress, null);

    public static FinancialIdempotencyClaim Missing() => new(FinancialIdempotencyClaimKind.Missing, null);
}

/// <summary>
/// Claims and completes a financial idempotency record inside the caller-owned posting transaction.
/// It never commits, rolls back or deletes a claim on behalf of the posting use case.
/// </summary>
public sealed class FinancialIdempotencyService
{
    private const int MaximumOutcomeLength = 16_384;

    private readonly CoreLedgerDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;

    public FinancialIdempotencyService(
        CoreLedgerDbContext context,
        ITenantContext tenantContext,
        TimeProvider timeProvider)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<FinancialIdempotencyClaim> ClaimAsync(
        PostingIdempotencyScope scope,
        string requestHash,
        CancellationToken cancellationToken)
    {
        Validate(scope, requestHash);
        if (_context.Database.CurrentTransaction is null)
        {
            throw new LedgerPostingException("financial_transaction_required");
        }

        int inserted = await _context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO integration.idempotency_records
                (institution_id, operation, initiator, idempotency_key, request_fingerprint, status, result, created_at, completed_at)
            VALUES
                ({scope.InstitutionId.Value}, {scope.Operation.Value}, {scope.Initiator.Value}, {scope.Key.Value},
                 {requestHash}, 'InProgress', NULL, {_timeProvider.GetUtcNow()}, NULL)
            ON CONFLICT (institution_id, operation, initiator, idempotency_key) DO NOTHING
            """,
            cancellationToken).ConfigureAwait(false);

        if (inserted == 1)
        {
            return FinancialIdempotencyClaim.Claimed();
        }

        return await ClassifyAsync(scope, requestHash, cancellationToken).ConfigureAwait(false);
    }

    public async Task StageOutcomeAsync(
        PostingIdempotencyScope scope,
        string requestHash,
        string outcome,
        CancellationToken cancellationToken)
    {
        Validate(scope, requestHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(outcome);
        if (outcome.Length > MaximumOutcomeLength)
        {
            throw new ArgumentOutOfRangeException(nameof(outcome), "Financial idempotency outcome is too large.");
        }

        if (_context.Database.CurrentTransaction is null)
        {
            throw new LedgerPostingException("financial_transaction_required");
        }

        IdempotencyRecord record = await FindAsync(scope, cancellationToken).ConfigureAwait(false)
            ?? throw new LedgerPostingException("financial_idempotency_claim_missing");

        if (!string.Equals(record.RequestFingerprint, requestHash, StringComparison.Ordinal))
        {
            throw new LedgerPostingException("ledger_idempotency_conflict");
        }

        if (record.Status == IdempotencyRecordStatus.Completed)
        {
            if (!string.Equals(record.Result, outcome, StringComparison.Ordinal))
            {
                throw new LedgerPostingException("financial_idempotency_outcome_conflict");
            }

            return;
        }

        record.Status = IdempotencyRecordStatus.Completed;
        record.Result = outcome;
        record.CompletedAt = _timeProvider.GetUtcNow();
    }

    public async Task<FinancialIdempotencyClaim> LookupAsync(
        PostingIdempotencyScope scope,
        string requestHash,
        CancellationToken cancellationToken)
    {
        Validate(scope, requestHash);
        return await ClassifyAsync(scope, requestHash, cancellationToken).ConfigureAwait(false);
    }

    private async Task<FinancialIdempotencyClaim> ClassifyAsync(
        PostingIdempotencyScope scope,
        string requestHash,
        CancellationToken cancellationToken)
    {
        IdempotencyRecord? existing = await FindAsync(scope, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return FinancialIdempotencyClaim.Missing();
        }

        if (!string.Equals(existing.RequestFingerprint, requestHash, StringComparison.Ordinal))
        {
            return FinancialIdempotencyClaim.Conflict();
        }

        return existing.Status == IdempotencyRecordStatus.Completed
            ? FinancialIdempotencyClaim.Replayed(existing.Result)
            : FinancialIdempotencyClaim.InProgress();
    }

    private Task<IdempotencyRecord?> FindAsync(PostingIdempotencyScope scope, CancellationToken cancellationToken) =>
        _context.IdempotencyRecords.SingleOrDefaultAsync(
            record => record.InstitutionId == scope.InstitutionId.Value
                      && record.Operation == scope.Operation.Value
                      && record.Initiator == scope.Initiator.Value
                      && record.IdempotencyKey == scope.Key.Value,
            cancellationToken);

    private void Validate(PostingIdempotencyScope scope, string requestHash)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestHash);

        if (requestHash.Length != 64)
        {
            throw new ArgumentException("Request hash must be a SHA-256 hexadecimal digest.", nameof(requestHash));
        }

        for (int index = 0; index < requestHash.Length; index++)
        {
            char character = requestHash[index];
            if (character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
            {
                throw new ArgumentException("Request hash must be lowercase hexadecimal.", nameof(requestHash));
            }
        }

        if (!string.Equals(_tenantContext.CurrentInstitution?.Value, scope.InstitutionId.Value, StringComparison.Ordinal))
        {
            throw new LedgerPostingException("institution_context_mismatch");
        }
    }
}
