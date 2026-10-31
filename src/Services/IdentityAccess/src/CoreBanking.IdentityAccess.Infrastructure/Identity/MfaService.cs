using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.IdentityAccess.Application.Authentication;
using CoreBanking.IdentityAccess.Contracts;
using CoreBanking.IdentityAccess.Domain.Authentication;
using CoreBanking.IdentityAccess.Infrastructure.Persistence;
using CoreBanking.IdentityAccess.Infrastructure.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.IdentityAccess.Infrastructure.Identity;

/// <summary>
/// Enrolment and step-up evidence for the factor family approved by P1-E02-D08. The provider
/// verifies the factor; this service stores only an opaque reference, binds evidence to the actor,
/// session, device and purpose, expires it after five minutes and consumes it once.
/// </summary>
public sealed class MfaService : IStepUpEvidenceSource
{
    private readonly IdentityAccessDbContext _context;
    private readonly IMfaFactorVerifier _verifier;
    private readonly TimeProvider _timeProvider;

    public MfaService(IdentityAccessDbContext context, IMfaFactorVerifier verifier, TimeProvider timeProvider)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<MfaEnrolmentResultV1> EnrolAsync(
        InstitutionId institutionId,
        ActorId actorId,
        MfaFactorKind factorKind,
        string providerReference,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentNullException.ThrowIfNull(actorId);

        if (!MfaPolicy.IsAvailable(factorKind))
        {
            throw new MfaException(MfaContractV1.FactorNotAvailable, "The factor is not available in Phase 1.");
        }

        if (string.IsNullOrWhiteSpace(providerReference) || providerReference.Length > 200)
        {
            throw new MfaException(MfaContractV1.FactorInvalid, "A provider reference is required.");
        }

        string kindValue = factorKind switch
        {
            MfaFactorKind.Totp => MfaContractV1.Totp,
            MfaFactorKind.Passkey => MfaContractV1.Passkey,
            MfaFactorKind.Sms => MfaContractV1.Sms,
            MfaFactorKind.Push => MfaContractV1.Push,
            _ => throw new MfaException(MfaContractV1.FactorInvalid, "The factor is not supported."),
        };

        bool exists = await _context.MfaEnrolments.AnyAsync(
            record => record.InstitutionId == institutionId.Value
                && record.IdentityId == actorId.Value
                && record.FactorKind == kindValue
                && record.Status != MfaContractV1.StatusRevoked,
            cancellationToken).ConfigureAwait(false);

        if (exists)
        {
            throw new MfaException(MfaContractV1.FactorInvalid, "An enrolment for this factor already exists.");
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        MfaEnrolmentRecord enrolment = new()
        {
            InstitutionId = institutionId.Value,
            EnrolmentId = Guid.NewGuid().ToString("N"),
            IdentityId = actorId.Value,
            FactorKind = kindValue,
            Status = MfaContractV1.StatusPending,
            ProviderReference = providerReference.Trim(),
            CreatedAt = now,
            ConcurrencyVersion = 1,
        };
        _context.MfaEnrolments.Add(enrolment);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new MfaEnrolmentResultV1
        {
            EnrolmentId = enrolment.EnrolmentId,
            FactorKind = enrolment.FactorKind,
            Status = enrolment.Status,
            CreatedAt = enrolment.CreatedAt,
        };
    }

    /// <summary>
    /// Verifies one challenge response and, on success, activates a pending enrolment and issues
    /// purpose-bound step-up evidence. A rejected or unavailable provider writes no evidence.
    /// </summary>
    public async Task<StepUpChallengeResultV1> VerifyStepUpAsync(
        InstitutionId institutionId,
        ActorId actorId,
        string sessionId,
        string? deviceId,
        string purpose,
        string response,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentNullException.ThrowIfNull(actorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);

        MfaEnrolmentRecord? enrolment = await _context.MfaEnrolments
            .Where(record => record.InstitutionId == institutionId.Value
                && record.IdentityId == actorId.Value
                && record.FactorKind == MfaContractV1.Totp
                && record.Status != MfaContractV1.StatusRevoked)
            .OrderByDescending(record => record.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (enrolment is null)
        {
            throw new MfaException(MfaContractV1.EnrolmentNotFound, "No enrolled factor is available.");
        }

        MfaVerificationResult verification = await _verifier.VerifyAsync(
            new MfaVerificationRequest(institutionId, actorId, MfaFactorKind.Totp, enrolment.ProviderReference, response, purpose),
            cancellationToken).ConfigureAwait(false);

        if (!verification.IsVerified)
        {
            return new StepUpChallengeResultV1
            {
                Status = MfaContractV1.StepUpRejected,
                Code = verification.FailureCode == "provider_unavailable"
                    ? MfaContractV1.ProviderUnavailable
                    : MfaContractV1.FactorInvalid,
            };
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        enrolment.Status = MfaContractV1.StatusActive;
        enrolment.VerifiedAt ??= now;
        enrolment.ConcurrencyVersion++;

        DateTimeOffset expiresAt = now.Add(MfaPolicy.StepUpValidity);
        _context.StepUpEvidence.Add(new StepUpEvidenceRecord
        {
            InstitutionId = institutionId.Value,
            EvidenceId = Guid.NewGuid().ToString("N"),
            IdentityId = actorId.Value,
            SessionId = sessionId,
            DeviceId = deviceId,
            Purpose = purpose.Trim(),
            FactorKind = MfaContractV1.Totp,
            CorrelationId = correlationId,
            IssuedAt = now,
            ExpiresAt = expiresAt,
        });

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new StepUpChallengeResultV1
        {
            Status = MfaContractV1.StepUpVerified,
            ExpiresAt = expiresAt,
        };
    }

    public async Task<bool> TryConsumeAsync(AuthenticatedActor actor, string purpose, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);

        if (string.IsNullOrWhiteSpace(actor.SessionId))
        {
            return false;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        StepUpEvidenceRecord? evidence = await _context.StepUpEvidence
            .Where(record => record.InstitutionId == actor.InstitutionId.Value
                && record.IdentityId == actor.Id.Value
                && record.SessionId == actor.SessionId
                && record.Purpose == purpose
                && record.ConsumedAt == null)
            .OrderByDescending(record => record.IssuedAt)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (evidence is null
            || !StepUpEvidenceRules.IsValidFor(
                evidence.IssuedAt,
                evidence.ExpiresAt,
                evidence.ConsumedAt,
                evidence.Purpose,
                purpose,
                evidence.SessionId,
                actor.SessionId!,
                evidence.DeviceId,
                actor.DeviceId,
                now))
        {
            return false;
        }

        evidence.ConsumedAt = now;
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }
}

/// <summary>Stable failure with a contract code. No provider detail or secret is carried.</summary>
public sealed class MfaException : Exception
{
    public MfaException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
