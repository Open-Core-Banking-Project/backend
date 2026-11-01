using System;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.IdentityAccess.Domain.Authentication;

namespace CoreBanking.IdentityAccess.Application.Authentication;

/// <summary>
/// Provider boundary for a second factor. The platform never implements the factor itself: the
/// approved provider verifies TOTP or a passkey and reports only the outcome. A response is never
/// persisted, logged or echoed.
/// Decision: ADR-0008 and P1-E02-D08.
/// </summary>
public interface IMfaFactorVerifier
{
    string ProviderName { get; }

    Task<MfaVerificationResult> VerifyAsync(MfaVerificationRequest request, CancellationToken cancellationToken);
}

public sealed record MfaVerificationRequest(
    InstitutionId InstitutionId,
    ActorId ActorId,
    MfaFactorKind FactorKind,
    string ProviderReference,
    string Response,
    string Purpose);

/// <summary>Verification outcome. <see cref="IsVerified"/> is true only when the provider confirmed the response.</summary>
public sealed record MfaVerificationResult(bool IsVerified, string? FailureCode)
{
    public static MfaVerificationResult Verified() => new(true, null);

    public static MfaVerificationResult Rejected(string failureCode) => new(false, failureCode);

    public static MfaVerificationResult Unavailable() => new(false, "provider_unavailable");
}
