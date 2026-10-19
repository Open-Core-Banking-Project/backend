using System;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;

namespace CoreBanking.CoreLedger.Application.Institutions;

public sealed record SuspendInstitutionCommand(InstitutionId InstitutionId, ActorId ActorId, string IdempotencyKey, string Reason, string CorrelationId);
public sealed record SuspendInstitutionResult(string InstitutionId, string EventId, DateTimeOffset SuspendedAt, bool Replayed);

public interface IInstitutionSuspensionService
{
    Task<SuspendInstitutionResult> SuspendAsync(SuspendInstitutionCommand command, CancellationToken cancellationToken);
}

public static class InstitutionSuspensionPolicy
{
    public static void EnsureIndependentApprover(string? lastLifecycleActorId, ActorId suspensionApproverId)
    {
        ArgumentNullException.ThrowIfNull(suspensionApproverId);
        if (string.IsNullOrWhiteSpace(lastLifecycleActorId))
        {
            throw new InstitutionSuspensionRejectedException("institution_suspension_evidence_missing", "The institution has no trusted activation evidence and cannot be suspended.");
        }

        if (string.Equals(lastLifecycleActorId, suspensionApproverId.Value, StringComparison.Ordinal))
        {
            throw new InstitutionSuspensionRejectedException("maker_cannot_approve_own_change", "The actor that last activated the institution cannot approve its suspension.");
        }
    }
}

public sealed class InstitutionSuspensionRejectedException : Exception
{
    public InstitutionSuspensionRejectedException(string code, string message) : base(message) => Code = code;
    public InstitutionSuspensionRejectedException(string code, string message, Exception inner) : base(message, inner) => Code = code;
    public string Code { get; }
}

public sealed class InstitutionSuspensionInProgressException : Exception
{
    public InstitutionSuspensionInProgressException(string message) : base(message) { }
}
