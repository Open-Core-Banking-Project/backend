using System;
using System.Text.RegularExpressions;

namespace CoreBanking.IntegrationHub.Domain.ProviderAdministration;

public enum CredentialReferenceKind
{
    ClientSecret,
    WebhookSecret,
    Certificate,
    ApiKey,
}

public sealed class CredentialReference
{
    private static readonly Regex CodePattern = new("^[a-z0-9][a-z0-9._-]{1,63}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ReferencePattern = new("^[A-Za-z0-9][A-Za-z0-9._:/-]{2,255}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private CredentialReference() { }

    public required string Id { get; init; }
    public required string InstitutionId { get; init; }
    public required string Environment { get; init; }
    public required string ProviderCode { get; init; }
    public CredentialReferenceKind Kind { get; init; }
    public required string ReferenceName { get; init; }
    public required string Owner { get; init; }
    public int Version { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public string? PreviousVersionId { get; init; }

    public static CredentialReference Register(
        string id,
        string institutionId,
        string environment,
        string providerCode,
        CredentialReferenceKind kind,
        string referenceName,
        string owner,
        DateTimeOffset now,
        DateTimeOffset? expiresAt,
        int version = 1,
        string? previousVersionId = null)
    {
        Require(id, nameof(id), ReferencePattern);
        Require(institutionId, nameof(institutionId), ReferencePattern);
        Require(environment, nameof(environment), CodePattern);
        Require(providerCode, nameof(providerCode), CodePattern);
        Require(referenceName, nameof(referenceName), ReferencePattern);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        if (owner.Trim().Length > 128) throw new ArgumentOutOfRangeException(nameof(owner));
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);
        if (expiresAt is not null && expiresAt <= now) throw new ArgumentOutOfRangeException(nameof(expiresAt));

        return new CredentialReference
        {
            Id = id.Trim(),
            InstitutionId = institutionId.Trim(),
            Environment = environment.Trim().ToLowerInvariant(),
            ProviderCode = providerCode.Trim().ToLowerInvariant(),
            Kind = kind,
            ReferenceName = referenceName.Trim(),
            Owner = owner.Trim(),
            Version = version,
            ExpiresAt = expiresAt,
            PreviousVersionId = previousVersionId,
        };
    }

    private static void Require(string value, string parameter, Regex pattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameter);
        if (!pattern.IsMatch(value.Trim())) throw new ArgumentException("The value has an invalid reference format.", parameter);
    }
}
