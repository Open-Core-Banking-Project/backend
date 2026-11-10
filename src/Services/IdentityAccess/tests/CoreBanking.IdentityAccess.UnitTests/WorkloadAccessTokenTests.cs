using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.BuildingBlocks.Infrastructure.Security;
using Xunit;

namespace CoreBanking.IdentityAccess.UnitTests;

/// <summary>Client-credentials and bearer propagation evidence for ADR-0008 and P1-E02-S13.</summary>
[Trait("Category", "Unit")]
public sealed class WorkloadAccessTokenTests
{
    private static readonly ClientCredentialsTokenOptions Options = new()
    {
        TokenEndpoint = new Uri("https://identity.example.test/realms/core/protocol/openid-connect/token"),
        ClientId = "core-ledger",
        ClientSecretReference = "identity.core-ledger.client-secret",
    };

    [Fact]
    public async Task Client_credentials_are_exchanged_and_the_short_lived_token_is_cached()
    {
        RecordingHandler provider = new((request, _) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(Options.TokenEndpoint, request.RequestUri);
            return Json(HttpStatusCode.OK, "{\"access_token\":\"provider-token\",\"expires_in\":300}");
        });
        using ClientCredentialsAccessTokenSource source = new(
            new HttpClient(provider),
            new StubSecretProvider("synthetic-secret"),
            Options,
            TimeProvider.System);

        string? first = await source.GetAccessTokenAsync(CancellationToken.None);
        string? second = await source.GetAccessTokenAsync(CancellationToken.None);

        Assert.Equal("provider-token", first);
        Assert.Equal(first, second);
        Assert.Equal(1, provider.CallCount);
        Assert.Contains("grant_type=client_credentials", provider.LastBody, StringComparison.Ordinal);
        Assert.Contains("client_id=core-ledger", provider.LastBody, StringComparison.Ordinal);
        Assert.Contains("client_secret=synthetic-secret", provider.LastBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_secret_fails_closed_without_calling_the_provider()
    {
        RecordingHandler provider = new((_, _) => Json(HttpStatusCode.OK, "{}"));
        using ClientCredentialsAccessTokenSource source = new(
            new HttpClient(provider),
            new StubSecretProvider(null),
            Options,
            TimeProvider.System);

        Assert.Null(await source.GetAccessTokenAsync(CancellationToken.None));
        Assert.Equal(0, provider.CallCount);
    }

    [Theory]
    [InlineData("{\"access_token\":\"\",\"expires_in\":300}")]
    [InlineData("{\"access_token\":\"token\",\"expires_in\":30}")]
    [InlineData("{\"access_token\":\"token\",\"expires_in\":3601}")]
    [InlineData("not-json")]
    public async Task Invalid_provider_response_fails_closed(string responseBody)
    {
        RecordingHandler provider = new((_, _) => Json(HttpStatusCode.OK, responseBody));
        using ClientCredentialsAccessTokenSource source = new(
            new HttpClient(provider),
            new StubSecretProvider("synthetic-secret"),
            Options,
            TimeProvider.System);

        Assert.Null(await source.GetAccessTokenAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Bearer_handler_attaches_the_provider_token_to_the_service_call()
    {
        RecordingHandler target = new((request, _) =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("provider-token", request.Headers.Authorization?.Parameter);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using WorkloadBearerTokenHandler handler = new(new StubTokenSource("provider-token"))
        {
            InnerHandler = target,
        };
        using HttpClient client = new(handler);

        using HttpResponseMessage response = await client.GetAsync("https://identity.example.test/internal", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Bearer_handler_refuses_the_call_when_no_workload_token_is_available()
    {
        using WorkloadBearerTokenHandler handler = new(new StubTokenSource(null))
        {
            InnerHandler = new RecordingHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)),
        };
        using HttpClient client = new(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.GetAsync("https://identity.example.test/internal", CancellationToken.None));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string value) => new(status)
    {
        Content = new StringContent(value, Encoding.UTF8, "application/json"),
    };

    private sealed class StubSecretProvider(string? value) : ISecretProvider
    {
        public Task<string?> TryReadAsync(string logicalName, CancellationToken cancellationToken)
        {
            Assert.Equal(Options.ClientSecretReference, logicalName);
            return Task.FromResult(value);
        }
    }

    private sealed class StubTokenSource(string? value) : IWorkloadAccessTokenSource
    {
        public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken) => Task.FromResult(value);
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> response) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        public string LastBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            if (request.Content is not null)
            {
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return response(request, cancellationToken);
        }
    }
}
