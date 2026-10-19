using System;
using CoreBanking.BuildingBlocks.Domain.Identity;

namespace CoreBanking.CoreLedger.Domain.Institutions;

public enum BranchStatus
{
    Draft,
    Active,
    Suspended,
    Closed,
}

public sealed class Branch
{
    private Branch(string id, InstitutionId institutionId, string regionId, string code, string name, DateTimeOffset createdAt)
    {
        Id = id;
        InstitutionId = institutionId;
        RegionId = regionId;
        Code = code;
        Name = name;
        Status = BranchStatus.Draft;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public string Id { get; }
    public InstitutionId InstitutionId { get; }
    public string RegionId { get; }
    public string Code { get; }
    public string Name { get; }
    public BranchStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static Branch Create(string id, InstitutionId institutionId, string regionId, string code, string name, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(regionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new Branch(id.Trim(), institutionId, regionId.Trim(), code.Trim().ToLowerInvariant(), name.Trim(), createdAt);
    }

    public void Activate(DateTimeOffset at) => Transition(BranchStatus.Active, at, BranchStatus.Draft, BranchStatus.Suspended);
    public void Suspend(DateTimeOffset at) => Transition(BranchStatus.Suspended, at, BranchStatus.Active);
    public void Close(DateTimeOffset at) => Transition(BranchStatus.Closed, at, BranchStatus.Suspended);

    private void Transition(BranchStatus target, DateTimeOffset at, params BranchStatus[] allowed)
    {
        if (Array.IndexOf(allowed, Status) < 0)
        {
            throw new InvalidOperationException($"Branch {Code} cannot transition from {Status} to {target}.");
        }

        if (at < UpdatedAt)
        {
            throw new InvalidOperationException("Branch lifecycle timestamps must be monotonic.");
        }

        Status = target;
        UpdatedAt = at;
    }
}
