using System.Threading;
using System.Threading.Tasks;

namespace CoreBanking.IdentityAccess.Application.Authentication;

/// <summary>
/// Boundary to the selected identity provider. It never issues platform tokens or persists raw
/// credentials. Interactive Keycloak authentication remains the OIDC authorisation-code/PKCE flow.
/// </summary>
public interface IAuthenticationProviderAdapter
{
    string ProviderName { get; }

    Task<ProviderAuthenticationResult> AuthenticateSyntheticAsync(
        SyntheticAuthenticationRequest request,
        CancellationToken cancellationToken);
}

public sealed record SyntheticAuthenticationRequest(string CredentialReference, string Channel);

public sealed record ProviderAuthenticationResult(string Outcome, string? ProviderSubject)
{
    public static ProviderAuthenticationResult Unsupported() => new("unsupported", null);

    public static ProviderAuthenticationResult Invalid() => new("invalid_credentials", null);

    public static ProviderAuthenticationResult Succeeded(string providerSubject) => new("authenticated", providerSubject);
}
