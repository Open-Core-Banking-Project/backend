using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CoreBanking.BuildingBlocks.Domain.Identity;

namespace CoreBanking.CoreLedger.Domain.Institutions;

public sealed class TenantProvisioning
{
    private readonly Dictionary<string, ProvisioningServiceStatus> _serviceStatuses;
    private readonly Dictionary<string, string> _failureCodes = new(StringComparer.Ordinal);
    private readonly ReadOnlyDictionary<string, ProvisioningServiceStatus> _readOnlyServiceStatuses;
    private readonly ReadOnlyDictionary<string, string> _readOnlyFailureCodes;

    private TenantProvisioning(
        InstitutionId institutionId,
        int planVersion,
        Dictionary<string, ProvisioningServiceStatus> serviceStatuses)
    {
        InstitutionId = institutionId;
        PlanVersion = planVersion;
        _serviceStatuses = serviceStatuses;
        _readOnlyServiceStatuses = new ReadOnlyDictionary<string, ProvisioningServiceStatus>(_serviceStatuses);
        _readOnlyFailureCodes = new ReadOnlyDictionary<string, string>(_failureCodes);
    }

    public InstitutionId InstitutionId { get; }

    public int PlanVersion { get; }

    public IReadOnlyDictionary<string, ProvisioningServiceStatus> ServiceStatuses => _readOnlyServiceStatuses;

    public IReadOnlyDictionary<string, string> FailureCodes => _readOnlyFailureCodes;

    public bool HasFailures => _failureCodes.Count > 0;

    public bool AllRequiredServicesInitialised =>
        _serviceStatuses.Count > 0 && _serviceStatuses.Values.All(status => status == ProvisioningServiceStatus.Initialised);

    public static TenantProvisioning Create(
        InstitutionId institutionId,
        int planVersion,
        IEnumerable<string> requiredServices)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        string[] normalizedServices = ValidatePlan(planVersion, requiredServices);

        Dictionary<string, ProvisioningServiceStatus> serviceStatuses = normalizedServices
            .ToDictionary(service => service, _ => ProvisioningServiceStatus.Pending, StringComparer.Ordinal);

        return new TenantProvisioning(institutionId, planVersion, serviceStatuses);
    }

    public static string[] ValidatePlan(int planVersion, IEnumerable<string> requiredServices)
    {
        ArgumentNullException.ThrowIfNull(requiredServices);

        if (planVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(planVersion), "A provisioning plan version starts at 1.");
        }

        HashSet<string> services = new(StringComparer.Ordinal);
        foreach (string requiredService in requiredServices)
        {
            string service = NormalizeServiceId(requiredService);
            if (!services.Add(service))
            {
                throw new ArgumentException($"Required service '{service}' is listed more than once.", nameof(requiredServices));
            }
        }

        if (services.Count == 0)
        {
            throw new ArgumentException("At least one required provisioning service must be configured.", nameof(requiredServices));
        }

        return services.ToArray();
    }

    public void RecordInitialised(string serviceId)
    {
        string service = GetRequiredService(serviceId);
        _serviceStatuses[service] = ProvisioningServiceStatus.Initialised;
        _failureCodes.Remove(service);
    }

    public void RecordFailure(string serviceId, string failureCode)
    {
        string service = GetRequiredService(serviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);

        if (_serviceStatuses[service] == ProvisioningServiceStatus.Initialised)
        {
            throw new InvalidOperationException($"Service '{service}' has already acknowledged initialisation.");
        }

        _serviceStatuses[service] = ProvisioningServiceStatus.Failed;
        _failureCodes[service] = failureCode.Trim();
    }

    internal void ResetFailedServicesForRetry()
    {
        foreach (string service in _failureCodes.Keys)
        {
            _serviceStatuses[service] = ProvisioningServiceStatus.Pending;
        }

        _failureCodes.Clear();
    }

    private string GetRequiredService(string serviceId)
    {
        string service = NormalizeServiceId(serviceId);
        if (!_serviceStatuses.ContainsKey(service))
        {
            throw new InvalidOperationException($"Service '{service}' is not part of provisioning plan version {PlanVersion}.");
        }

        return service;
    }

    private static string NormalizeServiceId(string serviceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceId);
        return serviceId.Trim().ToLowerInvariant();
    }
}
