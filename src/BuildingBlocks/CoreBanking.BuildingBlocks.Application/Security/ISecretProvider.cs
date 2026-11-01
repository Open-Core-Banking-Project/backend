using System.Threading;
using System.Threading.Tasks;

namespace CoreBanking.BuildingBlocks.Application.Security;

/// <summary>
/// The single access path to secret material. Services reference a secret by logical name and never read raw
/// environment variables, files or configuration keys for secret material.
/// Decision: ADR-0011 item 2.
/// </summary>
public interface ISecretProvider
{
    /// <summary>
    /// Reads one secret by logical name. A missing secret returns <see langword="null"/>; a provider failure
    /// throws rather than returning an empty value, so callers fail closed instead of falling back.
    /// </summary>
    Task<string?> TryReadAsync(string logicalName, CancellationToken cancellationToken);
}
