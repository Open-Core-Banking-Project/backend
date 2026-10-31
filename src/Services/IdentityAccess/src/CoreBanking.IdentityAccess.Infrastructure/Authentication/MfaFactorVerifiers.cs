using System;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.IdentityAccess.Application.Authentication;

namespace CoreBanking.IdentityAccess.Infrastructure.Authentication;

/// <summary>
/// Fail-closed boundary used whenever no approved factor provider is configured. It never accepts
/// a response, so a missing provider can only deny.
/// Decision: ADR-0008 and P1-E02-D08.
/// </summary>
public sealed class UnconfiguredMfaFactorVerifier : IMfaFactorVerifier
{
    public string ProviderName => "unconfigured";

    public Task<MfaVerificationResult> VerifyAsync(MfaVerificationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(MfaVerificationResult.Unavailable());
    }
}

/// <summary>
/// Development-only fixture that accepts one explicitly configured synthetic code. The value is a
/// test fixture, never a production secret, and is not logged, stored or echoed.
/// </summary>
public sealed class SyntheticMfaFactorVerifier : IMfaFactorVerifier
{
    private readonly string _acceptanceCode;

    public SyntheticMfaFactorVerifier(string acceptanceCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(acceptanceCode);
        _acceptanceCode = acceptanceCode;
    }

    public string ProviderName => "synthetic";

    public Task<MfaVerificationResult> VerifyAsync(MfaVerificationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(string.Equals(request.Response, _acceptanceCode, StringComparison.Ordinal)
            ? MfaVerificationResult.Verified()
            : MfaVerificationResult.Rejected("factor_invalid"));
    }
}
