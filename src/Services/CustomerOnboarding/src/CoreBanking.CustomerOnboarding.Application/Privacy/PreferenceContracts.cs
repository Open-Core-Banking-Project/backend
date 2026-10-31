using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CustomerOnboarding.Domain.Privacy;

namespace CoreBanking.CustomerOnboarding.Application.Privacy;

/// <summary>
/// Records one preference decision for a customer, purpose and channel. Repeating the current state is
/// idempotent; a change closes the current version and appends the next. Decision: P1-E03-D12.
/// </summary>
public sealed record SetCustomerPreferenceCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string CustomerId,
    string Purpose,
    string Channel,
    PreferenceState State,
    string? LawfulBasis,
    string Source,
    string? EvidenceReference,
    string? Reason,
    string CorrelationId);

public sealed record GetCustomerPreferencesQuery(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string CustomerId);

public sealed record ServicePreferenceView(
    string PreferenceId,
    string Purpose,
    string Channel,
    int Version,
    PreferenceState State,
    string StateCode,
    string? LawfulBasis,
    string Source,
    string? EvidenceReference,
    string? Reason,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    bool IsCurrent);

public sealed record ConsentEvidenceView(
    string ConsentId,
    string Purpose,
    string Channel,
    int Version,
    string StateCode,
    string? LawfulBasis,
    string Source,
    string? EvidenceReference,
    string RecordedBy,
    DateTimeOffset RecordedAt,
    DateTimeOffset? WithdrawnAt);

public sealed record CustomerPreferencesView(
    string CustomerId,
    IReadOnlyList<ServicePreferenceView> Preferences,
    IReadOnlyList<ConsentEvidenceView> ConsentHistory);

public interface ICustomerPreferencesService
{
    Task<CustomerPreferencesView> SetAsync(SetCustomerPreferenceCommand command, CancellationToken cancellationToken);

    Task<CustomerPreferencesView> GetAsync(GetCustomerPreferencesQuery query, CancellationToken cancellationToken);
}
