using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CustomerOnboarding.Application.Kyc;
using CoreBanking.CustomerOnboarding.Application.Onboarding;
using CoreBanking.CustomerOnboarding.Domain;
using CoreBanking.CustomerOnboarding.Domain.Kyc;
using CoreBanking.CustomerOnboarding.Infrastructure.Audit;
using CoreBanking.CustomerOnboarding.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.CustomerOnboarding.Infrastructure.Kyc;

/// <summary>
/// Runs one canonical verification through the provider port: a submission refused before acceptance is retried
/// as a new attempt, an accepted-but-late submission becomes uncertain and is resolved only by status enquiry,
/// and an accepted outcome needs its mapping version and evidence reference.
/// Decision: P1-E03-D07, P1-E03-D08.
/// </summary>
public sealed class KycVerificationRunner : IKycVerificationRunner
{
    public const string ActionAttemptSubmitted = "kyc.attempt.submitted";
    public const string ActionAttemptRetried = "kyc.attempt.retried";
    public const string ActionAttemptUncertain = "kyc.attempt.uncertain";
    public const string ActionAttemptEnquired = "kyc.attempt.enquired";

    private readonly CustomerOnboardingDbContext _context;
    private readonly IKycCaseService _kyc;
    private readonly IKycProviderPort _provider;
    private readonly TimeProvider _timeProvider;
    private readonly KycVerificationRunOptions _options;

    public KycVerificationRunner(
        CustomerOnboardingDbContext context,
        IKycCaseService kyc,
        IKycProviderPort provider,
        TimeProvider timeProvider,
        KycVerificationRunOptions options)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _kyc = kyc ?? throw new ArgumentNullException(nameof(kyc));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    public async Task<KycVerificationRunView> RunAsync(
        RunKycVerificationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        string verificationId = await EnsureVerificationAsync(command, cancellationToken).ConfigureAwait(false);
        string routeId = _provider.RouteFor(command.CheckCode);
        int attemptCount = await _context.VerificationAttempts
            .CountAsync(attempt => attempt.VerificationId == verificationId, cancellationToken)
            .ConfigureAwait(false);

        for (int submission = 1; submission <= _options.MaxSubmissionAttempts; submission++)
        {
            string attemptId = Guid.NewGuid().ToString("N");
            VerificationView submitted = await _kyc.RecordAttemptAsync(
                new RecordVerificationAttemptCommand(
                    command.InstitutionId,
                    command.ActorId,
                    verificationId,
                    attemptId,
                    ProviderReference: attemptId,
                    ProviderRouteId: routeId,
                    command.CorrelationId),
                cancellationToken).ConfigureAwait(false);
            attemptCount++;

            await AuditAsync(command, attemptId, routeId, submission == 1 ? ActionAttemptSubmitted : ActionAttemptRetried, cancellationToken)
                .ConfigureAwait(false);

            KycProviderReceipt receipt = await _provider
                .SubmitAsync(
                    new KycProviderSubmission(
                        command.InstitutionId.Value,
                        verificationId,
                        attemptId,
                        command.CheckCode,
                        command.RequirementCode),
                    cancellationToken)
                .ConfigureAwait(false);

            switch (receipt.Status)
            {
                case KycProviderReceiptStatus.Unavailable:
                    // Nothing was accepted, so the attempt is closed and the next one is a safe retry.
                    VerificationView closed = await _kyc.ApplyAttemptResultAsync(
                        new ApplyVerificationAttemptResultCommand(
                            command.InstitutionId,
                            command.ActorId,
                            verificationId,
                            attemptId,
                            VerificationAttemptStatus.CancelledBeforeSubmission,
                            Outcome: null,
                            ProviderMappingVersion: null,
                            EvidenceReference: null,
                            EvidenceHash: null,
                            command.CorrelationId),
                        cancellationToken).ConfigureAwait(false);

                    if (submission == _options.MaxSubmissionAttempts)
                    {
                        return new KycVerificationRunView(
                            verificationId,
                            closed.StatusCode,
                            attemptId,
                            routeId,
                            attemptCount,
                            RequiresStatusEnquiry: false,
                            Outcome: null,
                            OutcomeCode: null);
                    }

                    continue;

                case KycProviderReceiptStatus.TimedOut:
                    VerificationView uncertain = await _kyc.ApplyAttemptResultAsync(
                        new ApplyVerificationAttemptResultCommand(
                            command.InstitutionId,
                            command.ActorId,
                            verificationId,
                            attemptId,
                            VerificationAttemptStatus.Uncertain,
                            Outcome: null,
                            ProviderMappingVersion: null,
                            EvidenceReference: null,
                            EvidenceHash: null,
                            command.CorrelationId),
                        cancellationToken).ConfigureAwait(false);

                    await AuditAsync(command, attemptId, routeId, ActionAttemptUncertain, cancellationToken).ConfigureAwait(false);

                    return new KycVerificationRunView(
                        verificationId,
                        uncertain.StatusCode,
                        attemptId,
                        routeId,
                        attemptCount,
                        RequiresStatusEnquiry: true,
                        Outcome: null,
                        OutcomeCode: null);

                case KycProviderReceiptStatus.Accepted:
                default:
                    // The provider accepted the submission, so the canonical result arrives by status enquiry.
                    await AuditAsync(command, attemptId, routeId, ActionAttemptEnquired, cancellationToken).ConfigureAwait(false);
                    return await EnquireCoreAsync(
                        command.InstitutionId,
                        command.ActorId,
                        verificationId,
                        attemptId,
                        command.CheckCode,
                        routeId,
                        command.CorrelationId,
                        attemptCount,
                        cancellationToken).ConfigureAwait(false);
            }
        }

        throw new OnboardingRuleException(
            "kyc_submission_attempts_exhausted",
            "Every provider submission was refused before acceptance; the verification needs operator attention.");
    }

