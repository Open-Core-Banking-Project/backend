using System;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.CoreLedger.Domain.Ledger;

namespace CoreBanking.CoreLedger.Application.Ledger;

public sealed record CreateLedgerHoldCommand(
    PostingIdempotencyScope IdempotencyScope,
    string RequestHash,
    string LedgerAccountId,
    string SourceService,
    string SourceReference,
    string PurposeCode,
    Money Amount,
    DateTimeOffset ExpiresAt,
    string ChannelCode,
    string TransactionTypeCode,
    long KnownControlVersion,
    string ActorId,
    string ActorType,
    string CorrelationId,
    string? CausationId = null);

public sealed record ReleaseLedgerHoldCommand(
    PostingIdempotencyScope IdempotencyScope,
    string RequestHash,
    string HoldId,
    decimal Amount,
    string ReasonCode,
    long ExpectedVersion,
    string ActorId,
    string ActorType,
    string CorrelationId,
    string? CausationId = null);

public sealed record ExpireLedgerHoldCommand(
    PostingIdempotencyScope IdempotencyScope,
    string RequestHash,
    string HoldId,
    long ExpectedVersion,
    string ActorId,
    string ActorType,
    string CorrelationId,
    string? CausationId = null);

public sealed record LedgerHoldResult(
    string HoldId,
    string LedgerAccountId,
    decimal OriginalAmount,
    decimal RemainingAmount,
    decimal CapturedAmount,
    decimal ReleasedAmount,
    string Currency,
    string Status,
    long Version,
    DateTimeOffset ExpiresAt,
    DateTimeOffset ChangedAt,
    bool Replayed);

public interface ILedgerHoldService
{
    Task<LedgerHoldResult> CreateAsync(CreateLedgerHoldCommand command, CancellationToken cancellationToken);

    Task<LedgerHoldResult> ReleaseAsync(ReleaseLedgerHoldCommand command, CancellationToken cancellationToken);

    Task<LedgerHoldResult> ExpireAsync(ExpireLedgerHoldCommand command, CancellationToken cancellationToken);
}
