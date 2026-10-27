using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;

namespace CoreBanking.CoreLedger.Application.Institutions;

public sealed record RepairInstitutionProvisioningCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string ProvisioningRunId,
    string Reason,
    string CorrelationId);

public sealed record InstitutionProvisioningStepStatus(
    string ServiceId,
    string Status,
    string? FailureCode,
    DateTimeOffset UpdatedAt);

public sealed record InstitutionProvisioningStatusResult(
    string InstitutionId,
    string ProvisioningRunId,
    int PlanVersion,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<InstitutionProvisioningStepStatus> Steps);

public sealed record RepairInstitutionProvisioningResult(
    string InstitutionId,
    string ProvisioningRunId,
    string SourceEventId,
    string AuditEventId,
    DateTimeOffset RequestedAt,
    bool Replayed);

public interface IInstitutionProvisioningRepairService
{
    Task<InstitutionProvisioningStatusResult> GetStatusAsync(
        InstitutionId institutionId,
        string provisioningRunId,
        CancellationToken cancellationToken);

    Task<RepairInstitutionProvisioningResult> RepairAsync(
        RepairInstitutionProvisioningCommand command,
        CancellationToken cancellationToken);
}

public static class InstitutionProvisioningRepairPolicy
{
    public static void EnsureReplayEligible(string runStatus, IEnumerable<string> stepStatuses)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runStatus);
        ArgumentNullException.ThrowIfNull(stepStatuses);
        string[] statuses = stepStatuses.ToArray();
        if (statuses.Any(status => string.Equals(status, "failed", StringComparison.Ordinal)))
        {
            throw new InstitutionProvisioningRepairRejectedException(
                "downstream_repair_required",
                "A failed service boundary requires its owning service's approved repair before provisioning can resume.");
        }

        if (!string.Equals(runStatus, "provisioning", StringComparison.Ordinal)
            || !statuses.Any(status => string.Equals(status, "pending", StringComparison.Ordinal)))
        {
            throw new InstitutionProvisioningRepairRejectedException(
                "provisioning_repair_not_eligible",
                "Only a provisioning run with incomplete pending service boundaries can be replayed.");
        }
    }
}

public sealed class InstitutionProvisioningRepairRejectedException : Exception
{
    public InstitutionProvisioningRepairRejectedException(string code, string message)
        : base(message) => Code = code;

    public InstitutionProvisioningRepairRejectedException(string code, string message, Exception innerException)
        : base(message, innerException) => Code = code;

    public string Code { get; }
}

public sealed class InstitutionProvisioningRepairInProgressException : Exception
{
    public InstitutionProvisioningRepairInProgressException(string message)
        : base(message)
    {
    }
}
