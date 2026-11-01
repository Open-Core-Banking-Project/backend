using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CustomerOnboarding.Domain.Onboarding;
using CoreBanking.CustomerOnboarding.Domain.Parties;

namespace CoreBanking.CustomerOnboarding.Application.Onboarding;

/// <summary>
/// Starts an individual onboarding application and records the first revision with the captured
/// fields. The reference is institution-unique and is what staff quote to the applicant.
/// </summary>
public sealed record StartIndividualApplicationCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string ApplicationReference,
    string? BranchId,
    OnboardingCapture Capture,
    string CorrelationId);

/// <summary>
/// Replaces the captured fields of a draft. <paramref name="ExpectedVersion"/> is the concurrency
/// version the caller last saw, so a stale screen never overwrites a newer capture.
/// </summary>
public sealed record UpdateIndividualApplicationCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string ApplicationId,
    long ExpectedVersion,
    OnboardingCapture Capture,
    string CorrelationId);

/// <summary>Submits the exact revision the officer confirmed and creates the party from it.</summary>
public sealed record SubmitOnboardingApplicationCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string ApplicationId,
    int ExpectedRevision,
    string CorrelationId);

/// <summary>Withdraws a draft or submitted application. A submitted party is left untouched.</summary>
public sealed record WithdrawOnboardingApplicationCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string ApplicationId,
    long ExpectedVersion,
    string CorrelationId);

/// <summary>Reads one application. The response carries masked values only.</summary>
public sealed record GetOnboardingApplicationQuery(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string ApplicationId);

/// <summary>The stable outcome of a capture operation; a replay returns the recorded outcome.</summary>
public sealed record OnboardingApplicationView(
    string ApplicationId,
    string ApplicationReference,
    string ApplicationType,
    string? BranchId,
    string Status,
    int Revision,
    long ConcurrencyVersion,
    string? PartyId,
    IReadOnlyList<string> MissingFields,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? SubmittedAt,
    bool Replayed);

public interface IOnboardingApplicationService
{
    Task<OnboardingApplicationView> StartAsync(
        StartIndividualApplicationCommand command,
        CancellationToken cancellationToken);

    Task<OnboardingApplicationView> UpdateAsync(
        UpdateIndividualApplicationCommand command,
        CancellationToken cancellationToken);

    Task<OnboardingApplicationView> SubmitAsync(
        SubmitOnboardingApplicationCommand command,
        CancellationToken cancellationToken);

    Task<OnboardingApplicationView> WithdrawAsync(
        WithdrawOnboardingApplicationCommand command,
        CancellationToken cancellationToken);

    Task<OnboardingApplicationView> GetAsync(
        GetOnboardingApplicationQuery query,
        CancellationToken cancellationToken);
}

/// <summary>The application does not exist in the caller's tenant, or is not visible to the caller.</summary>
public sealed class OnboardingApplicationNotFoundException : Exception
{
    public OnboardingApplicationNotFoundException(string applicationId)
        : base($"Onboarding application '{applicationId}' was not found.") => ApplicationId = applicationId;

    public string ApplicationId { get; }
}

/// <summary>
/// A capture request lost a concurrency or revision race. The code is stable and is reported to the
/// caller as the problem detail code. Decision: CUS-UC-01 failure codes.
/// </summary>
public sealed class OnboardingApplicationConflictException : Exception
{
    public OnboardingApplicationConflictException(string code, string message)
        : base(message) => Code = code;

    public string Code { get; }
}
