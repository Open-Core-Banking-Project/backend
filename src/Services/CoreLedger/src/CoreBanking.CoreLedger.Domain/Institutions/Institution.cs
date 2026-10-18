using System;
using CoreBanking.BuildingBlocks.Domain.Identity;

namespace CoreBanking.CoreLedger.Domain.Institutions;

public sealed class Institution
{
    private Institution(
        InstitutionId id,
        string code,
        string legalName,
        string countryCode,
        string baseCurrency,
        DateTimeOffset createdAt)
    {
        Id = id;
        Code = code;
        LegalName = legalName;
        CountryCode = countryCode;
        BaseCurrency = baseCurrency;
        CreatedAt = createdAt;
        Status = InstitutionStatus.Draft;
        UpdatedAt = createdAt;
    }

    public InstitutionId Id { get; }

    public string Code { get; private set; }

    public string LegalName { get; private set; }

    public string CountryCode { get; private set; }

    public string BaseCurrency { get; private set; }

    public InstitutionStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? ProvisioningStartedAt { get; private set; }

    public DateTimeOffset? ProvisioningFailedAt { get; private set; }

    public DateTimeOffset? LastProvisioningAttemptAt { get; private set; }

    public int? ProvisioningPlanVersion { get; private set; }

    public DateTimeOffset? ReadyForActivationAt { get; private set; }

    public DateTimeOffset? ActivatedAt { get; private set; }

    public DateTimeOffset? SuspendedAt { get; private set; }

    public static Institution Create(
        InstitutionId id,
        string code,
        string legalName,
        string countryCode,
        string baseCurrency,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(code);
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Institution code is required.", nameof(code));
        }

        ArgumentNullException.ThrowIfNull(legalName);
        if (string.IsNullOrWhiteSpace(legalName))
        {
            throw new ArgumentException("Institution legal name is required.", nameof(legalName));
        }

        ArgumentNullException.ThrowIfNull(countryCode);
        if (string.IsNullOrWhiteSpace(countryCode))
        {
            throw new ArgumentException("Country code is required.", nameof(countryCode));
        }

        ArgumentNullException.ThrowIfNull(baseCurrency);
        if (string.IsNullOrWhiteSpace(baseCurrency))
        {
            throw new ArgumentException("Base currency is required.", nameof(baseCurrency));
        }

        return new Institution(
            id,
            code.Trim().ToLowerInvariant(),
            legalName.Trim(),
            countryCode.Trim().ToUpperInvariant(),
            baseCurrency.Trim().ToUpperInvariant(),
            createdAt);
    }

    public void BeginProvisioning(TenantProvisioning provisioning, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(provisioning);

        if (Status != InstitutionStatus.Draft)
        {
            throw new InvalidOperationException($"Institution {Code} cannot start provisioning while in {Status} state.");
        }

        if (provisioning.InstitutionId != Id)
        {
            throw new InvalidOperationException("Provisioning plan belongs to a different institution.");
        }

        EnsureNotBeforeCreated(at);
        Status = InstitutionStatus.Provisioning;
        ProvisioningPlanVersion = provisioning.PlanVersion;
        ProvisioningStartedAt = at;
        LastProvisioningAttemptAt = at;
        UpdatedAt = at;
    }

    public void MarkReadyForActivation(TenantProvisioning provisioning, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(provisioning);

        if (Status != InstitutionStatus.Provisioning)
        {
            throw new InvalidOperationException($"Institution {Code} must be provisioning before it can be marked ready for activation.");
        }

        EnsureProvisioningMatches(provisioning);

        if (!provisioning.AllRequiredServicesInitialised)
        {
            throw new InvalidOperationException("All required service boundaries must be initialised before activation readiness can be recorded.");
        }

        EnsureNotBeforeLastUpdate(at);
        Status = InstitutionStatus.PendingActivation;
        ReadyForActivationAt = at;
        UpdatedAt = at;
    }

    public void FailProvisioning(TenantProvisioning provisioning, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(provisioning);

        if (Status != InstitutionStatus.Provisioning)
        {
            throw new InvalidOperationException($"Institution {Code} must be provisioning before a provisioning failure can be recorded.");
        }

        EnsureProvisioningMatches(provisioning);

        if (!provisioning.HasFailures)
        {
            throw new InvalidOperationException("A provisioning failure must be recorded for at least one required service.");
        }

        EnsureNotBeforeLastUpdate(at);
        Status = InstitutionStatus.ProvisioningFailed;
        ProvisioningFailedAt = at;
        UpdatedAt = at;
    }

    public void RetryProvisioning(TenantProvisioning provisioning, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(provisioning);

        if (Status != InstitutionStatus.ProvisioningFailed)
        {
            throw new InvalidOperationException($"Institution {Code} can only retry provisioning after a provisioning failure.");
        }

        EnsureProvisioningMatches(provisioning);

        EnsureNotBeforeLastUpdate(at);
        provisioning.ResetFailedServicesForRetry();
        Status = InstitutionStatus.Provisioning;
        ProvisioningFailedAt = null;
        LastProvisioningAttemptAt = at;
        UpdatedAt = at;
    }

    public void Activate(DateTimeOffset at)
    {
        if (Status != InstitutionStatus.PendingActivation)
        {
            throw new InvalidOperationException($"Institution {Code} must be pending activation before it can become active.");
        }

        EnsureNotBeforeLastUpdate(at);
        Status = InstitutionStatus.Active;
        ActivatedAt = at;
        UpdatedAt = at;
    }

    public void Suspend(DateTimeOffset at)
    {
        if (Status != InstitutionStatus.Active)
        {
            throw new InvalidOperationException($"Institution {Code} can only be suspended while active.");
        }

        EnsureNotBeforeLastUpdate(at);
        Status = InstitutionStatus.Suspended;
        SuspendedAt = at;
        UpdatedAt = at;
    }

    public void Reactivate(DateTimeOffset at)
    {
        if (Status != InstitutionStatus.Suspended)
        {
            throw new InvalidOperationException($"Institution {Code} can only be reactivated from suspended state.");
        }

        EnsureNotBeforeLastUpdate(at);
        Status = InstitutionStatus.Active;
        SuspendedAt = null;
        UpdatedAt = at;
    }

    private void EnsureProvisioningMatches(TenantProvisioning provisioning)
    {
        if (provisioning.InstitutionId != Id || provisioning.PlanVersion != ProvisioningPlanVersion)
        {
            throw new InvalidOperationException("Provisioning state does not match the institution's active provisioning plan.");
        }
    }

    private void EnsureNotBeforeCreated(DateTimeOffset at)
    {
        if (at < CreatedAt)
        {
            throw new InvalidOperationException("A lifecycle transition cannot occur before the institution was created.");
        }
    }

    private void EnsureNotBeforeLastUpdate(DateTimeOffset at)
    {
        if (at < UpdatedAt)
        {
            throw new InvalidOperationException("Institution lifecycle timestamps must be monotonic.");
        }
    }
}
