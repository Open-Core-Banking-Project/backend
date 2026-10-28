using System;
using System.Collections.Generic;
using CoreBanking.BuildingBlocks.Application.Persistence;

namespace CoreBanking.CustomerOnboarding.Infrastructure.Persistence;

/// <summary>
/// An onboarding application and its current captured state. The capture is the protected snapshot,
/// so the row is safe to read for staff display only through its masked values. Decision: CUS-UC-01.
/// </summary>
public sealed class OnboardingApplicationRecord : ITenantOwnedEntity
{
    public string InstitutionId { get; set; } = string.Empty;

    public string ApplicationId { get; set; } = string.Empty;

    public string ApplicationReference { get; set; } = string.Empty;

    public string ApplicationType { get; set; } = string.Empty;

    public string? BranchId { get; set; }

    public string Status { get; set; } = string.Empty;

    public int Revision { get; set; }

    public string CurrentCapture { get; set; } = string.Empty;

    public string CurrentPayloadHash { get; set; } = string.Empty;

    public string? PartyId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? SubmittedAt { get; set; }

    public DateTimeOffset? WithdrawnAt { get; set; }

    public long ConcurrencyVersion { get; set; }

    public ICollection<OnboardingApplicationRevisionRecord> Revisions { get; set; } = [];
}

/// <summary>
/// An immutable captured revision. Every revision carries its own payload hash, so a later change can
/// never be confused with what the applicant or officer actually confirmed.
/// </summary>
public sealed class OnboardingApplicationRevisionRecord : ITenantOwnedEntity
{
    public string InstitutionId { get; set; } = string.Empty;

    public string ApplicationId { get; set; } = string.Empty;

    public int Revision { get; set; }

    public string Status { get; set; } = string.Empty;

    public string PayloadHash { get; set; } = string.Empty;

    public string Capture { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public string RecordedBy { get; set; } = string.Empty;

    public DateTimeOffset RecordedAt { get; set; }

    public OnboardingApplicationRecord? Application { get; set; }
}
