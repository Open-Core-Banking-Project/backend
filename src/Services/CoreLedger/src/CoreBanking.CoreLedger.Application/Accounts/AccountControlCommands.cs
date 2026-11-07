using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CoreLedger.Domain.Accounts;

namespace CoreBanking.CoreLedger.Application.Accounts;

public sealed record ApplyAccountRestrictionCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string AccountId,
    string RequestId,
    string RestrictionTypeCode,
    string PostingDirectionCode,
    IReadOnlyCollection<string> ChannelScopeCodes,
    IReadOnlyCollection<string> TransactionTypeScopeCodes,
    string ReasonCode,
    string? AuthorityReference,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    string CorrelationId);

public sealed record ApplyAccountFreezeCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string AccountId,
    string RequestId,
    string FreezeTypeCode,
    string ReasonCode,
    string AuthorityReference,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    string CorrelationId);

public sealed record ApplyAccountBlockCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string AccountId,
    string RequestId,
    string BlockTypeCode,
    IReadOnlyCollection<string> OperationScopeCodes,
    string ReasonCode,
    string? AuthorityReference,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    string CorrelationId);

public sealed record AccountControlChangeResult(
    string ControlId,
    string AccountId,
    string ControlKind,
    string ControlTypeCode,
    string Status,
    long ControlVersion,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    DateTimeOffset ChangedAt,
    bool Replayed);

public sealed record ReleaseAccountControlCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string AccountId,
    string ControlId,
    string ControlKind,
    string ReasonCode,
    string? ApprovalReference,
    string CorrelationId);

public sealed record EvaluateAccountControlsCommand(
    InstitutionId InstitutionId,
    string AccountId,
    string PostingIntentId,
    string PostingDirectionCode,
    string TransactionTypeCode,
    string ChannelCode,
    MonetaryAmountView Amount,
    long KnownControlVersion,
    DateTimeOffset EvaluationTime);

public sealed record MonetaryAmountView(decimal Amount, string Currency);

public sealed record AccountControlEvaluationResult(
    string AccountId,
    string PostingIntentId,
    string Outcome,
    IReadOnlyCollection<string> ReasonCodes,
    IReadOnlyCollection<AccountControlDescriptor> EffectiveControls,
    long ControlVersion,
    DateTimeOffset EvaluatedAt);

/// <summary>
/// Applies, releases and evaluates the account controls under the approved deny-dominant precedence.
/// Applying is an authorised immediate action with a reason; releasing a manually releasable control needs
/// dual control. Decisions: P1-E04-D10.
/// </summary>
public interface IAccountControlService
{
    Task<AccountControlChangeResult> ApplyRestrictionAsync(
        ApplyAccountRestrictionCommand command,
        CancellationToken cancellationToken);

    Task<AccountControlChangeResult> ApplyFreezeAsync(
        ApplyAccountFreezeCommand command,
        CancellationToken cancellationToken);

    Task<AccountControlChangeResult> ApplyBlockAsync(
        ApplyAccountBlockCommand command,
        CancellationToken cancellationToken);

    Task<AccountControlChangeResult> ReleaseAsync(
        ReleaseAccountControlCommand command,
        CancellationToken cancellationToken);

    Task<AccountControlEvaluationResult> EvaluateAsync(
        EvaluateAccountControlsCommand command,
        CancellationToken cancellationToken);
}
