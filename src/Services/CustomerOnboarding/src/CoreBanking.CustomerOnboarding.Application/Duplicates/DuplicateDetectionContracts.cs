using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CustomerOnboarding.Domain.Duplicates;

namespace CoreBanking.CustomerOnboarding.Application.Duplicates;

/// <summary>
/// Detects duplicate candidates for the exact application revision the reviewer is looking at. The
/// expected revision is required so a detection run can never silently attach to newer protected data.
/// Decision: P1-E03-D04-A, P1-E03-D04-D.
/// </summary>
public sealed record DetectDuplicateCandidatesCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string ApplicationId,
    int ExpectedRevision,
    string CorrelationId);

/// <summary>Records the reviewer outcome against the exact candidate set version that was read.</summary>
public sealed record DecideDuplicateCandidatesCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string SetId,
    long ExpectedVersion,
    DuplicateReviewDecision Decision,
    string Reason,
    string CorrelationId);

/// <summary>Reads one candidate set. Invisible outside the caller's institution.</summary>
public sealed record GetDuplicateCandidateSetQuery(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string SetId);

/// <summary>One explainable factor, with both the enum and its stable code for rendering.</summary>
public sealed record DuplicateMatchFactorView(DuplicateMatchFactorKind Kind, string KindCode, string Basis);

/// <summary>
/// A candidate as a reviewer sees it: references, status, factor codes and masked comparison values
/// only. Decision: P1-E03-D04 section 3.
/// </summary>
public sealed record DuplicateCandidateView(
    string CandidatePartyId,
    string CandidatePartyStatus,
    DuplicateMatchBand Band,
    string BandCode,
    IReadOnlyList<DuplicateMatchFactorView> Factors,
    IReadOnlyList<string> MaskedMatches);

/// <summary>A detection run and its decision. Superseded sets stay readable as evidence.</summary>
public sealed record DuplicateCandidateSetView(
    string SetId,
    string ApplicationId,
    int ApplicationRevision,
    string RuleVersion,
    DuplicateCandidateSetStatus Status,
    string StatusCode,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DecidedAt,
    string? DecidedBy,
    DuplicateReviewDecision? Decision,
    string? DecisionCode,
    string? DecisionReason,
    IReadOnlyList<DuplicateCandidateView> Candidates);

public interface IDuplicateDetectionService
{
    Task<DuplicateCandidateSetView> DetectAsync(
        DetectDuplicateCandidatesCommand command,
        CancellationToken cancellationToken);

    Task<DuplicateCandidateSetView> DecideAsync(
        DecideDuplicateCandidatesCommand command,
        CancellationToken cancellationToken);

    Task<DuplicateCandidateSetView> GetAsync(
        GetDuplicateCandidateSetQuery query,
        CancellationToken cancellationToken);
}

/// <summary>
/// The candidate set does not exist in the caller's tenant, or is not visible to the caller. A
/// foreign-institution set is reported the same way as a missing one, so the response cannot be used
/// to enumerate another institution's onboarding work.
/// </summary>
public sealed class DuplicateCandidateSetNotFoundException : Exception
{
    public DuplicateCandidateSetNotFoundException(string setId)
        : base($"Duplicate candidate set '{setId}' was not found.") => SetId = setId;

    public string SetId { get; }
}
