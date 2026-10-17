using System;
using System.Threading;
using CoreBanking.BuildingBlocks.Application.Tenancy;
using CoreBanking.BuildingBlocks.Domain.Identity;

namespace CoreBanking.BuildingBlocks.Infrastructure.Tenancy;

/// <summary>
/// Carries the trusted institution for one asynchronous operation. It flows with the execution
/// context so background work started inside a request inherits the institution, while a
/// parallel request cannot observe another one. Registered as a singleton because the state
/// lives in the execution context, not in the instance.
/// Decision: docs/adr/ADR-0004-database-per-service-and-tenant-isolation.md
/// </summary>
public sealed class AmbientTenantContext : ITenantContext
{
    private readonly AsyncLocal<InstitutionId?> _current = new();

    public InstitutionId? CurrentInstitution => _current.Value;

    public void Establish(InstitutionId institutionId)
    {
        ArgumentNullException.ThrowIfNull(institutionId);

        InstitutionId? current = _current.Value;
        if (current is not null)
        {
            if (current != institutionId)
            {
                throw new TenantContextConflictException(
                    $"The operation is already scoped to institution '{current.Value}' and cannot also be scoped to '{institutionId.Value}'.");
            }

            return;
        }

        _current.Value = institutionId;
    }

    public void Clear() => _current.Value = null;
}
