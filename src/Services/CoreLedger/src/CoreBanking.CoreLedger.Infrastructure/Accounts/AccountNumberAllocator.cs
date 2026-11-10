using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CoreLedger.Application.Accounts;
using CoreBanking.CoreLedger.Domain.Accounts;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.CoreLedger.Infrastructure.Accounts;

/// <summary>
/// Allocates sequences from the durable reservation row of one published scheme instance. The row is
/// read under <c>FOR UPDATE</c> and also carries an optimistic version, so a stale writer cannot
/// allocate and no number is ever derived from the highest stored account number.
/// Decision: P1-E04-D06.
/// </summary>
public sealed class AccountNumberAllocator : IAccountNumberAllocator
{
    private const long InitialNextValue = 1;

    private readonly CoreLedgerDbContext _context;
    private readonly TimeProvider _timeProvider;

    public AccountNumberAllocator(CoreLedgerDbContext context, TimeProvider timeProvider)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<AllocatedAccountNumber> AllocateAsync(
        InstitutionId institutionId,
        AccountNumberScheme scheme,
        string schemeVersionId,
        string productCode,
        string branchCode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentNullException.ThrowIfNull(scheme);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemeVersionId);

        AccountNumberReservationRecord reservation = await LockOrCreateAsync(
            institutionId, scheme, schemeVersionId, cancellationToken).ConfigureAwait(false);

        AccountNumberAllocationState state = new(reservation.NextValue, reservation.HighWaterMark, reservation.Version);
        AccountNumberAllocation allocation;
        try
        {
            allocation = AccountNumberAllocationPolicy.Allocate(state, 1, reservation.Capacity);
        }
        catch (InvalidOperationException exception) when (exception.Message == AccountNumberAllocationPolicy.ExhaustedCode)
        {
            throw new AccountApplicationRejectedException(
                AccountNumberAllocationPolicy.ExhaustedCode,
                "The account-number scheme has no numbers left; the application stays approved and unassigned.",
                exception);
        }
        catch (InvalidOperationException exception)
        {
            throw new AccountApplicationRejectedException("account_number_state_invalid", exception.Message, exception);
        }

        long sequence = allocation.Sequences[0];
        string accountNumber = scheme.Format(productCode, branchCode, sequence);
        reservation.NextValue = allocation.State.NextValue;
        reservation.HighWaterMark = allocation.State.HighWaterMark;
        reservation.Version = allocation.State.Version;
        reservation.UpdatedAt = _timeProvider.GetUtcNow();

        return new AllocatedAccountNumber(
            accountNumber,
            scheme.Mask(accountNumber),
            scheme.SchemeId,
            schemeVersionId,
            sequence,
            allocation.DepletionBand);
    }

    public async Task<IReadOnlyList<long>> RetireAsync(
        InstitutionId institutionId,
        string schemeVersionId,
        IReadOnlyCollection<long> sequences,
        string reason,
        string actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemeVersionId);
        ArgumentNullException.ThrowIfNull(sequences);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);

        DateTimeOffset now = _timeProvider.GetUtcNow();
        foreach (long sequence in sequences.Distinct())
        {
            _context.AccountNumberRetirements.Add(new AccountNumberRetirementRecord
            {
                InstitutionId = institutionId.Value,
                RetirementId = Guid.NewGuid().ToString("N"),
                SchemeVersionId = schemeVersionId,
                SequenceValue = sequence,
                Reason = reason.Trim(),
                RetiredByActorId = actorId,
                RetiredAt = now,
            });
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return sequences.Distinct().OrderBy(value => value).ToArray();
    }

    /// <summary>
    /// Takes the lock and the next sequence. The caller owns the transaction, so the reservation and the
    /// account row commit together. A concurrent first opening for the same scheme is not a failure: the
    /// rejected insert is detached and the winner's row is read under the same lock, so every opening
    /// still receives a number. Decision: P1-E04-D06.
    /// </summary>
    private async Task<AccountNumberReservationRecord> LockOrCreateAsync(
        InstitutionId institutionId,
        AccountNumberScheme scheme,
        string schemeVersionId,
        CancellationToken cancellationToken)
    {
        AccountNumberReservationRecord? reservation =
            await SelectForUpdateAsync(institutionId, schemeVersionId, cancellationToken).ConfigureAwait(false);
        if (reservation is not null)
        {
            return reservation;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        reservation = new AccountNumberReservationRecord
        {
            InstitutionId = institutionId.Value,
            SchemeVersionId = schemeVersionId,
            SchemeId = scheme.SchemeId,
            NextValue = InitialNextValue,
            HighWaterMark = 0,
            Capacity = scheme.Capacity,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _context.AccountNumberReservations.Add(reservation);
        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception)
        {
            // Entity Framework rolls the failed statement back to its savepoint, so the transaction is
            // still usable: discard the losing insert and take the row the other opening committed.
            _context.Entry(reservation).State = EntityState.Detached;

            AccountNumberReservationRecord? winner =
                await SelectForUpdateAsync(institutionId, schemeVersionId, cancellationToken).ConfigureAwait(false);
            return winner ?? throw new AccountApplicationRejectedException(
                "account_number_reservation_conflict",
                "Another opening created the reservation for this scheme at the same time.",
                exception);
        }

        return reservation;
    }

    private async Task<AccountNumberReservationRecord?> SelectForUpdateAsync(
        InstitutionId institutionId,
        string schemeVersionId,
        CancellationToken cancellationToken) =>
        (await _context.AccountNumberReservations
            .FromSqlRaw(
                "SELECT * FROM account.account_number_reservations " +
                "WHERE institution_id = {0} AND scheme_version_id = {1} FOR UPDATE",
                institutionId.Value,
                schemeVersionId)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false))
        .SingleOrDefault();
}
