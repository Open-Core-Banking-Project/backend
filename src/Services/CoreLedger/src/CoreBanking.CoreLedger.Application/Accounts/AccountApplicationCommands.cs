using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CoreLedger.Domain.Accounts;
using CoreBanking.CoreLedger.Domain.Products;

namespace CoreBanking.CoreLedger.Application.Accounts;

public sealed record CreateAccountApplicationCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string ApplicationId,
    string RequestId,
    string CustomerId,
    string ProductId,
    string BranchId,
    string CurrencyCode,
    string AccountPurposeCode,
    string OwnershipTypeCode,
    string CorrelationId);

public sealed record AccountApplicationCreatedResult(
    string ApplicationId,
    string RequestId,
    string CustomerId,
    string ProductId,
    string ProductCode,
    string ProductFamily,
    string ProductVersionId,
    int ProductVersionNumber,
    string ProductContentHash,
    DateTimeOffset ProductEffectiveFrom,
    DateTimeOffset? ProductEffectiveTo,
    string BranchId,
    string CurrencyCode,
    string AccountPurposeCode,
    string OwnershipTypeCode,
    AccountApplicationStatus Status,
    long Version,
    DateTimeOffset CreatedAt,
    bool Replayed);

public sealed record EvaluateAccountEligibilityCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string ApplicationId,
    long ExpectedVersion,
    string ReasonCode,
    string CorrelationId);

public sealed record AccountEligibilityResult(
    string ApplicationId,
    AccountApplicationStatus Status,
    long Version,
    string EvaluationId,
    string ProductVersionId,
    string ProductContentHash,
    bool Eligible,
    bool FactsCurrent,
    string DecisionCode,
    string CustomerFactsVersion,
    string BranchReferenceVersion,
    string CurrencyReferenceVersion,
    IReadOnlyCollection<EligibilityRuleTrace> Trace,
    AccountLimitAssessment? Limit,
    DateTimeOffset EvaluatedAt,
    bool Replayed);

public sealed record GetAccountApplicationQuery(
    InstitutionId InstitutionId,
    string ApplicationId);

/// <summary>
/// Owns the account-application boundary of P1-E04 slice 7: a draft application, then one
/// authoritative eligibility evaluation whose snapshot is immutable decision evidence.
/// No account, account number or balance is created here. Decisions: P1-E04-D07, P1-E04-D09, P1-E04-D17.
/// </summary>
public interface IAccountApplicationService
{
    Task<AccountApplicationCreatedResult> CreateAsync(
        CreateAccountApplicationCommand command,
        CancellationToken cancellationToken);

    Task<AccountEligibilityResult> EvaluateAsync(
        EvaluateAccountEligibilityCommand command,
        CancellationToken cancellationToken);

    Task<AccountEligibilityResult?> GetLatestEvaluationAsync(
        GetAccountApplicationQuery query,
        CancellationToken cancellationToken);
}

public sealed class AccountApplicationRejectedException : Exception
{
    public AccountApplicationRejectedException(string code, string message) : base(message) => Code = code;

    public AccountApplicationRejectedException(string code, string message, Exception inner)
        : base(message, inner) => Code = code;

    public string Code { get; }
}
