using System;
using CoreBanking.CoreLedger.Domain.Ledger;
using Xunit;

namespace CoreBanking.CoreLedger.UnitTests;

public sealed class LedgerHoldTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Capture_and_release_preserve_the_original_amount()
    {
        LedgerHold hold = LedgerHold.Create(
            "hold-1",
            "ledger-account-1",
            Money.Create(100m, "NGN"),
            Now.AddHours(1),
            Now);

        hold.Capture(40m, Now.AddMinutes(1));
        hold.Release(60m, Now.AddMinutes(2));

        Assert.Equal(0m, hold.RemainingAmount);
        Assert.Equal(40m, hold.CapturedAmount);
        Assert.Equal(60m, hold.ReleasedAmount);
        Assert.Equal(LedgerHoldStatus.Released, hold.Status);
        Assert.Equal(3, hold.Version);
    }

    [Fact]
    public void Capture_cannot_exceed_the_active_remainder()
    {
        LedgerHold hold = LedgerHold.Create(
            "hold-2",
            "ledger-account-2",
            Money.Create(25m, "NGN"),
            Now.AddHours(1),
            Now);

        LedgerPostingException exception = Assert.Throws<LedgerPostingException>(() =>
            hold.Capture(25.0001m, Now.AddMinutes(1)));

        Assert.Equal("ledger_hold_capture_exceeds_remaining", exception.Code);
        Assert.Equal(25m, hold.RemainingAmount);
    }

    [Fact]
    public void Expiry_is_idempotent_after_the_terminal_transition()
    {
        LedgerHold hold = LedgerHold.Create(
            "hold-3",
            "ledger-account-3",
            Money.Create(30m, "NGN"),
            Now.AddMinutes(5),
            Now);

        Assert.Equal(30m, hold.Expire(Now.AddMinutes(5)));
        Assert.Equal(0m, hold.Expire(Now.AddMinutes(6)));
        Assert.Equal(LedgerHoldStatus.Expired, hold.Status);
        Assert.Equal(2, hold.Version);
    }
}
