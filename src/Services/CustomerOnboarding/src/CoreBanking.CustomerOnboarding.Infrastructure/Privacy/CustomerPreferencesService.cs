using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.CustomerOnboarding.Application.Customers;
using CoreBanking.CustomerOnboarding.Application.Onboarding;
using CoreBanking.CustomerOnboarding.Application.Privacy;
using CoreBanking.CustomerOnboarding.Domain;
using CoreBanking.CustomerOnboarding.Domain.Privacy;
using CoreBanking.CustomerOnboarding.Infrastructure.Audit;
using CoreBanking.CustomerOnboarding.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Npgsql;

namespace CoreBanking.CustomerOnboarding.Infrastructure.Privacy;

/// <summary>
/// Versioned customer preferences with append-only consent evidence. Repeating the current decision is
/// idempotent, a change closes the current version, and a permission needs a lawful basis.
/// Decision: P1-E03-D12.
/// </summary>
public sealed class CustomerPreferencesService : ICustomerPreferencesService
{
    private readonly CustomerOnboardingDbContext _context;
    private readonly TimeProvider _timeProvider;

    public CustomerPreferencesService(CustomerOnboardingDbContext context, TimeProvider timeProvider)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<CustomerPreferencesView> SetAsync(
        SetCustomerPreferenceCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        GuardCommand(command);

        // Purpose and channel are normalised the same way the domain validates them, so the current-version
        // lookup and the stored row always agree.
        string purpose = command.Purpose.Trim().ToLowerInvariant();
        string channel = command.Channel.Trim().ToLowerInvariant();

        await EnsureCustomerAsync(command.CustomerId, cancellationToken).ConfigureAwait(false);

        ServicePreferenceRecord? current = await _context.ServicePreferences
            .SingleOrDefaultAsync(
                preference => preference.CustomerId == command.CustomerId
                    && preference.Purpose == purpose
                    && preference.Channel == channel
                    && preference.EffectiveTo == null,
                cancellationToken)
            .ConfigureAwait(false);

        if (current is not null && string.Equals(current.State, PreferenceCodes.ToCode(command.State), StringComparison.Ordinal))
        {
            return await GetAsync(
                new GetCustomerPreferencesQuery(command.InstitutionId, command.ActorId, command.CustomerId),
                cancellationToken).ConfigureAwait(false);
        }

        bool withdrawingGrant = command.State == PreferenceState.Denied
            && current is not null
            && current.State == PreferenceCodes.ToCode(PreferenceState.Permitted);
        if (withdrawingGrant && string.IsNullOrWhiteSpace(command.Reason))
        {
            throw Conflict("preference_reason_required", "Withdrawing a granted preference requires a recorded reason.");
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        int lastVersion = await _context.ServicePreferences
            .Where(preference => preference.CustomerId == command.CustomerId
                && preference.Purpose == purpose
                && preference.Channel == channel)
            .MaxAsync(preference => (int?)preference.Version, cancellationToken)
            .ConfigureAwait(false) ?? 0;

        ServicePreference preference;
        try
        {
            preference = ServicePreference.Create(
                command.InstitutionId,
                command.CustomerId,
                purpose,
                channel,
                lastVersion + 1,
                command.State,
                command.LawfulBasis,
                command.Source,
                command.EvidenceReference,
                command.Reason,
                command.ActorId.Value,
                now);
        }
        catch (OnboardingRuleException exception)
        {
            throw Conflict(exception.Code, exception.Message);
        }

        if (current is not null)
        {
            current.EffectiveTo = now;
        }

        _context.ServicePreferences.Add(new ServicePreferenceRecord
        {
            InstitutionId = command.InstitutionId.Value,
            PreferenceId = preference.PreferenceId,
            CustomerId = preference.CustomerId,
            Purpose = preference.Purpose,
            Channel = preference.Channel,
            Version = preference.Version,
            State = PreferenceCodes.ToCode(preference.State),
            LawfulBasis = preference.LawfulBasis,
            Source = preference.Source,
            EvidenceReference = preference.EvidenceReference,
            Reason = preference.Reason,
            EffectiveFrom = preference.EffectiveFrom,
            EffectiveTo = preference.EffectiveTo,
            RecordedBy = preference.RecordedBy,
        });

        _context.ConsentEvidence.Add(new ConsentEvidenceRecord
        {
            InstitutionId = command.InstitutionId.Value,
            ConsentId = Guid.NewGuid().ToString("N"),
            CustomerId = preference.CustomerId,
            Purpose = preference.Purpose,
            Channel = preference.Channel,
            Version = preference.Version,
            State = PreferenceCodes.ToCode(preference.State),
            LawfulBasis = preference.LawfulBasis,
            Source = preference.Source,
            EvidenceReference = preference.EvidenceReference,
            RecordedBy = preference.RecordedBy,
            RecordedAt = now,
            WithdrawnAt = withdrawingGrant ? now : null,
        });

        AuditRecorder.Record(
            _context,
            command.InstitutionId,
            command.ActorId.Value,
            AuditRecorder.ActionCustomerPreferenceSet,
            AuditRecorder.SubjectCustomer,
            preference.CustomerId,
            command.CorrelationId,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["purpose"] = preference.Purpose,
                ["channel"] = preference.Channel,
                ["state"] = PreferenceCodes.ToCode(preference.State),
                ["version"] = preference.Version.ToString(CultureInfo.InvariantCulture),
                ["source"] = preference.Source,
            },
            now);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            DiscardPendingChanges();
            throw Conflict("preference_conflict", "Another preference version was recorded at the same time.");
        }
        return await GetAsync(
            new GetCustomerPreferencesQuery(command.InstitutionId, command.ActorId, command.CustomerId),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<CustomerPreferencesView> GetAsync(
        GetCustomerPreferencesQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        await EnsureCustomerAsync(query.CustomerId, cancellationToken).ConfigureAwait(false);

        List<ServicePreferenceRecord> preferences = await _context.ServicePreferences
            .AsNoTracking()
            .Where(preference => preference.CustomerId == query.CustomerId)
            .OrderBy(preference => preference.Purpose)
            .ThenBy(preference => preference.Channel)
            .ThenBy(preference => preference.Version)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<ConsentEvidenceRecord> consent = await _context.ConsentEvidence
            .AsNoTracking()
            .Where(record => record.CustomerId == query.CustomerId)
            .OrderBy(record => record.RecordedAt)
            .ThenBy(record => record.Version)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new CustomerPreferencesView(
            query.CustomerId,
            [
                .. preferences.Select(preference => new ServicePreferenceView(
                    preference.PreferenceId,
                    preference.Purpose,
                    preference.Channel,
                    preference.Version,
                    PreferenceCodes.FromStateCode(preference.State),
                    preference.State,
                    preference.LawfulBasis,
                    preference.Source,
                    preference.EvidenceReference,
                    preference.Reason,
                    preference.EffectiveFrom,
                    preference.EffectiveTo,
                    preference.EffectiveTo is null)),
            ],
            [
                .. consent.Select(record => new ConsentEvidenceView(
                    record.ConsentId,
                    record.Purpose,
                    record.Channel,
                    record.Version,
                    record.State,
                    record.LawfulBasis,
                    record.Source,
                    record.EvidenceReference,
                    record.RecordedBy,
                    record.RecordedAt,
                    record.WithdrawnAt)),
            ]);
    }

    private static void GuardCommand(SetCustomerPreferenceCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.CustomerId))
        {
            throw Conflict("customer_not_found", "A preference requires a customer reference.");
        }

        if (command.State == PreferenceState.Denied
            && !string.IsNullOrWhiteSpace(command.LawfulBasis)
            && command.LawfulBasis.Trim().Length > 64)
        {
            throw Conflict("preference_code_invalid", "A lawful basis must be a stable code.");
        }
    }

    private async Task EnsureCustomerAsync(string customerId, CancellationToken cancellationToken)
    {
        bool exists = await _context.Customers
            .AsNoTracking()
            .AnyAsync(customer => customer.CustomerId == customerId, cancellationToken)
            .ConfigureAwait(false);

        if (!exists)
        {
            throw new CustomerNotFoundException(customerId);
        }
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgres
        && postgres.SqlState == PostgresErrorCodes.UniqueViolation;

    private static OnboardingApplicationConflictException Conflict(string code, string message) => new(code, message);

    private void DiscardPendingChanges()
    {
        foreach (EntityEntry entry in _context.ChangeTracker.Entries().ToList())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.State = EntityState.Detached;
                    break;
                case EntityState.Modified:
                case EntityState.Deleted:
                    entry.State = EntityState.Unchanged;
                    break;
                default:
                    break;
            }
        }
    }
}