    public async Task<KycVerificationRunView> EnquireAsync(
        EnquireKycVerificationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        IdentityVerificationRecord record = await _context.IdentityVerifications
            .Include(verification => verification.Attempts)
            .SingleOrDefaultAsync(
                verification => verification.VerificationId == command.VerificationId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KycVerificationNotFoundException(command.VerificationId);

        VerificationAttemptRecord? attempt = record.Attempts
            .OrderByDescending(candidate => candidate.SubmittedAt)
            .FirstOrDefault();

        if (attempt is null)
        {
            throw Conflict("kyc_attempt_not_found", "This verification has no provider attempt to enquire about.");
        }

        string routeId = attempt.ProviderRouteId
            ?? _provider.RouteFor(record.CheckCode);

        return await EnquireCoreAsync(
            command.InstitutionId,
            command.ActorId,
            record.VerificationId,
            attempt.AttemptId,
            record.CheckCode,
            routeId,
            command.CorrelationId,
            record.Attempts.Count,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<KycVerificationRunView> EnquireCoreAsync(
        InstitutionId institutionId,
        ActorId actorId,
        string verificationId,
        string attemptId,
        string checkCode,
        string routeId,
        string correlationId,
        int attemptCount,
        CancellationToken cancellationToken)
    {
        for (int enquiry = 1; enquiry <= _options.MaxStatusEnquiries; enquiry++)
        {
            KycProviderResult result = await _provider
                .EnquireAsync(new KycProviderEnquiry(institutionId.Value, checkCode, routeId, attemptId), cancellationToken)
                .ConfigureAwait(false);

            if (result.Status == KycProviderEnquiryStatus.Pending)
            {
                continue;
            }

            if (result.Status == KycProviderEnquiryStatus.Unknown)
            {
                // The provider has no record of the submission: keep it uncertain and never resubmit blindly.
                IdentityVerificationRecord current = await _context.IdentityVerifications
                    .SingleAsync(verification => verification.VerificationId == verificationId, cancellationToken)
                    .ConfigureAwait(false);

                return new KycVerificationRunView(
                    verificationId,
                    current.Status,
                    attemptId,
                    routeId,
                    attemptCount,
                    RequiresStatusEnquiry: true,
                    Outcome: null,
                    OutcomeCode: null);
            }

            VerificationView resolved = await _kyc.ApplyAttemptResultAsync(
                new ApplyVerificationAttemptResultCommand(
                    institutionId,
                    actorId,
                    verificationId,
                    attemptId,
                    VerificationAttemptStatus.Succeeded,
                    result.Outcome,
                    result.ProviderMappingVersion,
                    result.EvidenceReference,
                    result.EvidenceHash,
                    correlationId),
                cancellationToken).ConfigureAwait(false);

            return new KycVerificationRunView(
                verificationId,
                resolved.StatusCode,
                attemptId,
                routeId,
                attemptCount,
                RequiresStatusEnquiry: false,
                resolved.Outcome,
                resolved.OutcomeCode);
        }

        IdentityVerificationRecord pending = await _context.IdentityVerifications
            .SingleAsync(verification => verification.VerificationId == verificationId, cancellationToken)
            .ConfigureAwait(false);

        return new KycVerificationRunView(
            verificationId,
            pending.Status,
            attemptId,
            routeId,
            attemptCount,
            RequiresStatusEnquiry: true,
            Outcome: null,
            OutcomeCode: null);
    }

    private async Task<string> EnsureVerificationAsync(
        RunKycVerificationCommand command,
        CancellationToken cancellationToken)
    {
        KycCaseView kycCase = await _kyc
            .GetAsync(new GetKycCaseQuery(command.InstitutionId, command.ActorId, command.CaseId), cancellationToken)
            .ConfigureAwait(false);

        KycRequirementView requirement = kycCase.Requirements
            .SingleOrDefault(candidate => string.Equals(candidate.Code, command.RequirementCode.Trim().ToLowerInvariant(), StringComparison.Ordinal))
            ?? throw Conflict("kyc_requirement_not_found", "The requirement is not part of this case.");

        if (string.Equals(requirement.StatusCode, "waived", StringComparison.Ordinal))
        {
            throw Conflict("kyc_requirement_waived", "A waived requirement needs no verification.");
        }

        if (!string.IsNullOrWhiteSpace(requirement.LinkedVerificationId))
        {
            return requirement.LinkedVerificationId;
        }

        VerificationView requested = await _kyc.RequestVerificationAsync(
            new RequestVerificationCommand(
                command.InstitutionId,
                command.ActorId,
                command.CaseId,
                command.RequirementCode,
                command.CheckCode,
                command.CorrelationId),
            cancellationToken).ConfigureAwait(false);

        return requested.VerificationId;
    }

    private async Task AuditAsync(
        RunKycVerificationCommand command,
        string attemptId,
        string routeId,
        string action,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        AuditRecorder.Record(
            _context,
            command.InstitutionId,
            command.ActorId.Value,
            action,
            AuditRecorder.SubjectKycCase,
            command.CaseId,
            command.CorrelationId,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["attemptId"] = attemptId,
                ["providerRouteId"] = routeId,
                ["checkCode"] = command.CheckCode.Trim().ToLowerInvariant(),
                ["requirement"] = command.RequirementCode.Trim().ToLowerInvariant(),
            },
            now);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static OnboardingApplicationConflictException Conflict(string code, string message) => new(code, message);
}
