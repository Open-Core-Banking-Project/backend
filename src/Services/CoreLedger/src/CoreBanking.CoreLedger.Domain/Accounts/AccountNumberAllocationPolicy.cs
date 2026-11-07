using System;
using System.Collections.Generic;

namespace CoreBanking.CoreLedger.Domain.Accounts;

/// <summary>
/// The durable allocation state of one scheme instance. <see cref="NextValue"/> is the only source of
/// the next number: allocation never derives a value from the highest stored account number.
/// Decision: P1-E04-D06.
/// </summary>
public sealed record AccountNumberAllocationState(
    long NextValue,
    long HighWaterMark,
    long Version);

/// <summary>One allocation attempt: the sequences granted and the state that must be persisted.</summary>
public sealed record AccountNumberAllocation(
    IReadOnlyList<long> Sequences,
    AccountNumberAllocationState State,
    int DepletionBand);

public static class AccountNumberAllocationPolicy
{
    public const string ExhaustedCode = "account_number_scheme_exhausted";
    public const string RequestInvalidCode = "account_number_request_invalid";

    /// <summary>
    /// Grants the requested count from the stored next value, under the caller's row lock. Exhaustion
    /// refuses the whole request and leaves the state untouched so no partial account is written.
    /// Decision: P1-E04-D06.
    /// </summary>
    public static AccountNumberAllocation Allocate(
        AccountNumberAllocationState state,
        long count,
        long capacity)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentOutOfRangeException.ThrowIfNegative(state.NextValue);
        ArgumentOutOfRangeException.ThrowIfNegative(state.HighWaterMark);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        if (count < 1)
        {
            throw new InvalidOperationException(RequestInvalidCode);
        }

        if (state.NextValue < state.HighWaterMark)
        {
            // A released value is never reissued, so the next value always leads the high-water mark.
            throw new InvalidOperationException("account_number_state_invalid");
        }

        long remaining = capacity - state.NextValue;
        if (count > remaining)
        {
            throw new InvalidOperationException(ExhaustedCode);
        }

        List<long> sequences = [];
        for (long offset = 0; offset < count; offset++)
        {
            sequences.Add(state.NextValue + offset);
        }

        long next = state.NextValue + count;
        AccountNumberAllocationState updated = new(next, next, state.Version + 1);
        return new AccountNumberAllocation(sequences, updated, DepletionBand(updated, capacity));
    }

    /// <summary>
    /// The alerting band of the allocation state: the highest of 80, 90 or 95 percent reached, or zero
    /// while the scheme is comfortably below the first threshold. Decision: P1-E04-D06.
    /// </summary>
    public static int DepletionBand(AccountNumberAllocationState state, long capacity)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        double used = (double)state.HighWaterMark / capacity;
        return used switch
        {
            >= 0.95 => 95,
            >= 0.90 => 90,
            >= 0.80 => 80,
            _ => 0,
        };
    }

    /// <summary>Numbers still issuable under this scheme instance.</summary>
    public static long Available(AccountNumberAllocationState state, long capacity)
    {
        ArgumentNullException.ThrowIfNull(state);
        return Math.Max(0, capacity - state.NextValue);
    }
}
