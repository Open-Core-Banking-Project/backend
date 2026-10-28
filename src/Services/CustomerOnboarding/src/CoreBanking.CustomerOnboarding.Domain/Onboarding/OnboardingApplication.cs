using System;
using System.Collections.Generic;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CustomerOnboarding.Domain.Parties;

namespace CoreBanking.CustomerOnboarding.Domain.Onboarding;

/// <summary>Onboarding application lifecycle. Decision: application lifecycle, slice 4.</summary>
public enum ApplicationStatus
{
    Draft,
    Submitted,
    Withdrawn,
    Expired,
    Rejected,
    Approved,
    Completed,
}

/// <summary>
/// A complete or partial individual capture before protection. The record is the request-side value
/// object: it validates what was typed and is never persisted as plaintext. Decision: CUS-028.
/// </summary>
public sealed record OnboardingCapture(
    PersonName? LegalName,
    BirthDetails? BirthDetails,
    IReadOnlyList<ContactPoint> Contacts,
    IReadOnlyList<Address> Addresses)
{
    public static OnboardingCapture Empty { get; } = new(null, null, [], []);

    /// <summary>The field groups still missing, empty when the capture is complete.</summary>
    public IReadOnlyList<string> MissingFields()
    {
        List<string> missing = [];
        if (LegalName is null)
        {
            missing.Add("legalName");
        }

        if (BirthDetails is null)
        {
            missing.Add("birthDate");
        }

        if (Contacts.Count == 0)
        {
            missing.Add("contacts");
        }

        if (Addresses.Count == 0)
        {
            missing.Add("addresses");
        }

        return missing;
    }

    public bool IsComplete => MissingFields().Count == 0;
}

/// <summary>
/// Application identity and lifecycle. The captured values are deliberately not part of this
/// aggregate: they are protected snapshots with their own immutability rules, so lifecycle code can
/// never read or rewrite personal data. Decision: invariant 3, CUS-UC-01.
/// </summary>
public sealed class OnboardingApplication
{
    private OnboardingApplication(
        InstitutionId institutionId,
        string applicationId,
        string applicationReference,
        string applicationType,
        string? branchId,
        ApplicationStatus status,
        int revision,
        string? partyId,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        DateTimeOffset? submittedAt,
        DateTimeOffset? withdrawnAt,
        long concurrencyVersion)
    {
        InstitutionId = institutionId;
        ApplicationId = applicationId;
        ApplicationReference = applicationReference;
        ApplicationType = applicationType;
        BranchId = branchId;
        Status = status;
        Revision = revision;
        PartyId = partyId;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        SubmittedAt = submittedAt;
        WithdrawnAt = withdrawnAt;
        ConcurrencyVersion = concurrencyVersion;
    }

    public InstitutionId InstitutionId { get; }

    public string ApplicationId { get; }

    /// <summary>Institution-unique human reference shown to staff and to the applicant.</summary>
    public string ApplicationReference { get; }

    public string ApplicationType { get; }

    public string? BranchId { get; }

    public ApplicationStatus Status { get; private set; }

    /// <summary>Current revision number. Revision 1 is the capture recorded when the application started.</summary>
    public int Revision { get; private set; }

    public string? PartyId { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? SubmittedAt { get; private set; }

    public DateTimeOffset? WithdrawnAt { get; private set; }

    public long ConcurrencyVersion { get; private set; }

    public static OnboardingApplication Start(
        InstitutionId institutionId,
        string applicationReference,
        string? branchId,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationReference);

        return new OnboardingApplication(
            institutionId,
            Guid.NewGuid().ToString("N"),
            applicationReference.Trim(),
            "individual",
            string.IsNullOrWhiteSpace(branchId) ? null : branchId.Trim(),
            ApplicationStatus.Draft,
            revision: 1,
            partyId: null,
            now,
            now,
            submittedAt: null,
            withdrawnAt: null,
            concurrencyVersion: 1L);
    }

    /// <summary>Records a new capture revision of a draft application.</summary>
    public void Revise(DateTimeOffset now)
    {
        if (Status != ApplicationStatus.Draft)
        {
            throw new OnboardingRuleException("onboarding_revision_conflict", "Only a draft application can be revised.");
        }

        Revision++;
        UpdatedAt = now;
        ConcurrencyVersion++;
    }

    /// <summary>
    /// Submits the exact revision the caller confirmed. Completeness is checked against the protected
    /// snapshot by the caller, because this aggregate never holds personal data.
    /// </summary>
    public void Submit(string partyId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(partyId);

        if (Status != ApplicationStatus.Draft)
        {
            throw new OnboardingRuleException("onboarding_revision_conflict", "Only a draft application can be submitted.");
        }

        Status = ApplicationStatus.Submitted;
        PartyId = partyId;
        Revision++;
        SubmittedAt = now;
        UpdatedAt = now;
        ConcurrencyVersion++;
    }

    /// <summary>A draft or submitted application can be withdrawn; a terminal application cannot.</summary>
    public void Withdraw(DateTimeOffset now)
    {
        if (Status is not (ApplicationStatus.Draft or ApplicationStatus.Submitted))
        {
            throw new OnboardingRuleException("onboarding_revision_conflict", "Only a draft or submitted application can be withdrawn.");
        }

        Status = ApplicationStatus.Withdrawn;
        WithdrawnAt = now;
        Revision++;
        UpdatedAt = now;
        ConcurrencyVersion++;
    }

    /// <summary>Rehydrates a persisted application. Used by the persistence layer only.</summary>
    public static OnboardingApplication Restore(
        InstitutionId institutionId,
        string applicationId,
        string applicationReference,
        string applicationType,
        string? branchId,
        ApplicationStatus status,
        int revision,
        string? partyId,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        DateTimeOffset? submittedAt,
        DateTimeOffset? withdrawnAt,
        long concurrencyVersion) =>
        new(institutionId, applicationId, applicationReference, applicationType, branchId, status, revision, partyId, createdAt, updatedAt, submittedAt, withdrawnAt, concurrencyVersion);
}
