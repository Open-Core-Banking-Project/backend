using System;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CoreLedger.Domain.Institutions;
using Xunit;

namespace CoreBanking.CoreLedger.UnitTests;

public sealed class InstitutionTests
{
    private static readonly string[] RequiredServices = ["identity-access", "audit"];
    private static readonly string[] IdentityOnlyService = ["identity-access"];

    [Fact]
    public void Create_sets_a_valid_draft_institution()
    {
        DateTimeOffset createdAt = new(2026, 10, 18, 8, 0, 0, TimeSpan.Zero);

        Institution institution = Institution.Create(InstitutionId.From("institution-001"), "bank-001", "Bank One", "NG", "NGN", createdAt);

        Assert.Equal("bank-001", institution.Code);
        Assert.Equal("Bank One", institution.LegalName);
        Assert.Equal("NG", institution.CountryCode);
        Assert.Equal("NGN", institution.BaseCurrency);
        Assert.Equal(InstitutionStatus.Draft, institution.Status);
        Assert.Equal(createdAt, institution.CreatedAt);
        Assert.Equal(createdAt, institution.UpdatedAt);
        Assert.Equal("institution-001", institution.Id.Value);
    }

    [Fact]
    public void Create_canonicalises_country_and_currency_codes()
    {
        Institution institution = Institution.Create(
            InstitutionId.From("institution-001"),
            "bank-001",
            "Bank One",
            " ng ",
            " ngn ",
            new DateTimeOffset(2026, 10, 18, 8, 0, 0, TimeSpan.Zero));

        Assert.Equal("NG", institution.CountryCode);
        Assert.Equal("NGN", institution.BaseCurrency);
    }

