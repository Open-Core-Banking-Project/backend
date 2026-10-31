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
using CoreBanking.CustomerOnboarding.Infrastructure.Requirements;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Npgsql;

namespace CoreBanking.CustomerOnboarding.Infrastructure.Kyc;

/// <summary>
/// KYC cases, their requirements and canonical verifications. The case is opened from the submitted
/// revision's requirement snapshot, provider results enter only as canonical outcomes with a mapping
/// version, and every state change is appended to history. Decision: P1-E03-D06.
/// </summary>
public sealed class KycCaseService : IKycCaseService
{
    private const int MinimumReasonLength = 3;
    private const int MaximumReasonLength = 500;

    private readonly CustomerOnboardingDbContext _context;
    private readonly TimeProvider _timeProvider;

    public KycCaseService(CustomerOnboardingDbContext context, TimeProvider timeProvider)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<KycCaseView> OpenAsync(OpenKycCaseCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        OnboardingApplicationRecord application = await _context.OnboardingApplications
            .AsNoTracking()
            .SingleOrDefaultAsync(record => record.ApplicationId == command.ApplicationId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new OnboardingApplicationNotFoundException(command.ApplicationId);

        if (application.Revision != command.ExpectedRevision)
        {
            throw Conflict("onboarding_revision_conflict", "The application changed since the revision the case was requested for.");
        }

        if (string.IsNullOrWhiteSpace(application.PartyId))
        {
            throw Conflict("kyc_application_not_submitted", "A KYC case is opened only for a submitted application with a recorded party.");
        }

        RequirementSnapshotRecord? snapshot = await _context.RequirementSnapshots
            .AsNoTracking()
            .SingleOrDefaultAsync(
                record => record.ApplicationId == application.ApplicationId && record.ApplicationRevision == application.Revision,
                cancellationToken)
            .ConfigureAwait(false);

        if (snapshot is null)
        {
            throw Conflict("kyc_requirement_snapshot_missing", "No requirement snapshot exists for this application revision.");
        }

        List<string> requiredCodes =
        [
            .. RequirementPayload.Deserialise(snapshot.Requirements)
                .Where(requirement => requirement.Mandatory)
                .Select(requirement => requirement.Code),
        ];

        DateTimeOffset now = _timeProvider.GetUtcNow();
        KycCase kycCase = KycCase.Open(
            command.InstitutionId,
            application.ApplicationId,
            application.PartyId,
            snapshot.RequirementSnapshotId,
            requiredCodes,
            now);

        KycCaseRecord record = ToRecord(kycCase);
        _context.KycCases.Add(record);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            DiscardPendingChanges();
            throw Conflict("kyc_case_exists", "A KYC case already exists for this application.");
        }

        foreach (KycRequirement requirement in kycCase.Requirements)
        {
            await AppendRequirementHistoryAsync(
                command.InstitutionId.Value,
                kycCase.CaseId,
                requirement.Code,
                requirement.Status,
                reason: null,
                command.ActorId,
                now,
                cancellationToken).ConfigureAwait(false);
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToView(kycCase);
    }

    public async Task<VerificationView> RequestVerificationAsync(
        RequestVerificationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        KycCaseRecord caseRecord = await LoadCaseRecordAsync(command.CaseId, cancellationToken).ConfigureAwait(false);
        KycCase kycCase = ToModel(caseRecord);
        DateTimeOffset now = _timeProvider.GetUtcNow();

        IdentityVerification verification = IdentityVerification.Request(
            command.InstitutionId,
            kycCase.ApplicationId,
            kycCase.PartyId,
            command.RequirementCode,
            command.CheckCode,
            kycCase.RequirementSnapshotId,
            now);

        try
        {
            kycCase.StartVerification(command.RequirementCode, verification.VerificationId, now);
        }
        catch (OnboardingRuleException exception)
        {
            throw Conflict(exception.Code, exception.Message);
        }

        IdentityVerificationRecord verificationRecord = ToRecord(verification, kycCase.CaseId);
        _context.IdentityVerifications.Add(verificationRecord);
        Apply(kycCase, caseRecord);
        await AppendVerificationHistoryAsync(verificationRecord, command.ActorId, now, cancellationToken).ConfigureAwait(false);
        await AppendRequirementHistoryAsync(
            command.InstitutionId.Value,
            kycCase.CaseId,
            command.RequirementCode,
            kycCase.Require(command.RequirementCode).Status,
            reason: null,
            command.ActorId,
            now,
            cancellationToken).ConfigureAwait(false);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            DiscardPendingChanges();
            throw Conflict("kyc_verification_active", "This requirement already has an active verification.");
        }

        return ToView(verification, kycCase.CaseId);
    }

