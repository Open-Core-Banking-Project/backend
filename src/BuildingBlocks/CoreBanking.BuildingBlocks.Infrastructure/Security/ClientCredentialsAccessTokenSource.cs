using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Security;

namespace CoreBanking.BuildingBlocks.Infrastructure.Security;

/// <summary>Configuration for one OAuth client-credentials workload identity.</summary>
public sealed record ClientCredentialsTokenOptions
{
    public const string SectionName = "IdentityAccess:WorkloadIdentity";

    public required Uri TokenEndpoint { get; init; }

    public required string ClientId { get; init; }

    public required string ClientSecretReference { get; init; }

    public void Validate()
    {
        if (!TokenEndpoint.IsAbsoluteUri || !string.Equals(TokenEndpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The workload token endpoint must be an absolute HTTPS URI.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(ClientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ClientSecretReference);
    }
}

/// <summary>
/// Acquires and briefly caches a provider-issued OAuth client-credentials token. Only an opaque
/// secret reference is configured; the credential is read through <see cref="ISecretProvider"/>
/// and is never logged or returned to callers.
/// </summary>
public sealed class ClientCredentialsAccessTokenSource : IWorkloadAccessTokenSource, IDisposable
{
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromSeconds(30);
    private readonly HttpClient _client;
    private readonly ISecretProvider _secrets;
    private readonly ClientCredentialsTokenOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _refreshAt;

    public ClientCredentialsAccessTokenSource(
        HttpClient client,
        ISecretProvider secrets,
        ClientCredentialsTokenOptions options,
        TimeProvider timeProvider)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _options.Validate();
    }

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (!string.IsNullOrWhiteSpace(_accessToken) && _refreshAt > now)
        {
            return _accessToken;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            now = _timeProvider.GetUtcNow();
            if (!string.IsNullOrWhiteSpace(_accessToken) && _refreshAt > now)
            {
                return _accessToken;
            }

            string? secret = await _secrets.TryReadAsync(_options.ClientSecretReference, cancellationToken)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(secret))
            {
                return null;
            }

            using FormUrlEncodedContent content = new(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _options.ClientId,
                ["client_secret"] = secret,
            });
            TokenResponse? token;
            try
            {
                using HttpResponseMessage response = await _client.PostAsync(_options.TokenEndpoint, content, cancellationToken)
                    .ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                return null;
            }
            catch (JsonException)
            {
                return null;
            }
            catch (NotSupportedException)
            {
                return null;
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return null;
            }
            if (token is null
                || string.IsNullOrWhiteSpace(token.AccessToken)
                || token.ExpiresIn is <= 30 or > 3600)
            {
                return null;
            }

            _accessToken = token.AccessToken;
            _refreshAt = now.AddSeconds(token.ExpiresIn).Subtract(RefreshSkew);
            return _accessToken;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private sealed record TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }
    }
}

/// <summary>Fails closed until workload identity configuration is supplied.</summary>
public sealed class UnconfiguredWorkloadAccessTokenSource : IWorkloadAccessTokenSource
{
    public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<string?>(null);
    }
}

/// <summary>Adds a provider-issued bearer token to one outbound service request.</summary>
public sealed class WorkloadBearerTokenHandler : DelegatingHandler
{
    private readonly IWorkloadAccessTokenSource _tokens;

    public WorkloadBearerTokenHandler(IWorkloadAccessTokenSource tokens) =>
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        string? token = await _tokens.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new HttpRequestException("Workload identity is unavailable.");
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
