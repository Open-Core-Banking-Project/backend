using System;
using CoreBanking.BuildingBlocks.Domain.Identity;

namespace CoreBanking.CoreLedger.Domain.Institutions;

public enum RegionStatus
{
    Draft,
    Active,
    Suspended,
    Closed,
}

public sealed class Region
{
    private Region(string id, InstitutionId institutionId, string code, string name, DateTimeOffset createdAt)
    {
        Id = id;
        InstitutionId = institutionId;
        Code = code;
        Name = name;
        Status = RegionStatus.Draft;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public string Id { get; }
    public InstitutionId InstitutionId { get; }
    public string Code { get; }
    public string Name { get; }
    public RegionStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static Region Create(string id, InstitutionId institutionId, string code, string name, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new Region(id.Trim(), institutionId, code.Trim().ToLowerInvariant(), name.Trim(), createdAt);
    }

    public void Activate(DateTimeOffset at) => Transition(RegionStatus.Active, at, RegionStatus.Draft, RegionStatus.Suspended);
    public void Suspend(DateTimeOffset at) => Transition(RegionStatus.Suspended, at, RegionStatus.Active);
    public void Close(DateTimeOffset at) => Transition(RegionStatus.Closed, at, RegionStatus.Draft, RegionStatus.Suspended);

    private void Transition(RegionStatus target, DateTimeOffset at, params RegionStatus[] allowed)
    {
        if (Array.IndexOf(allowed, Status) < 0)
        {
            throw new InvalidOperationException($"Region {Code} cannot transition from {Status} to {target}.");
        }

        if (at < UpdatedAt)
        {
            throw new InvalidOperationException("Region lifecycle timestamps must be monotonic.");
        }

        Status = target;
        UpdatedAt = at;
    }
}
