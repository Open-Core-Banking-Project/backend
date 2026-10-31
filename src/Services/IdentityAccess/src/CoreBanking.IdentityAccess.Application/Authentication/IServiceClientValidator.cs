using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Security;

namespace CoreBanking.IdentityAccess.Application.Authentication;

/// <summary>
/// Confirms that a validated service token belongs to an active, unexpired service client. It runs
/// after token validation and before authorisation, so an unregistered or expired client can never
/// reach a protected operation. Decision: ADR-0008; slice 13.
/// </summary>
public interface IServiceClientValidator
{
    Task<bool> IsUsableAsync(AuthenticatedActor actor, CancellationToken cancellationToken);
}
