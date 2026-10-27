using System;
using System.Collections.Generic;
using System.Linq;

namespace CoreBanking.IntegrationHub.Domain.ProviderAdministration;

public sealed class ApiClient
{
    private ApiClient() { }

    public required string Id { get; init; }
    public required string InstitutionId { get; init; }
    public required string Environment { get; init; }
    public required string ClientIdentifier { get; init; }
    public required string DisplayName { get; init; }
    public required IReadOnlyList<string> AllowedScopes { get; init; }
    public required IReadOnlyList<string> AllowedNetworkRanges { get; init; }

    public static ApiClient Register(
        string id,
        string institutionId,
        string environment,
        string clientIdentifier,
        string displayName,
        IEnumerable<string> allowedScopes,
        IEnumerable<string>? allowedNetworkRanges)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(institutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(environment);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientIdentifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        string[] scopes = allowedScopes?.Select(Normalize).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()
            ?? throw new ArgumentNullException(nameof(allowedScopes));
        if (scopes.Length == 0) throw new ArgumentException("At least one scope is required.", nameof(allowedScopes));
        string[] networks = allowedNetworkRanges?.Select(Normalize).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray() ?? [];

        return new ApiClient
        {
            Id = id.Trim(),
            InstitutionId = institutionId.Trim(),
            Environment = environment.Trim().ToLowerInvariant(),
            ClientIdentifier = clientIdentifier.Trim(),
            DisplayName = displayName.Trim(),
            AllowedScopes = scopes,
            AllowedNetworkRanges = networks,
        };
    }

    private static string Normalize(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Trim();
    }
}
