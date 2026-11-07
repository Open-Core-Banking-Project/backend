using System;
using System.Collections.Generic;

namespace CoreBanking.CoreLedger.Contracts;

public sealed record CreateAccountRestrictionRequestV1(
    string RequestId,
    string RestrictionTypeCode,
    string PostingDirectionCode,
    IReadOnlyCollection<string> ChannelScopeCodes,
    IReadOnlyCollection<string> TransactionTypeScopeCodes,
    string ReasonCode,
    string? AuthorityReference,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo);

public sealed record RestrictionResponseV1(
    string InstitutionId,
    string RestrictionId,
    string AccountId,
    string RestrictionTypeCode,
    string PostingDirectionCode,
    string Status,
    long ControlVersion,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    DateTimeOffset ChangedAt,
    bool Replayed);

public sealed record CreateAccountFreezeRequestV1(
    string RequestId,
    string FreezeTypeCode,
    string ReasonCode,
    string AuthorityReference,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo);

public sealed record CreateAccountBlockRequestV1(
    string RequestId,
    string BlockTypeCode,
    IReadOnlyCollection<string> OperationScopeCodes,
    string ReasonCode,
    string? AuthorityReference,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo);

public sealed record ReleaseAccountControlRequestV1(
    string ControlKind,
    string ReasonCode,
    string? ApprovalReference);

public sealed record CreateAccountLienRequestV1(string RequestId,
    MonetaryAmountV1 Amount,
    string PriorityCode,
    int PriorityRank,
    string ReasonCode,
    string? AuthorityReference,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    bool UncoveredAcknowledged);

public sealed record ReleaseAccountLienRequestV1(
    decimal Amount,
    string ReasonCode,
    string? AuthorityReference,
    string? ApprovalReference,
    long ExpectedOutstandingVersion);

public sealed record SatisfyAccountLienRequestV1(
    decimal Amount,
    string SettlementReference);

public sealed record LienAvailabilityPositionV1(
    decimal TotalOutstandingLiens,
    decimal ReportedAvailable,
    decimal UncoveredLienAmount,
    bool ExceedsAvailable,
    string SourceVersion,
    DateTimeOffset AsOf);

public sealed record LienResponseV1(
    string InstitutionId,
    string LienId,
    string AccountId,
    MonetaryAmountV1 Amount,
    decimal OutstandingAmount,
    string PriorityCode,
    int PriorityRank,
    string Status,
    LienAvailabilityPositionV1 Availability,
    DateTimeOffset ChangedAt,
    bool Replayed);

public sealed record EvaluateAccountControlsRequestV1(
    string PostingIntentId,
    string PostingDirectionCode,
    string TransactionTypeCode,
    string ChannelCode,
    MonetaryAmountV1 Amount,
    long KnownControlVersion,
    DateTimeOffset EvaluationTime);

public sealed record EffectiveAccountControlV1(
    string ControlId,
    string ControlTypeCode,
    string ScopeCode,
    string Status,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo);

public sealed record AccountControlDecisionV1(
    string InstitutionId,
    string AccountId,
    string PostingIntentId,
    string Outcome,
    IReadOnlyCollection<string> ReasonCodes,
    IReadOnlyCollection<EffectiveAccountControlV1> EffectiveControls,
    long ControlVersion,
    DateTimeOffset EvaluatedAt);

public sealed record AccountRestrictionChangedV1(
    string InstitutionId,
    string AccountId,
    string RestrictionId,
    string RestrictionTypeCode,
    string PostingDirectionCode,
    IReadOnlyCollection<string> ScopeCodes,
    string Status,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    long ControlVersion,
    DateTimeOffset ChangedAt);

public sealed record AccountLienChangedV1(
    string InstitutionId,
    string AccountId,
    string LienId,
    MonetaryAmountV1 Amount,
    string Status,
    string PriorityCode,
    long ControlVersion,
    DateTimeOffset ChangedAt);
