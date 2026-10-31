using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;

namespace CoreBanking.CustomerOnboarding.Application.Customers;

/// <summary>Section availability. An unavailable section is reported, never fabricated. Decision: P1-E03-D13.</summary>
public enum Customer360SectionStatus
{
    Available,
    Unavailable,
}

/// <summary>
/// Requests a bounded set of Customer 360 sections. An empty set returns nothing rather than everything.
/// Decision: P1-E03-D13.
/// </summary>
public sealed record GetCustomer360Query(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string CustomerId,
    IReadOnlyList<string> Sections);

/// <summary>
/// One section of the composition: its status, its owning source, when it was read, the permission it needs
/// and only masked or aggregate values. Decision: P1-E03-D13.
/// </summary>
public sealed record Customer360SectionView(
    string Section,
    Customer360SectionStatus Status,
    string StatusCode,
    string Source,
    string RequiredPermission,
    DateTimeOffset? AsOf,
    string? ReasonCode,
    IReadOnlyDictionary<string, string> Fields);

public sealed record Customer360View(
    string CustomerId,
    DateTimeOffset AssembledAt,
    IReadOnlyList<Customer360SectionView> Sections);

public interface ICustomer360Service
{
    Task<Customer360View> GetAsync(GetCustomer360Query query, CancellationToken cancellationToken);
}
