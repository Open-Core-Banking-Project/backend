using System;

namespace CoreBanking.BuildingBlocks.Application.Messaging;

/// <summary>
/// Exponential backoff with jitter for outbox publication and consumer retry. The schedule is a
/// pure function of the attempt and a caller-supplied jitter source, so tests are deterministic.
/// Decision: ADR-0007.
/// </summary>
public static class RetrySchedule
{
    public const int DefaultMaximumAttempts = 10;

    /// <summary>The delay before the next attempt, given the attempt that just failed.</summary>
    public static TimeSpan DelayFor(
        int failedAttempt,
        TimeSpan baseDelay,
        TimeSpan maximumDelay,
        double jitter = 0d)
    {
        if (failedAttempt < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(failedAttempt), "Attempts start at 1.");
        }

        if (baseDelay <= TimeSpan.Zero || maximumDelay < baseDelay)
        {
            throw new ArgumentOutOfRangeException(nameof(baseDelay), "The base delay must be positive and no larger than the maximum delay.");
        }

        if (jitter is < 0d or > 1d)
        {
            throw new ArgumentOutOfRangeException(nameof(jitter), "Jitter is a fraction between 0 and 1.");
        }

        double growth = Math.Pow(2d, Math.Min(failedAttempt, 16) - 1);
        double scaled = Math.Min(baseDelay.TotalMilliseconds * growth, maximumDelay.TotalMilliseconds);
        double jittered = scaled * (1d - jitter);

        return TimeSpan.FromMilliseconds(Math.Round(jittered));
    }

    public static bool IsTerminal(int attempts, int maximumAttempts = DefaultMaximumAttempts) =>
        attempts >= maximumAttempts;
}