    [Fact]
    public void Create_canonicalises_the_institution_code_for_unique_comparison()
    {
        Institution institution = Institution.Create(
            InstitutionId.From("institution-001"),
            " Bank-001 ",
            "Bank One",
            "NG",
            "NGN",
            new DateTimeOffset(2026, 10, 18, 8, 0, 0, TimeSpan.Zero));

        Assert.Equal("bank-001", institution.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_invalid_required_values(string? value)
    {
        if (value is null)
        {
            Assert.Throws<ArgumentNullException>(() => Institution.Create(InstitutionId.From("institution-001"), null!, "Bank One", "NG", "NGN", DateTimeOffset.UtcNow));
            return;
        }

        Assert.Throws<ArgumentException>(() => Institution.Create(InstitutionId.From("institution-001"), value, "Bank One", "NG", "NGN", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void BeginProvisioning_transitions_from_draft_to_provisioning()
    {
        DateTimeOffset createdAt = new(2026, 10, 18, 8, 0, 0, TimeSpan.Zero);
        Institution institution = CreateInstitution(createdAt);

        DateTimeOffset startedAt = new(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);
        TenantProvisioning provisioning = TenantProvisioning.Create(institution.Id, 4, RequiredServices);
        institution.BeginProvisioning(provisioning, startedAt);

        Assert.Equal(InstitutionStatus.Provisioning, institution.Status);
        Assert.Equal(4, institution.ProvisioningPlanVersion);
        Assert.Equal(startedAt, institution.ProvisioningStartedAt);
        Assert.Equal(startedAt, institution.UpdatedAt);
    }

    [Fact]
    public void MarkReadyForActivation_requires_provisioning_state()
    {
        DateTimeOffset createdAt = new(2026, 10, 18, 8, 0, 0, TimeSpan.Zero);
        Institution institution = CreateInstitution(createdAt);

        DateTimeOffset provisioningStartedAt = new(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);
        TenantProvisioning provisioning = CreateCompletedProvisioning(institution.Id);
        institution.BeginProvisioning(provisioning, provisioningStartedAt);

        DateTimeOffset readyAt = new(2026, 10, 18, 10, 0, 0, TimeSpan.Zero);
        institution.MarkReadyForActivation(provisioning, readyAt);

        Assert.Equal(InstitutionStatus.PendingActivation, institution.Status);
        Assert.Equal(readyAt, institution.ReadyForActivationAt);
    }

    [Fact]
    public void Activate_requires_pending_activation()
    {
        DateTimeOffset createdAt = new(2026, 10, 18, 8, 0, 0, TimeSpan.Zero);
        Institution institution = CreateInstitution(createdAt);

        TenantProvisioning provisioning = CreateCompletedProvisioning(institution.Id);
        institution.BeginProvisioning(provisioning, createdAt.AddHours(1));
        institution.MarkReadyForActivation(provisioning, createdAt.AddHours(2));

        DateTimeOffset activatedAt = createdAt.AddHours(3);
        institution.Activate(activatedAt);

        Assert.Equal(InstitutionStatus.Active, institution.Status);
        Assert.Equal(activatedAt, institution.ActivatedAt);
    }

    [Fact]
    public void Suspend_and_reactivate_follow_the_active_lifecycle()
    {
        DateTimeOffset createdAt = new(2026, 10, 18, 8, 0, 0, TimeSpan.Zero);
        Institution institution = CreateInstitution(createdAt);

        TenantProvisioning provisioning = CreateCompletedProvisioning(institution.Id);
        institution.BeginProvisioning(provisioning, createdAt.AddHours(1));
        institution.MarkReadyForActivation(provisioning, createdAt.AddHours(2));
        institution.Activate(createdAt.AddHours(3));

        DateTimeOffset suspendedAt = createdAt.AddHours(4);
        institution.Suspend(suspendedAt);
        Assert.Equal(InstitutionStatus.Suspended, institution.Status);
        Assert.Equal(suspendedAt, institution.SuspendedAt);

        DateTimeOffset reactivatedAt = createdAt.AddHours(5);
        institution.Reactivate(reactivatedAt);
        Assert.Equal(InstitutionStatus.Active, institution.Status);
        Assert.Null(institution.SuspendedAt);
    }

    [Fact]
    public void Provisioning_failure_requires_recovery_before_readiness()
    {
        DateTimeOffset createdAt = new(2026, 10, 18, 8, 0, 0, TimeSpan.Zero);
        Institution institution = CreateInstitution(createdAt);
        TenantProvisioning provisioning = TenantProvisioning.Create(institution.Id, 1, RequiredServices);

        institution.BeginProvisioning(provisioning, createdAt.AddHours(1));
        provisioning.RecordInitialised("identity-access");
        provisioning.RecordFailure("audit", "service_unavailable");
        institution.FailProvisioning(provisioning, createdAt.AddHours(2));

        Assert.Equal(InstitutionStatus.ProvisioningFailed, institution.Status);
        Assert.Equal(createdAt.AddHours(2), institution.ProvisioningFailedAt);
        Assert.Throws<InvalidOperationException>(
            () => institution.MarkReadyForActivation(provisioning, createdAt.AddHours(3)));

        institution.RetryProvisioning(provisioning, createdAt.AddHours(4));
        Assert.Null(institution.ProvisioningFailedAt);
        Assert.Equal(createdAt.AddHours(4), institution.LastProvisioningAttemptAt);
        provisioning.RecordInitialised("audit");
        institution.MarkReadyForActivation(provisioning, createdAt.AddHours(5));

        Assert.Equal(InstitutionStatus.PendingActivation, institution.Status);
        Assert.True(provisioning.AllRequiredServicesInitialised);
    }

    [Fact]
    public void Readiness_is_rejected_until_every_configured_service_initialises()
    {
        DateTimeOffset createdAt = new(2026, 10, 18, 8, 0, 0, TimeSpan.Zero);
        Institution institution = CreateInstitution(createdAt);
        TenantProvisioning provisioning = TenantProvisioning.Create(institution.Id, 7, RequiredServices);

        institution.BeginProvisioning(provisioning, createdAt.AddHours(1));
        provisioning.RecordInitialised("identity-access");

        Assert.Throws<InvalidOperationException>(
            () => institution.MarkReadyForActivation(provisioning, createdAt.AddHours(2)));
        Assert.Equal(InstitutionStatus.Provisioning, institution.Status);
    }

    [Fact]
    public void Acknowledgements_are_scoped_to_the_configured_plan()
    {
        TenantProvisioning provisioning = TenantProvisioning.Create(
            InstitutionId.From("institution-001"),
            3,
            IdentityOnlyService);

        Assert.Throws<InvalidOperationException>(() => provisioning.RecordInitialised("payments"));
        provisioning.RecordInitialised("identity-access");
        Assert.Throws<InvalidOperationException>(() => provisioning.RecordFailure("identity-access", "late_failure"));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TenantProvisioning.Create(InstitutionId.From("institution-001"), 0, IdentityOnlyService));
        Assert.Throws<ArgumentException>(
            () => TenantProvisioning.Create(InstitutionId.From("institution-001"), 1, Array.Empty<string>()));
    }

    [Fact]
    public void Readiness_requires_acknowledgements_for_the_same_plan_version()
    {
        DateTimeOffset createdAt = new(2026, 10, 18, 8, 0, 0, TimeSpan.Zero);
        Institution institution = CreateInstitution(createdAt);
        TenantProvisioning startedPlan = TenantProvisioning.Create(institution.Id, 1, RequiredServices);
        TenantProvisioning otherPlan = CreateCompletedProvisioning(institution.Id, 2);

        institution.BeginProvisioning(startedPlan, createdAt.AddHours(1));

        Assert.Throws<InvalidOperationException>(
            () => institution.MarkReadyForActivation(otherPlan, createdAt.AddHours(2)));
    }

    [Fact]
    public void Provisioning_plan_must_belong_to_the_same_institution()
    {
        DateTimeOffset createdAt = new(2026, 10, 18, 8, 0, 0, TimeSpan.Zero);
        Institution institution = CreateInstitution(createdAt);
        TenantProvisioning foreignProvisioning = TenantProvisioning.Create(
            InstitutionId.From("institution-002"),
            1,
            RequiredServices);

        Assert.Throws<InvalidOperationException>(
            () => institution.BeginProvisioning(foreignProvisioning, createdAt.AddHours(1)));
        Assert.Equal(InstitutionStatus.Draft, institution.Status);
    }

    [Fact]
    public void Lifecycle_timestamps_cannot_move_backwards()
    {
        DateTimeOffset createdAt = new(2026, 10, 18, 8, 0, 0, TimeSpan.Zero);
        Institution institution = CreateInstitution(createdAt);
        TenantProvisioning provisioning = CreateCompletedProvisioning(institution.Id);

        Assert.Throws<InvalidOperationException>(
            () => institution.BeginProvisioning(provisioning, createdAt.AddMinutes(-1)));

        institution.BeginProvisioning(provisioning, createdAt.AddHours(1));
        Assert.Throws<InvalidOperationException>(
            () => institution.MarkReadyForActivation(provisioning, createdAt.AddMinutes(30)));
        Assert.Equal(InstitutionStatus.Provisioning, institution.Status);
    }

    private static Institution CreateInstitution(DateTimeOffset createdAt) =>
        Institution.Create(InstitutionId.From("institution-001"), "bank-001", "Bank One", "NG", "NGN", createdAt);

    private static TenantProvisioning CreateCompletedProvisioning(InstitutionId institutionId)
        => CreateCompletedProvisioning(institutionId, 1);

    private static TenantProvisioning CreateCompletedProvisioning(InstitutionId institutionId, int planVersion)
    {
        TenantProvisioning provisioning = TenantProvisioning.Create(institutionId, planVersion, RequiredServices);
        provisioning.RecordInitialised("identity-access");
        provisioning.RecordInitialised("audit");
        return provisioning;
    }
}