    public async Task<VerificationView> RecordAttemptAsync(
        RecordVerificationAttemptCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        IdentityVerificationRecord record = await LoadVerificationRecordAsync(command.VerificationId, cancellationToken)
            .ConfigureAwait(false);
        IdentityVerification verification = ToModel(record);
        DateTimeOffset now = _timeProvider.GetUtcNow();

        try
        {
            verification.RecordAttempt(command.AttemptId, command.ProviderReference, command.ProviderRouteId, now);
        }
        catch (OnboardingRuleException exception)
        {
            throw Conflict(exception.Code, exception.Message);
        }

        _context.VerificationAttempts.Add(new VerificationAttemptRecord
        {
            InstitutionId = record.InstitutionId,
            VerificationId = record.VerificationId,
            AttemptId = command.AttemptId,
            ProviderReference = command.ProviderReference,
            ProviderRouteId = command.ProviderRouteId,
            Status = AttemptStatusText(verification.Attempts[^1].Status),
            SubmittedAt = now,
        });

        Apply(verification, record, now);
        await AppendVerificationHistoryAsync(record, command.ActorId, now, cancellationToken).ConfigureAwait(false);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToView(verification, record.CaseId);
    }

    public async Task<VerificationView> ApplyAttemptResultAsync(
        ApplyVerificationAttemptResultCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        IdentityVerificationRecord record = await LoadVerificationRecordAsync(command.VerificationId, cancellationToken)
            .ConfigureAwait(false);
        IdentityVerification verification = ToModel(record);
        DateTimeOffset now = _timeProvider.GetUtcNow();

        try
        {
            verification.ApplyAttemptResult(
                command.AttemptId,
                command.AttemptStatus,
                command.Outcome,
                command.ProviderMappingVersion,
                string.IsNullOrWhiteSpace(command.EvidenceReference) && string.IsNullOrWhiteSpace(command.EvidenceHash)
                    ? null
                    : new VerificationEvidence(command.EvidenceReference ?? string.Empty, command.EvidenceHash ?? string.Empty),
                now);
        }
        catch (OnboardingRuleException exception)
        {
            throw Conflict(exception.Code, exception.Message);
        }

        VerificationAttemptRecord attemptRecord = await _context.VerificationAttempts
            .SingleAsync(
                attempt => attempt.VerificationId == record.VerificationId && attempt.AttemptId == command.AttemptId,
                cancellationToken)
            .ConfigureAwait(false);

        VerificationAttempt? attempt = verification.Attempts.SingleOrDefault(candidate =>
            string.Equals(candidate.AttemptId, command.AttemptId, StringComparison.Ordinal));
        attemptRecord.Status = AttemptStatusText(attempt?.Status ?? command.AttemptStatus);
        attemptRecord.ResolvedAt = attempt?.ResolvedAt;

        Apply(verification, record, now);
        await AppendVerificationHistoryAsync(record, command.ActorId, now, cancellationToken).ConfigureAwait(false);

        CanonicalVerificationOutcome? requirementOutcome = verification.Status switch
        {
            VerificationStatus.Succeeded => verification.Outcome,
            VerificationStatus.Failed => CanonicalVerificationOutcome.Failed,
            _ => null,
        };

        if (requirementOutcome is not null)
        {
            KycCaseRecord caseRecord = await LoadCaseRecordAsync(record.CaseId, cancellationToken).ConfigureAwait(false);
            KycCase kycCase = ToModel(caseRecord);

            try
            {
                kycCase.ApplyOutcome(verification.RequirementCode, requirementOutcome.Value, now);
            }
            catch (OnboardingRuleException exception)
            {
                throw Conflict(exception.Code, exception.Message);
            }

            Apply(kycCase, caseRecord);
            await AppendRequirementHistoryAsync(
                record.InstitutionId,
                kycCase.CaseId,
                verification.RequirementCode,
                kycCase.Require(verification.RequirementCode).Status,
                reason: null,
                command.ActorId,
                now,
                cancellationToken).ConfigureAwait(false);
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToView(verification, record.CaseId);
    }

    public async Task<KycCaseView> ResolveRequirementAsync(
        ResolveKycRequirementCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        KycCaseRecord record = await LoadCaseRecordAsync(command.CaseId, cancellationToken).ConfigureAwait(false);
        KycCase kycCase = ToModel(record);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        string reason = ValidateReason(command.Reason, "kyc_review_reason_required");

        if (command.Outcome is not (CanonicalVerificationOutcome.Passed or CanonicalVerificationOutcome.Failed))
        {
            throw Conflict("kyc_outcome_invalid", "A reviewer resolves a requirement as passed or failed.");
        }

        try
        {
            kycCase.ApplyOutcome(command.RequirementCode, command.Outcome, now);
        }
        catch (OnboardingRuleException exception)
        {
            throw Conflict(exception.Code, exception.Message);
        }

        Apply(kycCase, record);
        await AppendRequirementHistoryAsync(
            record.InstitutionId,
            kycCase.CaseId,
            command.RequirementCode,
            kycCase.Require(command.RequirementCode).Status,
            reason,
            command.ActorId,
            now,
            cancellationToken).ConfigureAwait(false);

        AuditRecorder.Record(
            _context,
            command.InstitutionId,
            command.ActorId.Value,
            AuditRecorder.ActionKycRequirementResolved,
            AuditRecorder.SubjectKycCase,
            kycCase.CaseId,
            command.CorrelationId,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["requirementCode"] = command.RequirementCode,
                ["outcome"] = KycCodes.ToCode(command.Outcome),
            },
            now);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToView(kycCase);
    }

