using System;
using System.Collections.Generic;

namespace CoreBanking.IntegrationHub.Contracts;

public enum CredentialReferenceKindV1
{
    ClientSecret,
    WebhookSecret,
    Certificate,
    ApiKey,
}

public sealed record RegisterCredentialReferenceRequestV1(
    string Environment,
    string ProviderCode,
    CredentialReferenceKindV1 Kind,
    string ReferenceName,
    string Owner,
    DateTimeOffset? ExpiresAt,
    string Reason);

public sealed record RotateCredentialReferenceRequestV1(string ReferenceName, DateTimeOffset? ExpiresAt, string Reason);

public sealed record RegisterApiClientRequestV1(
    string Environment,
    string ClientIdentifier,
    string DisplayName,
    IReadOnlyList<string> AllowedScopes,
    IReadOnlyList<string> AllowedNetworkRanges,
    string Reason);

public sealed record RevokeApiClientRequestV1(string Reason);
