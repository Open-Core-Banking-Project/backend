using System.Threading;
using System.Threading.Tasks;

namespace CoreBanking.BuildingBlocks.Application.Security;

/// <summary>
/// Supplies a short-lived provider-issued access token for an outbound workload call. Implementations
/// fail closed when provider configuration or credential material is unavailable.
/// Decision: ADR-0008; P1-E02-S13.
/// </summary>
public interface IWorkloadAccessTokenSource
{
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken);
}
