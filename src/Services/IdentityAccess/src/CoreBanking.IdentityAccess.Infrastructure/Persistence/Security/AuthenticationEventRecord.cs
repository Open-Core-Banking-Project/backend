using System;
using CoreBanking.BuildingBlocks.Application.Persistence;

namespace CoreBanking.IdentityAccess.Infrastructure.Persistence.Security;

/// <summary>
/// An authentication attempt, kept as the evidence ADR-0010 requires. It records the outcome and
/// never the credential, the token or any other secret material.
/// Table: <c>security.authentication_events</c>. Decision: ADR-0010.
/// </summary>
public sealed class AuthenticationEventRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }

    public required string AuthenticationEventId { get; set; }

    public string? IdentityId { get; set; }

    public required string EventType { get; set; }

    public AuthenticationOutcome Outcome { get; set; }

    public string? Detail { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
}
