using System;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.BuildingBlocks.Domain.Identity;

namespace CoreBanking.IdentityAccess.Application.Authorisation;

/// <summary>
/// Evidence for one authorisation decision. It carries identifiers and the decision only:
/// no credentials, tokens, names, or other personal data.
/// Decision: docs/adr/ADR-0010-observability-audit-evidence-redaction-and-retention.md
/// </summary>
public sealed record AuthorisationAuditEntry
{
    public required string AuditEventId { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>Absent when authentication did not succeed.</summary>
    public ActorId? ActorId { get; init; }

    public ActorType? ActorType { get; init; }

    /// <summary>Absent when no institution context could be resolved.</summary>
    public InstitutionId? InstitutionId { get; init; }

    /// <summary>The permission the operation required, in domain.resource.action form.</summary>
    public required string Permission { get; init; }

    public required string Action { get; init; }

    public required string Decision { get; init; }

    /// <summary>Stable code for a denial, or <c>none</c> for an allowance.</summary>
    public required string Reason { get; init; }

    public required string Source { get; init; }

    public required string CorrelationId { get; init; }

    public long? EntitlementVersion { get; init; }

    public string? SessionId { get; init; }

    public string? DeviceId { get; init; }

    public string? PolicyVersion { get; init; }
}

/// <summary>
/// Writes authorisation evidence. Records are append-only through ordinary application
/// paths and cannot be edited or deleted by an ordinary administrator.
/// </summary>
public interface IAuthorisationAuditWriter
{
    Task WriteAsync(AuthorisationAuditEntry entry, CancellationToken cancellationToken);
}
