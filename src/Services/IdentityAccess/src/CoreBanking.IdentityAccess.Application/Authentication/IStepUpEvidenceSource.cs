using System;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Security;

namespace CoreBanking.IdentityAccess.Application.Authentication;

/// <summary>
/// Answers whether a protected operation has fresh, purpose-bound, single-use step-up evidence for
/// the current actor and session, and consumes it when it does. Absence is a denial.
/// Decision: ADR-0008 and P1-E02-D08.
/// </summary>
public interface IStepUpEvidenceSource
{
    Task<bool> TryConsumeAsync(AuthenticatedActor actor, string purpose, CancellationToken cancellationToken);
}
