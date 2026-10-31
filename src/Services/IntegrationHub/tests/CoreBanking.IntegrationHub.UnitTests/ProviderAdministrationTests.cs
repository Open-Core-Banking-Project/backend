using System;
using System.Linq;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.IntegrationHub.Contracts;
using CoreBanking.IntegrationHub.Domain.ProviderAdministration;
using CoreBanking.IntegrationHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace CoreBanking.IntegrationHub.UnitTests;

public sealed class ProviderAdministrationTests
{
    [Fact]
    public void Credential_registration_keeps_only_a_logical_reference_and_metadata()
    {
        DateTimeOffset now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);
        CredentialReference reference = CredentialReference.Register(
            "credential-ref-1", "institution-1", "production", "provider-1", CredentialReferenceKind.ClientSecret,
            "vault/providers/provider-1/client-secret", "security-team", now, now.AddDays(30));

        Assert.Equal("vault/providers/provider-1/client-secret", reference.ReferenceName);
        Assert.Equal(1, reference.Version);
        Assert.DoesNotContain(reference.GetType().GetProperties(), property =>
            property.Name is "Secret" or "SecretValue" or "PrivateKey" or "CredentialValue");
    }

    [Fact]
    public void Credential_reference_rejects_expired_metadata()
    {
        DateTimeOffset now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);
        Assert.Throws<ArgumentOutOfRangeException>(() => CredentialReference.Register(
            "credential-ref-1", "institution-1", "production", "provider-1", CredentialReferenceKind.Certificate,
            "vault/providers/provider-1/certificate", "security-team", now, now));
    }

    [Fact]
    public void Api_client_requires_at_least_one_explicit_scope()
    {
        Assert.Throws<ArgumentException>(() => ApiClient.Register(
            "api-client-1", "institution-1", "production", "partner-client", "Partner", [], []));
    }

    [Fact]
    public void Administration_contracts_have_no_secret_value_fields()
    {
        Type[] contracts = [typeof(RegisterCredentialReferenceRequestV1), typeof(RotateCredentialReferenceRequestV1), typeof(RegisterApiClientRequestV1)];
        Assert.All(contracts, contract => Assert.DoesNotContain(contract.GetProperties(), property =>
            property.Name.Contains("SecretValue", StringComparison.OrdinalIgnoreCase)
            || property.Name.Contains("PrivateKey", StringComparison.OrdinalIgnoreCase)
            || property.Name.Equals("Password", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Provider_administration_tables_are_tenant_filtered_and_constrained()
    {
        using IntegrationHubDbContext context = CreateContext();
        IEntityType credential = context.Model.FindEntityType(typeof(CredentialReferenceRecord))!;
        IEntityType client = context.Model.FindEntityType(typeof(ApiClientRecord))!;
        Assert.NotEmpty(credential.GetDeclaredQueryFilters());
        Assert.NotEmpty(client.GetDeclaredQueryFilters());
        Assert.Contains(credential.GetIndexes(), index => index.IsUnique && index.Properties.Select(property => property.Name)
            .SequenceEqual(["InstitutionId", "CredentialReferenceId", "Version"]));
        Assert.Contains(client.GetIndexes(), index => index.IsUnique && index.Properties.Select(property => property.Name)
            .SequenceEqual(["InstitutionId", "Environment", "ClientIdentifier"]));
    }

    private static IntegrationHubDbContext CreateContext()
    {
        DbContextOptions<IntegrationHubDbContext> options = new DbContextOptionsBuilder<IntegrationHubDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=model_only;Username=model;Password=model").Options;
        return new IntegrationHubDbContext(options, new AmbientTenantContext());
    }
}
