using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.IdentityAccess.Application.Authentication;

namespace CoreBanking.IdentityAccess.Infrastructure.Authentication;

/// <summary>
/// Production provider boundary. Direct credential submission is intentionally unsupported: ADR-0008
/// requires an OIDC authorisation-code flow with PKCE, not a platform password grant.
/// </summary>
public sealed class KeycloakAuthenticationProviderAdapter : IAuthenticationProviderAdapter
{
    public string ProviderName => "keycloak";

    public Task<ProviderAuthenticationResult> AuthenticateSyntheticAsync(
        SyntheticAuthenticationRequest request,
        CancellationToken cancellationToken) => Task.FromResult(ProviderAuthenticationResult.Unsupported());
}

/// <summary>
/// Development-only adapter for explicitly configured synthetic credential references. Values are
/// test fixtures, never password equivalents, and are not logged or persisted.
/// </summary>
public sealed class SyntheticAuthenticationProviderAdapter : IAuthenticationProviderAdapter
{
    private readonly IReadOnlyDictionary<string, string> _subjects;

    public SyntheticAuthenticationProviderAdapter(IReadOnlyDictionary<string, string> subjects)
    {
        _subjects = subjects ?? throw new ArgumentNullException(nameof(subjects));
    }

    public string ProviderName => "synthetic";

    public Task<ProviderAuthenticationResult> AuthenticateSyntheticAsync(
        SyntheticAuthenticationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_subjects.TryGetValue(request.CredentialReference, out string? subject)
            ? ProviderAuthenticationResult.Succeeded(subject)
            : ProviderAuthenticationResult.Invalid());
    }
}
