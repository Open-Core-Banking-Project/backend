using System;
using System.Collections.Generic;

namespace CoreBanking.CoreLedger.Contracts;

/// <summary>Request bodies for the institution account-opening policy lifecycle. Decision: P1-E04-D07.</summary>
public sealed record CreateAccountOpeningPolicyDraftRequestV1(
    string VersionId,
    string LimitKey,
    int MaximumOpenAccounts,
    int OverrideAdditionalAccounts);

public sealed record SimulateAccountOpeningPolicyRequestV1(
    string SimulationId,
    string ContentHash);

public sealed record SubmitAccountOpeningPolicyRequestV1(
    string RequestId,
    string SourceRequestKey,
    string ContentHash);

public sealed record PublishAccountOpeningPolicyRequestV1(
    string PublicationId,
    string ContentHash,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo);

public sealed record EligibilityRuleTraceV1(
    string Rule,
    string Input,
    bool Passed);

/// <summary>
/// Immutable eligibility evidence for one account application: the exact facts versions, product
/// content hash, counted holdings and the decision. Decisions: P1-E04-D07, P1-E04-D17.
/// </summary>
public sealed record AccountEligibilityEvaluationResponseV1(
    string InstitutionId,
    string ApplicationId,
    string EvaluationId,
    string ProductVersionId,
    string ProductContentHash,
    string CustomerFactsVersion,
    string BranchReferenceVersion,
    string CurrencyReferenceVersion,
    bool Eligible,
    bool FactsCurrent,
    string DecisionCode,
    string? LimitKey,
    int? CountedHoldings,
    int? EffectiveMaximum,
    IReadOnlyCollection<string> CountedHoldingIds,
    IReadOnlyCollection<EligibilityRuleTraceV1> Trace,
    string ApplicationStatus,
    long ApplicationVersion,
    DateTimeOffset EvaluatedAt,
    bool Replayed);
