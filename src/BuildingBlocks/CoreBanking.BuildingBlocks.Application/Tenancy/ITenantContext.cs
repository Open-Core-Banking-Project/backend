using System;
using CoreBanking.BuildingBlocks.Domain.Identity;

namespace CoreBanking.BuildingBlocks.Application.Tenancy;

/// <summary>
/// The trusted institution context of the current operation. It is established only from a
/// validated authentication context and is the single source the persistence layer uses to
/// constrain tenant-owned queries. There is no fallback institution: an operation without an
/// established context reads nothing.
/// Decision: docs/adr/ADR-0004-database-per-service-and-tenant-isolation.md
/// </summary>
public interface ITenantContext
{
    /// <summary>The established institution, or <c>null</c> when the operation is not tenant-scoped.</summary>
    InstitutionId? CurrentInstitution { get; }

    /// <summary>
    /// Establishes the institution for the current operation. Establishing a second, different
    /// institution in the same operation is a conflict and fails closed.
    /// </summary>
    void Establish(InstitutionId institutionId);

    /// <summary>Clears the context once the operation completes.</summary>
    void Clear();
}

/// <summary>Raised when an operation tries to switch tenant inside one unit of work.</summary>
public sealed class TenantContextConflictException : InvalidOperationException
{
    public TenantContextConflictException()
        : base("The operation already has a different institution context; a second tenant cannot be established.")
    {
    }

    public TenantContextConflictException(string message)
        : base(message)
    {
    }

    public TenantContextConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
