using System;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CoreLedger.Domain.Institutions;
using Xunit;

namespace CoreBanking.CoreLedger.UnitTests;

public sealed class OrganisationLifecycleTests
{
    private static readonly InstitutionId InstitutionId = InstitutionId.From("institution-1");
    private static readonly DateTimeOffset CreatedAt = new(2026, 10, 19, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Region_lifecycle_is_controlled_and_closed_is_terminal()
    {
        Region region = Region.Create("region-1", InstitutionId, " NORTH ", "North", CreatedAt);
        Assert.Equal("north", region.Code);
        Assert.Equal(RegionStatus.Draft, region.Status);

        region.Activate(CreatedAt.AddHours(1));
        region.Suspend(CreatedAt.AddHours(2));
        region.Close(CreatedAt.AddHours(3));

        Assert.Equal(RegionStatus.Closed, region.Status);
        Assert.Throws<InvalidOperationException>(() => region.Activate(CreatedAt.AddHours(4)));
    }

    [Fact]
    public void Branch_must_be_suspended_before_closure_and_can_be_reactivated()
    {
        Branch branch = Branch.Create("branch-1", InstitutionId, "region-1", " HQ ", "Head Office", CreatedAt);
        branch.Activate(CreatedAt.AddHours(1));
        Assert.Throws<InvalidOperationException>(() => branch.Close(CreatedAt.AddHours(2)));

        branch.Suspend(CreatedAt.AddHours(2));
        branch.Activate(CreatedAt.AddHours(3));
        branch.Suspend(CreatedAt.AddHours(4));
        branch.Close(CreatedAt.AddHours(5));

        Assert.Equal(BranchStatus.Closed, branch.Status);
        Assert.Equal("hq", branch.Code);
    }

    [Fact]
    public void Lifecycle_timestamps_are_monotonic()
    {
        Region region = Region.Create("region-1", InstitutionId, "north", "North", CreatedAt);
        Assert.Throws<InvalidOperationException>(() => region.Activate(CreatedAt.AddMinutes(-1)));
    }
}
