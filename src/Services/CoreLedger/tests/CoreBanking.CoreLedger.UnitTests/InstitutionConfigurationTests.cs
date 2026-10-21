using System;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CoreLedger.Domain.Institutions;
using Xunit;

namespace CoreBanking.CoreLedger.UnitTests;

public sealed class InstitutionConfigurationTests
{
    private static readonly InstitutionId Institution = InstitutionId.From("institution-1");
    private static readonly ActorId Maker = ActorId.From("maker-1");
    private static readonly ActorId Approver = ActorId.From("approver-1");

    [Fact]
    public void Equivalent_json_has_one_stable_content_hash()
    {
        ConfigurationContent first = ConfigurationContent.Create("""{"limit":1000,"enabled":true}""");
        ConfigurationContent second = ConfigurationContent.Create("""{ "enabled": true, "limit": 1000 }""");

        Assert.Equal(first.CanonicalJson, second.CanonicalJson);
        Assert.Equal(first.Hash, second.Hash);
    }

    [Theory]
    [InlineData("password")]
    [InlineData("apiKey")]
    [InlineData("private-key")]
    [InlineData("secret")]
    public void Secret_bearing_properties_are_rejected(string propertyName)
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            ConfigurationContent.Create($"{{\"nested\":{{\"{propertyName}\":\"must-not-persist\"}}}}"));

        Assert.Contains("secret material", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Submission_requires_simulation_of_the_exact_content_hash()
    {
        InstitutionConfigurationVersion version = CreateVersion();

        Assert.Throws<InvalidOperationException>(() => version.Submit(Maker));
        Assert.Throws<InvalidOperationException>(() => version.RecordSimulation(new ConfigurationSimulationEvidence(
            "simulation-1", new string('0', 64), "product-rules-v1", "valid", DateTimeOffset.UtcNow)));

        version.RecordSimulation(SimulationFor(version));
        version.Submit(Maker);

        Assert.Equal(ConfigurationVersionStatus.Submitted, version.Status);
    }

    [Fact]
    public void Maker_cannot_approve_or_publish_their_own_version()
    {
        InstitutionConfigurationVersion makerApproval = CreateVersion();
        makerApproval.RecordSimulation(SimulationFor(makerApproval));
        makerApproval.Submit(Maker);
        Assert.Throws<InvalidOperationException>(() => makerApproval.Approve(Maker, "approval-1"));

        InstitutionConfigurationVersion makerPublication = CreateVersion();
        makerPublication.RecordSimulation(SimulationFor(makerPublication));
        makerPublication.Submit(Maker);
        makerPublication.Approve(Approver, "approval-1");
        Assert.Throws<InvalidOperationException>(() => makerPublication.Publish(Maker, DateTimeOffset.UtcNow, null, []));
    }

    [Fact]
    public void Approved_exact_version_can_be_published_once()
    {
        InstitutionConfigurationVersion version = CreateVersion();
        version.RecordSimulation(SimulationFor(version));
        version.Submit(Maker);
        version.Approve(Approver, "approval-1");

        DateTimeOffset effectiveFrom = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset effectiveTo = effectiveFrom.AddMonths(1);
        version.Publish(Approver, effectiveFrom, effectiveTo, []);

        Assert.Equal(ConfigurationVersionStatus.Published, version.Status);
        Assert.Equal(effectiveFrom, version.EffectiveFrom);
        Assert.Equal(effectiveTo, version.EffectiveTo);
        Assert.Throws<InvalidOperationException>(() => version.Publish(Approver, effectiveFrom, effectiveTo, []));
    }

    [Fact]
    public void Rollback_is_a_new_draft_linked_to_immutable_published_history()
    {
        InstitutionConfigurationVersion published = CreateVersion();
        published.RecordSimulation(SimulationFor(published));
        published.Submit(Maker);
        published.Approve(Approver, "approval-1");
        published.Publish(Approver, DateTimeOffset.UtcNow, null, []);

        InstitutionConfigurationVersion rollback = published.CreateRollbackDraft("version-2", 2, Maker);

        Assert.Equal(ConfigurationVersionStatus.Published, published.Status);
        Assert.Equal(ConfigurationVersionStatus.Draft, rollback.Status);
        Assert.Equal(published.VersionId, rollback.RollbackOfVersionId);
        Assert.Equal(published.Content.Hash, rollback.Content.Hash);
    }

    [Fact]
    public void Effective_periods_are_half_open_and_adjacent_versions_do_not_overlap()
    {
        DateTimeOffset november = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset december = november.AddMonths(1);
        PublishedConfigurationVersion first = new(
            "version-1", ConfigurationEffectivePeriod.Create(november, december));
        PublishedConfigurationVersion second = new(
            "version-2", ConfigurationEffectivePeriod.Create(december, null));

        ConfigurationPublicationPolicy.EnsureNoOverlap([first], second.EffectivePeriod);

        Assert.Equal("version-1", ConfigurationPublicationPolicy.ResolveEffective([first, second], november).VersionId);
        Assert.Equal("version-2", ConfigurationPublicationPolicy.ResolveEffective([first, second], december).VersionId);
        Assert.Throws<InvalidOperationException>(() =>
            ConfigurationPublicationPolicy.ResolveEffective([first, second], november.AddTicks(-1)));
    }

    [Fact]
    public void Publication_rejects_an_overlapping_period()
    {
        InstitutionConfigurationVersion version = CreateVersion();
        version.RecordSimulation(SimulationFor(version));
        version.Submit(Maker);
        version.Approve(Approver, "approval-1");
        DateTimeOffset november = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        PublishedConfigurationVersion existing = new(
            "existing", ConfigurationEffectivePeriod.Create(november, november.AddMonths(1)));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            version.Publish(Approver, november.AddDays(10), null, [existing]));

        Assert.Equal("configuration_publication_period_overlaps", exception.Message);
        Assert.Equal(ConfigurationVersionStatus.Approved, version.Status);
    }

    [Fact]
    public void Effective_selector_fails_closed_if_persisted_periods_overlap()
    {
        DateTimeOffset november = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        PublishedConfigurationVersion first = new(
            "version-1", ConfigurationEffectivePeriod.Create(november, null));
        PublishedConfigurationVersion second = new(
            "version-2", ConfigurationEffectivePeriod.Create(november.AddDays(1), null));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            ConfigurationPublicationPolicy.ResolveEffective([first, second], november.AddDays(2)));

        Assert.Equal("configuration_publication_period_overlaps", exception.Message);
    }

    [Fact]
    public void Invalid_effective_period_is_rejected()
    {
        DateTimeOffset instant = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

        Assert.Throws<ArgumentException>(() => ConfigurationEffectivePeriod.Create(instant, instant));
        Assert.Throws<ArgumentException>(() => ConfigurationEffectivePeriod.Create(instant, instant.AddTicks(-1)));
    }

    private static InstitutionConfigurationVersion CreateVersion() => InstitutionConfigurationVersion.Create(
        Institution,
        "product-defaults",
        "version-1",
        1,
        "products",
        "institution",
        ConfigurationContent.Create("""{"minimumBalance":{"amount":0,"currency":"NGN"}}"""),
        Maker);

    private static ConfigurationSimulationEvidence SimulationFor(InstitutionConfigurationVersion version) => new(
        "simulation-1",
        version.Content.Hash,
        "product-rules-v1",
        "passed",
        new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));
}
