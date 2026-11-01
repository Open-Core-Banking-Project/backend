using System;
using CoreBanking.BuildingBlocks.Application.Persistence;

namespace CoreBanking.IdentityAccess.Infrastructure.Persistence.Security;

/// <summary>
/// One enrolled second factor. Only an opaque provider reference is stored; secret material never
/// enters this table. Decision: ADR-0008, ADR-0010 and P1-E02-D08.
/// </summary>
public sealed class MfaEnrolmentRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }

    public required string EnrolmentId { get; set; }

    public required string IdentityId { get; set; }

    public required string FactorKind { get; set; }

    public required string Status { get; set; }

    public required string ProviderReference { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? VerifiedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public long ConcurrencyVersion { get; set; }
}

/// <summary>
/// Purpose-bound, single-use step-up evidence. Absence, expiry, a consumed record or a purpose,
/// session or device mismatch is a denial, never a fallback.
/// </summary>
public sealed class StepUpEvidenceRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }

    public required string EvidenceId { get; set; }

    public required string IdentityId { get; set; }

    public required string SessionId { get; set; }

    public string? DeviceId { get; set; }

    public required string Purpose { get; set; }

    public required string FactorKind { get; set; }

    public required string CorrelationId { get; set; }

    public DateTimeOffset IssuedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? ConsumedAt { get; set; }
}
