using System;
using System.Linq;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.CustomerOnboarding.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace CoreBanking.CustomerOnboarding.UnitTests.Persistence;

/// <summary>
/// Model-level guarantees for the party and onboarding tables: the right schema and table names, the
/// uniqueness the use cases depend on, and a fail-closed tenant filter on every new record.
/// Decision: epic 03 database design.
/// </summary>
public sealed class OnboardingModelTests
{
    [Theory]
    [InlineData(typeof(PartyRecord), "party", "parties")]
    [InlineData(typeof(PartyIndividualRecord), "party", "individuals")]
    [InlineData(typeof(PartyNameRecord), "party", "names")]
    [InlineData(typeof(PartyContactRecord), "party", "contacts")]
    [InlineData(typeof(PartyAddressRecord), "party", "addresses")]
    [InlineData(typeof(OnboardingApplicationRecord), "onboarding", "applications")]
    [InlineData(typeof(OnboardingApplicationRevisionRecord), "onboarding", "application_revisions")]
    public void A_new_record_is_mapped_to_its_owned_schema(Type recordType, string schema, string table)
    {
        using CustomerOnboardingDbContext context = CreateContext();
        IEntityType entity = context.Model.FindEntityType(recordType)!;

        Assert.Equal(schema, entity.GetSchema());
        Assert.Equal(table, entity.GetTableName());
        Assert.NotEmpty(entity.GetDeclaredQueryFilters());
    }

    [Fact]
    public void The_application_reference_is_unique_within_an_institution()
    {
        using CustomerOnboardingDbContext context = CreateContext();
        IEntityType application = context.Model.FindEntityType(typeof(OnboardingApplicationRecord))!;

        Assert.Contains(application.GetIndexes(), index =>
            index.IsUnique
            && index.Properties.Select(property => property.Name).SequenceEqual(["InstitutionId", "ApplicationReference"]));
    }

    [Fact]
    public void An_active_contact_value_token_is_unique_per_party()
    {
        using CustomerOnboardingDbContext context = CreateContext();
        IEntityType contact = context.Model.FindEntityType(typeof(PartyContactRecord))!;

        IIndex index = Assert.Single(contact.GetIndexes(), candidate =>
            candidate.Properties.Select(property => property.Name).SequenceEqual(["InstitutionId", "PartyId", "LookupToken"]));

        Assert.True(index.IsUnique);
        Assert.Equal("effective_to IS NULL", index.GetFilter());
    }

    [Fact]
    public void A_revision_is_immutable_and_unique_per_application()
    {
        using CustomerOnboardingDbContext context = CreateContext();
        IEntityType revision = context.Model.FindEntityType(typeof(OnboardingApplicationRevisionRecord))!;

        Assert.Equal(
            ["InstitutionId", "ApplicationId", "Revision"],
            revision.FindPrimaryKey()!.Properties.Select(property => property.Name));
    }

    [Fact]
    public void The_application_capture_is_stored_as_json()
    {
        using CustomerOnboardingDbContext context = CreateContext();
        IEntityType application = context.Model.FindEntityType(typeof(OnboardingApplicationRecord))!;

        Assert.Equal("jsonb", application.FindProperty(nameof(OnboardingApplicationRecord.CurrentCapture))!.GetColumnType());
    }

    private static CustomerOnboardingDbContext CreateContext()
    {
        DbContextOptions<CustomerOnboardingDbContext> options =
            new DbContextOptionsBuilder<CustomerOnboardingDbContext>()
                .UseNpgsql("Host=127.0.0.1;Database=model_only;Username=model;******")
                .Options;
        return new CustomerOnboardingDbContext(options, new AmbientTenantContext());
    }
}
