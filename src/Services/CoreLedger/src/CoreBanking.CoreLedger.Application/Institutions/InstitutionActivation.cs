using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;

namespace CoreBanking.CoreLedger.Application.Institutions;

public sealed record ActivateInstitutionCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string CorrelationId);

public sealed record ActivateInstitutionResult(
    string InstitutionId,
    string ProvisioningRunId,
    string EventId,
    DateTimeOffset ActivatedAt,
    bool Replayed);

public interface IInstitutionActivationService
{
    Task<ActivateInstitutionResult> ActivateAsync(
        ActivateInstitutionCommand command,
        CancellationToken cancellationToken);
}

public static class InstitutionActivationPolicy
{
    public static void EnsureIndependentApprover(string? makerActorId, ActorId approverActorId)
    {
        ArgumentNullException.ThrowIfNull(approverActorId);
        if (string.IsNullOrWhiteSpace(makerActorId))
        {
            throw new InstitutionActivationRejectedException(
                "institution_activation_evidence_missing",
                "The institution has no trusted maker evidence and cannot be activated.");
        }

        if (string.Equals(makerActorId, approverActorId.Value, StringComparison.Ordinal))
        {
            throw new InstitutionActivationRejectedException(
                "maker_cannot_approve_own_change",
                "The actor that created the institution cannot activate it.");
        }
    }

    public static void EnsureProvisioningComplete(string institutionStatus, IEnumerable<string> stepStatuses)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(institutionStatus);
        ArgumentNullException.ThrowIfNull(stepStatuses);
        string[] statuses = stepStatuses.ToArray();
        if (!(string.Equals(institutionStatus, "pending_activation", StringComparison.Ordinal)
                || string.Equals(institutionStatus, "suspended", StringComparison.Ordinal))
            || statuses.Length == 0
            || statuses.Any(status => !string.Equals(status, "initialised", StringComparison.Ordinal)))
        {
            throw new InstitutionActivationRejectedException(
                "institution_not_provisioned",
                "All mandatory service boundaries must be initialised before activation.");
        }
    }
}

public sealed class InstitutionActivationRejectedException : Exception
{
    public InstitutionActivationRejectedException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public InstitutionActivationRejectedException(string code, string message, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}

public sealed class InstitutionActivationInProgressException : Exception
{
    public InstitutionActivationInProgressException(string message)
        : base(message)
    {
    }
}
