using System;
using System.Collections.Generic;

namespace CoreBanking.CoreLedger.Contracts;

public sealed record EligibilityCodeCriterionV1(
    string Mode,
    IReadOnlyCollection<string> Values);

public sealed record EligibilityAgeCriterionV1(
    string Mode,
    int? MinimumYears,
    int? MaximumYears);

public sealed record SavingsEligibilityRulesV1(
    string RuleSetId,
    int Version,
    EligibilityCodeCriterionV1 CustomerTypes,
    EligibilityCodeCriterionV1 KycTiers,
    EligibilityCodeCriterionV1 Segments,
    EligibilityCodeCriterionV1 RiskRatings,
    EligibilityAgeCriterionV1 Ages,
    EligibilityCodeCriterionV1 Branches,
    EligibilityCodeCriterionV1 ResidencyCountries,
    EligibilityCodeCriterionV1 Currencies);

public sealed record CreateSavingsEligibilityDraftRequestV1(
    string ProductCode,
    string VersionId,
    int VersionNumber,
    SavingsEligibilityRulesV1 Rules);

public sealed record SavingsEligibilityDraftResponseV1(
    string InstitutionId,
    string ProductId,
    string ProductCode,
    string VersionId,
    int VersionNumber,
    string ContentHash,
    string Status,
    DateTimeOffset ChangedAt,
    bool Replayed);

public sealed record ProductEligibilitySimulationScenarioV1(
    string ScenarioId,
    string DatasetVersion,
    string CustomerFactsVersion,
    string BranchReferenceVersion,
    string CurrencyReferenceVersion,
    string CustomerType,
    string KycTier,
    string Segment,
    string RiskRating,
    int AgeYears,
    string BranchCode,
    string ResidencyCountryCode,
    string CurrencyCode,
    bool ExpectedEligible);

public sealed record SimulateSavingsEligibilityRequestV1(
    string ContentHash,
    string SimulationId,
    IReadOnlyCollection<ProductEligibilitySimulationScenarioV1> Scenarios);

public sealed record SavingsEligibilitySimulationResponseV1(
    string InstitutionId,
    string VersionId,
    string SimulationId,
    string ContentHash,
    string InputHash,
    string OutputHash,
    string Outcome,
    DateTimeOffset SimulatedAt,
    bool Replayed);

public sealed record SubmitSavingsEligibilityRequestV1(
    string RequestId,
    string SourceRequestKey,
    string ContentHash);

public sealed record SavingsEligibilityApprovalSubmissionResponseV1(
    string InstitutionId,
    string RequestId,
    string VersionId,
    string ContentHash,
    string EventId,
    string Status,
    DateTimeOffset SubmittedAt,
    bool Replayed);