    public async Task<KycCaseView> ReopenRequirementAsync(
        ReopenKycRequirementCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        KycCaseRecord record = await LoadCaseRecordAsync(command.CaseId, cancellationToken).ConfigureAwait(false);
        KycCase kycCase = ToModel(record);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        string reason = (command.Reason ?? string.Empty).Trim();
        if (reason.Length < 3)
        {
            throw Conflict("kyc_requirement_reason_required", "Reopening a requirement requires a recorded reason.");
        }

        try
        {
            kycCase.ReopenForEvidence(command.RequirementCode, now);
        }
        catch (OnboardingRuleException exception)
        {
            throw Conflict(exception.Code, exception.Message);
        }

        Apply(kycCase, record);
        await AppendRequirementHistoryAsync(
            record.InstitutionId,
            kycCase.CaseId,
            command.RequirementCode,
            kycCase.Require(command.RequirementCode).Status,
            reason,
            command.ActorId,
            now,
            cancellationToken).ConfigureAwait(false);

        AuditRecorder.Record(
            _context,
            command.InstitutionId,
            command.ActorId.Value,
            AuditRecorder.ActionKycRequirementReopened,
            AuditRecorder.SubjectKycCase,
            kycCase.CaseId,
            command.CorrelationId,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["requirementCode"] = command.RequirementCode.Trim().ToLowerInvariant(),
                ["reason"] = reason,
            },
            now);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToView(kycCase);
    }

    public async Task<KycCaseView> WaiveRequirementAsync(
        WaiveKycRequirementCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        KycCaseRecord record = await LoadCaseRecordAsync(command.CaseId, cancellationToken).ConfigureAwait(false);
        KycCase kycCase = ToModel(record);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        string reason = ValidateReason(command.Reason, "kyc_waiver_reason_required");

        try
        {
            kycCase.Waive(command.RequirementCode, command.ActorId.Value, reason, now);
        }
        catch (OnboardingRuleException exception)
        {
            throw Conflict(exception.Code, exception.Message);
        }

        Apply(kycCase, record);
        await AppendRequirementHistoryAsync(
            record.InstitutionId,
            kycCase.CaseId,
            command.RequirementCode,
            kycCase.Require(command.RequirementCode).Status,
            reason,
            command.ActorId,
            now,
            cancellationToken).ConfigureAwait(false);

        AuditRecorder.Record(
            _context,
            command.InstitutionId,
            command.ActorId.Value,
            AuditRecorder.ActionKycRequirementWaived,
            AuditRecorder.SubjectKycCase,
            kycCase.CaseId,
            command.CorrelationId,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["requirementCode"] = command.RequirementCode,
                ["approvalAuthority"] = command.ActorId.Value,
            },
            now);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToView(kycCase);
    }

    public async Task<KycCaseView> GetAsync(GetKycCaseQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        KycCaseRecord record = await _context.KycCases
            .AsNoTracking()
            .Include(kycCase => kycCase.Requirements)
            .SingleOrDefaultAsync(kycCase => kycCase.CaseId == query.CaseId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KycCaseNotFoundException(query.CaseId);

        return ToView(ToModel(record));
    }

    private async Task<KycCaseRecord> LoadCaseRecordAsync(string caseId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caseId);

        KycCaseRecord? record = await _context.KycCases
            .Include(kycCase => kycCase.Requirements)
            .SingleOrDefaultAsync(kycCase => kycCase.CaseId == caseId, cancellationToken)
            .ConfigureAwait(false);

        return record ?? throw new KycCaseNotFoundException(caseId);
    }

    private async Task<IdentityVerificationRecord> LoadVerificationRecordAsync(
        string verificationId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(verificationId);

        IdentityVerificationRecord? record = await _context.IdentityVerifications
            .Include(verification => verification.Attempts)
            .SingleOrDefaultAsync(verification => verification.VerificationId == verificationId, cancellationToken)
            .ConfigureAwait(false);

        return record ?? throw new KycVerificationNotFoundException(verificationId);
    }

    private async Task AppendVerificationHistoryAsync(
        IdentityVerificationRecord record,
        ActorId actorId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        int sequence = (await _context.VerificationStatusHistory
            .Where(history => history.VerificationId == record.VerificationId)
            .MaxAsync(history => (int?)history.Sequence, cancellationToken)
            .ConfigureAwait(false) ?? 0) + 1;

        _context.VerificationStatusHistory.Add(new VerificationStatusHistoryRecord
        {
            InstitutionId = record.InstitutionId,
            VerificationId = record.VerificationId,
            Sequence = sequence,
            Status = record.Status,
            Outcome = record.Outcome,
            RecordedBy = actorId.Value,
            RecordedAt = now,
        });
    }

    private async Task AppendRequirementHistoryAsync(
        string institutionId,
        string caseId,
        string requirementCode,
        KycRequirementStatus status,
        string? reason,
        ActorId actorId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        int sequence = (await _context.KycRequirementHistory
            .Where(history => history.CaseId == caseId && history.RequirementCode == requirementCode)
            .MaxAsync(history => (int?)history.Sequence, cancellationToken)
            .ConfigureAwait(false) ?? 0) + 1;

        _context.KycRequirementHistory.Add(new KycRequirementHistoryRecord
        {
            InstitutionId = institutionId,
            CaseId = caseId,
            RequirementCode = requirementCode,
            Sequence = sequence,
            Status = KycCodes.ToCode(status),
            Reason = reason,
            RecordedBy = actorId.Value,
            RecordedAt = now,
        });
    }

    private static string ValidateReason(string? reason, string code)
    {
        string trimmed = (reason ?? string.Empty).Trim();
        return trimmed.Length >= MinimumReasonLength && trimmed.Length <= MaximumReasonLength
            ? trimmed
            : throw new OnboardingApplicationConflictException(code, "A recorded reason is required.");
    }

    private static KycCaseRecord ToRecord(KycCase kycCase) => new()
    {
        InstitutionId = kycCase.InstitutionId.Value,
        CaseId = kycCase.CaseId,
        ApplicationId = kycCase.ApplicationId,
        PartyId = kycCase.PartyId,
        RequirementSnapshotId = kycCase.RequirementSnapshotId,
        Status = KycCodes.ToCode(kycCase.Status),
        CreatedAt = kycCase.CreatedAt,
        UpdatedAt = kycCase.UpdatedAt,
        CompletedAt = kycCase.CompletedAt,
        Requirements =
        [
            .. kycCase.Requirements.Select(requirement => new KycRequirementRecord
            {
                InstitutionId = kycCase.InstitutionId.Value,
                CaseId = kycCase.CaseId,
                RequirementCode = requirement.Code,
                Status = KycCodes.ToCode(requirement.Status),
                LinkedVerificationId = requirement.LinkedVerificationId,
                WaiverAuthority = requirement.WaiverAuthority,
                WaiverReason = requirement.WaiverReason,
                WaivedAt = requirement.WaivedAt,
                UpdatedAt = requirement.UpdatedAt,
            }),
        ],
    };

    private static KycCase ToModel(KycCaseRecord record) =>
        KycCase.Restore(
            InstitutionId.From(record.InstitutionId),
            record.CaseId,
            record.ApplicationId,
            record.PartyId,
            record.RequirementSnapshotId,
            KycCodes.FromCaseStatusCode(record.Status),
            [
                .. record.Requirements.Select(requirement => KycRequirement.Restore(
                    requirement.RequirementCode,
                    KycCodes.FromRequirementStatusCode(requirement.Status),
                    requirement.LinkedVerificationId,
                    requirement.WaiverAuthority,
                    requirement.WaiverReason,
                    requirement.WaivedAt,
                    requirement.UpdatedAt)),
            ],
            record.CreatedAt,
            record.UpdatedAt,
            record.CompletedAt);

    private static void Apply(KycCase kycCase, KycCaseRecord record)
    {
        record.Status = KycCodes.ToCode(kycCase.Status);
        record.UpdatedAt = kycCase.UpdatedAt;
        record.CompletedAt = kycCase.CompletedAt;

        foreach (KycRequirement requirement in kycCase.Requirements)
        {
            KycRequirementRecord row = record.Requirements.Single(candidate =>
                string.Equals(candidate.RequirementCode, requirement.Code, StringComparison.Ordinal));

            row.Status = KycCodes.ToCode(requirement.Status);
            row.LinkedVerificationId = requirement.LinkedVerificationId;
            row.WaiverAuthority = requirement.WaiverAuthority;
            row.WaiverReason = requirement.WaiverReason;
            row.WaivedAt = requirement.WaivedAt;
            row.UpdatedAt = requirement.UpdatedAt;
        }
    }

    private static IdentityVerificationRecord ToRecord(IdentityVerification verification, string caseId) => new()
    {
        InstitutionId = verification.InstitutionId.Value,
        VerificationId = verification.VerificationId,
        CaseId = caseId,
        ApplicationId = verification.ApplicationId,
        PartyId = verification.PartyId,
        RequirementCode = verification.RequirementCode,
        CheckCode = verification.CheckCode,
        RequirementSnapshotId = verification.RequirementSnapshotId,
        Status = StatusText(verification.Status),
        Outcome = verification.Outcome is null ? null : KycCodes.ToCode(verification.Outcome.Value),
        ProviderMappingVersion = verification.ProviderMappingVersion,
        EvidenceReference = verification.EvidenceReference,
        EvidenceHash = verification.EvidenceHash,
        CreatedAt = verification.CreatedAt,
        UpdatedAt = verification.UpdatedAt,
    };

    private static IdentityVerification ToModel(IdentityVerificationRecord record) =>
        IdentityVerification.Restore(
            InstitutionId.From(record.InstitutionId),
            record.VerificationId,
            record.ApplicationId,
            record.PartyId,
            record.RequirementCode,
            record.CheckCode,
            record.RequirementSnapshotId,
            ParseStatus(record.Status),
            record.Outcome is null ? null : KycCodes.FromOutcomeCode(record.Outcome),
            record.ProviderMappingVersion,
            record.EvidenceReference,
            record.EvidenceHash,
            [
                .. record.Attempts.Select(attempt => VerificationAttempt.Restore(
                    attempt.AttemptId,
                    attempt.ProviderReference,
                    attempt.ProviderRouteId,
                    ParseAttemptStatus(attempt.Status),
                    attempt.SubmittedAt,
                    attempt.ResolvedAt)),
            ],
            record.CreatedAt,
            record.UpdatedAt);

    private static void Apply(IdentityVerification verification, IdentityVerificationRecord record, DateTimeOffset now)
    {
        record.Status = StatusText(verification.Status);
        record.Outcome = verification.Outcome is null ? null : KycCodes.ToCode(verification.Outcome.Value);
        record.ProviderMappingVersion = verification.ProviderMappingVersion;
        record.EvidenceReference = verification.EvidenceReference;
        record.EvidenceHash = verification.EvidenceHash;
        record.UpdatedAt = now;
    }

    private static string StatusText(VerificationStatus status) => status switch
    {
        VerificationStatus.Created => "created",
        VerificationStatus.Submitted => "submitted",
        VerificationStatus.Pending => "pending",
        VerificationStatus.Succeeded => "succeeded",
        VerificationStatus.Failed => "failed",
        VerificationStatus.Uncertain => "uncertain",
        VerificationStatus.ManualReview => "manual_review",
        VerificationStatus.Cancelled => "cancelled",
        _ => throw new OnboardingRuleException("kyc_verification_status_invalid", $"Verification status '{status}' has no code."),
    };

    private static VerificationStatus ParseStatus(string text) => text switch
    {
        "created" => VerificationStatus.Created,
        "submitted" => VerificationStatus.Submitted,
        "pending" => VerificationStatus.Pending,
        "succeeded" => VerificationStatus.Succeeded,
        "failed" => VerificationStatus.Failed,
        "uncertain" => VerificationStatus.Uncertain,
        "manual_review" => VerificationStatus.ManualReview,
        "cancelled" => VerificationStatus.Cancelled,
        _ => throw new OnboardingRuleException("kyc_verification_status_invalid", $"Verification status code '{text}' is not known."),
    };

    private static string AttemptStatusText(VerificationAttemptStatus status) => status switch
    {
        VerificationAttemptStatus.Submitted => "submitted",
        VerificationAttemptStatus.Pending => "pending",
        VerificationAttemptStatus.Succeeded => "succeeded",
        VerificationAttemptStatus.Failed => "failed",
        VerificationAttemptStatus.Uncertain => "uncertain",
        VerificationAttemptStatus.CancelledBeforeSubmission => "cancelled_before_submission",
        _ => throw new OnboardingRuleException("kyc_attempt_status_invalid", $"Attempt status '{status}' has no code."),
    };

    private static VerificationAttemptStatus ParseAttemptStatus(string text) => text switch
    {
        "submitted" => VerificationAttemptStatus.Submitted,
        "pending" => VerificationAttemptStatus.Pending,
        "succeeded" => VerificationAttemptStatus.Succeeded,
        "failed" => VerificationAttemptStatus.Failed,
        "uncertain" => VerificationAttemptStatus.Uncertain,
        "cancelled_before_submission" => VerificationAttemptStatus.CancelledBeforeSubmission,
        _ => throw new OnboardingRuleException("kyc_attempt_status_invalid", $"Attempt status code '{text}' is not known."),
    };

    private static KycCaseView ToView(KycCase kycCase) =>
        new(
            kycCase.CaseId,
            kycCase.ApplicationId,
            kycCase.PartyId,
            kycCase.RequirementSnapshotId,
            kycCase.Status,
            KycCodes.ToCode(kycCase.Status),
            kycCase.CreatedAt,
            kycCase.UpdatedAt,
            kycCase.CompletedAt,
            [
                .. kycCase.Requirements.Select(requirement => new KycRequirementView(
                    requirement.Code,
                    requirement.Status,
                    KycCodes.ToCode(requirement.Status),
                    requirement.LinkedVerificationId,
                    requirement.WaiverAuthority,
                    requirement.WaiverReason,
                    requirement.UpdatedAt)),
            ]);

    private static VerificationView ToView(IdentityVerification verification, string caseId) =>
        new(
            verification.VerificationId,
            caseId,
            verification.RequirementCode,
            verification.CheckCode,
            verification.Status,
            StatusText(verification.Status),
            verification.Outcome,
            verification.Outcome is null ? null : KycCodes.ToCode(verification.Outcome.Value),
            verification.ProviderMappingVersion,
            verification.EvidenceReference,
            verification.EvidenceHash,
            [
                .. verification.Attempts.Select(attempt => new VerificationAttemptView(
                    attempt.AttemptId,
                    attempt.ProviderReference,
                    attempt.ProviderRouteId,
                    attempt.Status,
                    attempt.SubmittedAt,
                    attempt.ResolvedAt)),
            ]);

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgres
        && postgres.SqlState == PostgresErrorCodes.UniqueViolation;

    private static OnboardingApplicationConflictException Conflict(string code, string message) => new(code, message);

    private void DiscardPendingChanges()
    {
        foreach (EntityEntry entry in _context.ChangeTracker.Entries().ToList())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.State = EntityState.Detached;
                    break;
                case EntityState.Modified:
                case EntityState.Deleted:
                    entry.State = EntityState.Unchanged;
                    break;
                default:
                    break;
            }
        }
    }
}
