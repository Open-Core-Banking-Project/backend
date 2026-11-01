using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CustomerOnboarding.Domain.Kyc;

namespace CoreBanking.CustomerOnboarding.Application.Kyc;

/// <summary>
/// Opens the KYC case for the exact submitted revision. The requirement snapshot of that revision decides
/// which requirements exist. Decision: P1-E03-D06, invariant 3.
/// </summary>
public sealed record OpenKycCaseCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string ApplicationId,
    int ExpectedRevision,
    string CorrelationId);

/// <summary>Requests one canonical verification for a requirement. The check code is approved configuration data.</summary>
public sealed record RequestVerificationCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string CaseId,
    string RequirementCode,
    string CheckCode,
    string CorrelationId);

/// <summary>Records a new provider attempt for a logical verification.</summary>
public sealed record RecordVerificationAttemptCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string VerificationId,
    string AttemptId,
    string? ProviderReference,
    string? ProviderRouteId,
    string CorrelationId);

/// <summary>
/// Applies one attempt result. A succeeded attempt must carry the canonical outcome and the mapping version
/// that produced it. Decision: P1-E03-D06.
/// </summary>
public sealed record ApplyVerificationAttemptResultCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string VerificationId,
    string AttemptId,
    VerificationAttemptStatus AttemptStatus,
    CanonicalVerificationOutcome? Outcome,
    string? ProviderMappingVersion,
    string? EvidenceReference,
    string? EvidenceHash,
    string CorrelationId);

/// <summary>A reviewer resolves an inconclusive or manual-review requirement with a recorded reason.</summary>
public sealed record ResolveKycRequirementCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string CaseId,
    string RequirementCode,
    CanonicalVerificationOutcome Outcome,
    string Reason,
    string CorrelationId);

/// <summary>
/// Reopens a satisfied requirement after a protected-data correction, so the previously accepted evidence is
/// never treated as current. Decision: epic 03 CUS-UC-06.
/// </summary>
public sealed record ReopenKycRequirementCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string CaseId,
    string RequirementCode,
    string Reason,
    string CorrelationId);

/// <summary>Waives a requirement with an explicit authority and reason.</summary>
public sealed record WaiveKycRequirementCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string CaseId,
    string RequirementCode,
    string Reason,
    string CorrelationId);

public sealed record GetKycCaseQuery(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string CaseId);

public sealed record KycRequirementView(
    string Code,
    KycRequirementStatus Status,
    string StatusCode,
    string? LinkedVerificationId,
    string? WaiverAuthority,
    string? WaiverReason,
    DateTimeOffset UpdatedAt);

public sealed record KycCaseView(
    string CaseId,
    string ApplicationId,
    string PartyId,
    string RequirementSnapshotId,
    KycCaseStatus Status,
    string StatusCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<KycRequirementView> Requirements);

public sealed record VerificationAttemptView(
    string AttemptId,
    string? ProviderReference,
    string? ProviderRouteId,
    VerificationAttemptStatus Status,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? ResolvedAt);

public sealed record VerificationView(
    string VerificationId,
    string CaseId,
    string RequirementCode,
    string CheckCode,
    VerificationStatus Status,
    string StatusCode,
    CanonicalVerificationOutcome? Outcome,
    string? OutcomeCode,
    string? ProviderMappingVersion,
    string? EvidenceReference,
    string? EvidenceHash,
    IReadOnlyList<VerificationAttemptView> Attempts);

public interface IKycCaseService
{
    Task<KycCaseView> OpenAsync(OpenKycCaseCommand command, CancellationToken cancellationToken);

    Task<VerificationView> RequestVerificationAsync(RequestVerificationCommand command, CancellationToken cancellationToken);

    Task<VerificationView> RecordAttemptAsync(RecordVerificationAttemptCommand command, CancellationToken cancellationToken);

    Task<VerificationView> ApplyAttemptResultAsync(ApplyVerificationAttemptResultCommand command, CancellationToken cancellationToken);

    Task<KycCaseView> ResolveRequirementAsync(ResolveKycRequirementCommand command, CancellationToken cancellationToken);

    Task<KycCaseView> WaiveRequirementAsync(WaiveKycRequirementCommand command, CancellationToken cancellationToken);

    Task<KycCaseView> ReopenRequirementAsync(ReopenKycRequirementCommand command, CancellationToken cancellationToken);

    Task<KycCaseView> GetAsync(GetKycCaseQuery query, CancellationToken cancellationToken);
}

/// <summary>The case does not exist in the caller's tenant, or is not visible to the caller.</summary>
public sealed class KycCaseNotFoundException : Exception
{
    public KycCaseNotFoundException(string caseId)
        : base($"KYC case '{caseId}' was not found.") => CaseId = caseId;

    public string CaseId { get; }
}

/// <summary>The verification does not exist in the caller's tenant, or is not visible to the caller.</summary>
public sealed class KycVerificationNotFoundException : Exception
{
    public KycVerificationNotFoundException(string verificationId)
        : base($"KYC verification '{verificationId}' was not found.") => VerificationId = verificationId;

    public string VerificationId { get; }
}
