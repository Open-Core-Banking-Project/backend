using System;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CustomerOnboarding.Domain.Kyc;

namespace CoreBanking.CustomerOnboarding.Application.Kyc;

/// <summary>
/// One submission to an approved provider route. The route is configuration data; the port never chooses a
/// check code, a mapping or an outcome. Decision: P1-E03-D07.
/// </summary>
public sealed record KycProviderSubmission(
    string InstitutionId,
    string VerificationId,
    string AttemptId,
    string CheckCode,
    string? PartyReference);

/// <summary>
/// The provider's immediate answer. <c>Unavailable</c> means the submission was not accepted and may be
/// retried; <c>TimedOut</c> means the outcome is unknown and requires a status enquiry.
/// </summary>
public enum KycProviderReceiptStatus
{
    Accepted,
    Unavailable,
    TimedOut,
}

public sealed record KycProviderReceipt(
    KycProviderReceiptStatus Status,
    string ProviderRouteId,
    string? ProviderReference);

/// <summary>
/// A status enquiry for one accepted submission. Integration Hub correlates on the attempt identifier that the
/// submission carried, so an enquiry never needs the provider's own reference.
/// </summary>
public sealed record KycProviderEnquiry(
    string InstitutionId,
    string CheckCode,
    string ProviderRouteId,
    string AttemptId);

public enum KycProviderEnquiryStatus
{
    Pending,
    Completed,
    Unknown,
}

/// <summary>
/// The canonical answer from an enquiry. <c>Completed</c> carries the canonical outcome, the mapping version
/// that produced it and the evidence reference with its hash. Decision: P1-E03-D07, P1-E03-D08.
/// </summary>
public sealed record KycProviderResult(
    KycProviderEnquiryStatus Status,
    CanonicalVerificationOutcome? Outcome,
    string? ProviderMappingVersion,
    string? EvidenceReference,
    string? EvidenceHash);

/// <summary>
/// The Integration Hub boundary for KYC checks. Provider SDKs and transports stay behind this port.
/// Decision: P1-E03-D07.
/// </summary>
public interface IKycProviderPort
{
    /// <summary>The approved route for a check code. Routing is configuration data, not code policy.</summary>
    string RouteFor(string checkCode);

    Task<KycProviderReceipt> SubmitAsync(KycProviderSubmission submission, CancellationToken cancellationToken);

    Task<KycProviderResult> EnquireAsync(KycProviderEnquiry enquiry, CancellationToken cancellationToken);
}

/// <summary>Runs one canonical verification through the provider port, applying the approved retry rules.</summary>
public sealed record RunKycVerificationCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string CaseId,
    string RequirementCode,
    string CheckCode,
    string CorrelationId);

/// <summary>Resolves an uncertain verification by status enquiry. Decision: P1-E03-D07.</summary>
public sealed record EnquireKycVerificationCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string CaseId,
    string VerificationId,
    string CorrelationId);

/// <summary>
/// The result of one run. <c>RequiresStatusEnquiry</c> is true when the provider did or may not have accepted
/// the submission, so the caller must enquire instead of resubmitting.
/// </summary>
public sealed record KycVerificationRunView(
    string VerificationId,
    string StatusCode,
    string AttemptId,
    string ProviderRouteId,
    int AttemptCount,
    bool RequiresStatusEnquiry,
    CanonicalVerificationOutcome? Outcome,
    string? OutcomeCode);

/// <summary>Drives one verification through submission, bounded retry and status enquiry.</summary>
public interface IKycVerificationRunner
{
    Task<KycVerificationRunView> RunAsync(RunKycVerificationCommand command, CancellationToken cancellationToken);

    Task<KycVerificationRunView> EnquireAsync(EnquireKycVerificationCommand command, CancellationToken cancellationToken);
}

/// <summary>Limits for one verification run. Values are Operations tuning, not provider policy.</summary>
public sealed record KycVerificationRunOptions
{
    public int MaxSubmissionAttempts { get; init; } = 2;

    public int MaxStatusEnquiries { get; init; } = 5;

    public static KycVerificationRunOptions Default { get; } = new();

    public void Validate()
    {
        if (MaxSubmissionAttempts < 1 || MaxStatusEnquiries < 1)
        {
            throw new InvalidOperationException(
                "KYC verification attempt and enquiry limits must be positive.");
        }
    }
}
