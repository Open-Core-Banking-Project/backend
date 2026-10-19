using System;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.CoreLedger.Contracts;
using Xunit;

namespace CoreBanking.CoreLedger.UnitTests;

public sealed class InstitutionActivationContractTests
{
    [Fact]
    public void Activated_event_preserves_approver_and_provisioning_evidence()
    {
        DateTimeOffset activatedAt = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        InstitutionActivatedV1 expected = new("institution-1", "run-1", 1, "approver-1", activatedAt);

        InstitutionActivatedV1? actual = EventContract.Deserialize<InstitutionActivatedV1>(
            EventContract.Serialize(expected));

        Assert.Equal(expected, actual);
    }
}
