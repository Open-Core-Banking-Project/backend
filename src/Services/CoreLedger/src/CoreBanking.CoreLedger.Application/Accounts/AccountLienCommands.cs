using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CoreLedger.Domain.Accounts;

namespace CoreBanking.CoreLedger.Application.Accounts;

public sealed record CreateAccountLienCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string AccountId,
    string RequestId,
    decimal Amount,
    string CurrencyCode,
    string PriorityCode,
    int PriorityRank,
    string ReasonCode,
    string? AuthorityReference,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    bool UncoveredAcknowledgedByApproverId,
    string CorrelationId);

public sealed record ReleaseAccountLienCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string AccountId,
    string LienId,
    decimal Amount,
    string ReasonCode,
    string? AuthorityReference,
    string? ApprovalReference,
    long ExpectedOutstandingVersion,
    string CorrelationId);

public sealed record SatisfyAccountLienCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string AccountId,
    string LienId,
    decimal Amount,
    string SettlementReference,
    string CorrelationId);

public sealed record AccountLienResult(
    string LienId,
    string AccountId,
    string PriorityCode,
    int PriorityRank,
    decimal Amount,
    decimal OutstandingAmount,
    string CurrencyCode,
    string Status,
    LienAvailabilityPosition Availability,
    DateTimeOffset ChangedAt,
    bool Replayed);

/// <summary>
/// The lien lifecycle under the approved priority, immutable-amount and dual-control rules. The module never
/// computes an available balance: it reports the ledger's answer and its own outstanding total.
/// Decision: P1-E04-D11.
/// </summary>
public interface IAccountLienService
{
    Task<AccountLienResult> CreateAsync(CreateAccountLienCommand command, CancellationToken cancellationToken);

    Task<AccountLienResult> ReleaseAsync(ReleaseAccountLienCommand command, CancellationToken cancellationToken);

    Task<AccountLienResult> SatisfyAsync(SatisfyAccountLienCommand command, CancellationToken cancellationToken);

    Task<IReadOnlyList<AccountLienResult>> ListActiveAsync(
        InstitutionId institutionId,
        string accountId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken);
}

/// <summary>The ledger's availability answer. The account module never derives it. Decision: P1-E04-D11.</summary>
public interface ILedgerAvailabilitySource
{
    Task<LedgerAvailabilitySnapshot> GetAsync(
        InstitutionId institutionId,
        string accountId,
        string currencyCode,
        CancellationToken cancellationToken);
}
