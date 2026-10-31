using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.CustomerOnboarding.Application.Onboarding;
using CoreBanking.CustomerOnboarding.Domain.Onboarding;
using CoreBanking.CustomerOnboarding.Domain.Parties;
using CoreBanking.CustomerOnboarding.Domain.Requirements;
using CoreBanking.CustomerOnboarding.Infrastructure.Persistence;
using CoreBanking.CustomerOnboarding.Infrastructure.Requirements;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Npgsql;

namespace CoreBanking.CustomerOnboarding.Infrastructure.Onboarding;

/// <summary>
/// Individual onboarding capture. Every write records an immutable revision with its payload hash and
/// the protected snapshot in the same local transaction, and submission creates the party from that
/// exact snapshot. Decision: CUS-UC-01, invariant 3, ADR-0007.
/// </summary>
public sealed class OnboardingApplicationService : IOnboardingApplicationService
{
    public const string OperationCreate = "customer.application.create";
    public const string OperationUpdate = "customer.application.update";
    public const string OperationSubmit = "customer.application.submit";

    private const string EffectivePackStatus = "effective";
    private const string RejectedScanStatus = "rejected";

    private readonly CustomerOnboardingDbContext _context;
    private readonly IProtectedValueProtector _protector;
    private readonly IdempotencyService _idempotency;
    private readonly TimeProvider _timeProvider;

    public OnboardingApplicationService(
        CustomerOnboardingDbContext context,
        IProtectedValueProtector protector,
        IdempotencyService idempotency,
        TimeProvider timeProvider)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
        _idempotency = idempotency ?? throw new ArgumentNullException(nameof(idempotency));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<OnboardingApplicationView> StartAsync(
        StartIndividualApplicationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Capture);

        return await ExecuteAsync(
            command.InstitutionId,
            command.ActorId,
            OperationCreate,
            command.IdempotencyKey,
            Fingerprint(command.ApplicationReference, command.BranchId ?? string.Empty, command.ActorId.Value, DescribeCapture(command.Capture)),
            token => ExecuteStartAsync(command, token),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<OnboardingApplicationView> UpdateAsync(
        UpdateIndividualApplicationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Capture);

        return await ExecuteAsync(
            command.InstitutionId,
            command.ActorId,
            OperationUpdate,
            command.IdempotencyKey,
            Fingerprint(command.ApplicationId, command.ExpectedVersion.ToString(CultureInfo.InvariantCulture), command.ActorId.Value, DescribeCapture(command.Capture)),
            token => ExecuteUpdateAsync(command, token),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<OnboardingApplicationView> SubmitAsync(
        SubmitOnboardingApplicationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return await ExecuteAsync(
            command.InstitutionId,
            command.ActorId,
            OperationSubmit,
            command.IdempotencyKey,
            Fingerprint(command.ApplicationId, command.ExpectedRevision.ToString(CultureInfo.InvariantCulture), command.ActorId.Value),
            token => ExecuteSubmitAsync(command, token),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<OnboardingApplicationView> WithdrawAsync(
        WithdrawOnboardingApplicationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return await ExecuteAsync(
            command.InstitutionId,
            command.ActorId,
            OperationUpdate,
            command.IdempotencyKey,
            Fingerprint(command.ApplicationId, command.ExpectedVersion.ToString(CultureInfo.InvariantCulture), "withdraw", command.ActorId.Value),
            token => ExecuteWithdrawAsync(command, token),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<OnboardingApplicationView> GetAsync(
        GetOnboardingApplicationQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        OnboardingApplicationRecord record = await LoadAsync(query.InstitutionId, query.ApplicationId, cancellationToken)
            .ConfigureAwait(false);

        return ToView(record, replayed: false);
    }

    private async Task<OnboardingApplicationView> ExecuteAsync(
        InstitutionId institutionId,
        ActorId actorId,
        string operation,
        string idempotencyKeyValue,
        string fingerprint,
        Func<CancellationToken, Task<string>> command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actorId);

        IdempotencyKey key = IdempotencyKey.Create(
            institutionId.Value,
            operation,
            actorId.Value,
            idempotencyKeyValue);

        IdempotencyOutcome outcome = await _idempotency.ExecuteAsync(
            key,
            fingerprint,
            async token => await command(token).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

        return outcome.Kind switch
        {
            IdempotencyOutcomeKind.Executed => await ReadAsync(institutionId, outcome.Result, replayed: false, cancellationToken).ConfigureAwait(false),
            IdempotencyOutcomeKind.Replayed => await ReadAsync(institutionId, outcome.Result, replayed: true, cancellationToken).ConfigureAwait(false),
            IdempotencyOutcomeKind.Conflict => throw new OnboardingApplicationConflictException(
                "idempotency_key_reused_with_different_request",
                "The idempotency key was already used with a materially different onboarding request."),
            _ => throw new OnboardingApplicationConflictException(
                "onboarding_capture_in_progress",
                "An equivalent onboarding request is still in progress."),
        };
    }

    private async Task<OnboardingApplicationView> ReadAsync(
        InstitutionId institutionId,
        string? applicationId,
        bool replayed,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(applicationId))
        {
            throw new OnboardingApplicationConflictException(
                "onboarding_revision_conflict",
                "The recorded outcome does not identify an onboarding application.");
        }

        OnboardingApplicationRecord record = await LoadAsync(institutionId, applicationId, cancellationToken)
            .ConfigureAwait(false);

        return ToView(record, replayed);
    }

    private async Task<string> ExecuteStartAsync(
        StartIndividualApplicationCommand command,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        CapturePayload payload = CapturePayloadFactory.Protect(command.Capture, _protector);
        string payloadHash = CapturePayloadFactory.Hash(payload);

        OnboardingApplication application = OnboardingApplication.Start(
            command.InstitutionId,
            command.ApplicationReference,
            command.BranchId,
            now);

        OnboardingApplicationRecord record = new()
        {
            InstitutionId = application.InstitutionId.Value,
            ApplicationId = application.ApplicationId,
            ApplicationReference = application.ApplicationReference,
            ApplicationType = application.ApplicationType,
            BranchId = application.BranchId,
            CreatedAt = application.CreatedAt,
        };

        Apply(application, record, payload, payloadHash, command.ActorId, now, reason: "captured");
        _context.OnboardingApplications.Add(record);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (IsReferenceConflict(exception))
        {
            DiscardPendingChanges();
            throw new OnboardingApplicationConflictException(
                "onboarding_reference_conflict",
                "The application reference is already in use in this institution.");
        }
        catch (DbUpdateException)
        {
            DiscardPendingChanges();
            throw;
        }

        return record.ApplicationId;
    }

    private async Task<string> ExecuteUpdateAsync(
        UpdateIndividualApplicationCommand command,
        CancellationToken cancellationToken)
    {
        OnboardingApplicationRecord record = await LoadAsync(command.InstitutionId, command.ApplicationId, cancellationToken)
            .ConfigureAwait(false);
        EnsureVersion(record, command.ExpectedVersion);

        DateTimeOffset now = _timeProvider.GetUtcNow();
        OnboardingApplication application = Restore(record);
        application.Revise(now);

        CapturePayload payload = CapturePayloadFactory.Protect(command.Capture, _protector);
        Apply(application, record, payload, CapturePayloadFactory.Hash(payload), command.ActorId, now, reason: "captured");

        await SaveAsync(cancellationToken).ConfigureAwait(false);
        return record.ApplicationId;
    }

    private async Task<string> ExecuteSubmitAsync(
        SubmitOnboardingApplicationCommand command,
        CancellationToken cancellationToken)
    {
        OnboardingApplicationRecord record = await LoadAsync(command.InstitutionId, command.ApplicationId, cancellationToken)
            .ConfigureAwait(false);

        if (record.Revision != command.ExpectedRevision)
        {
            throw new OnboardingApplicationConflictException(
                "onboarding_revision_conflict",
                "The application changed since the confirmed revision was read.");
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        CapturePayload payload = CapturePayloadFactory.Deserialise(record.CurrentCapture);
        Party party = CapturePayloadFactory.ToParty(payload, command.InstitutionId, now);

        RequirementPackRecord? effectivePack = await _context.RequirementPacks
            .SingleOrDefaultAsync(pack => pack.Status == EffectivePackStatus, cancellationToken)
            .ConfigureAwait(false);
        if (effectivePack is null)
        {
            throw new OnboardingApplicationConflictException(
                "onboarding_requirement_pack_missing",
                "No approved onboarding requirement pack is effective for this institution.");
        }

        bool rejectedDocument = await _context.DocumentReferences
            .AnyAsync(
                document => document.ApplicationId == record.ApplicationId && document.ScanStatus == RejectedScanStatus,
                cancellationToken)
            .ConfigureAwait(false);
        if (rejectedDocument)
        {
            throw new OnboardingApplicationConflictException(
                "onboarding_document_scan_rejected",
                "A document registered for this application failed its security scan.");
        }

        RequirementSnapshot requirementSnapshot = RequirementSnapshot.Capture(
            command.InstitutionId,
            record.ApplicationId,
            record.Revision + 1,
            RequirementPayload.ToPack(effectivePack),
            command.ActorId.Value,
            now);

        OnboardingApplication application = Restore(record);
        application.Submit(party.PartyId, requirementSnapshot.RequirementSnapshotId, now);

        _context.RequirementSnapshots.Add(RequirementPayload.ToRecord(requirementSnapshot));
        _context.Parties.Add(ToPartyRecord(party));
        Apply(application, record, payload, CapturePayloadFactory.Hash(payload), command.ActorId, now, reason: "submitted");

        await SaveAsync(cancellationToken).ConfigureAwait(false);
        return record.ApplicationId;
    }

    private async Task<string> ExecuteWithdrawAsync(
        WithdrawOnboardingApplicationCommand command,
        CancellationToken cancellationToken)
    {
        OnboardingApplicationRecord record = await LoadAsync(command.InstitutionId, command.ApplicationId, cancellationToken)
            .ConfigureAwait(false);
        EnsureVersion(record, command.ExpectedVersion);

        DateTimeOffset now = _timeProvider.GetUtcNow();
        OnboardingApplication application = Restore(record);
        application.Withdraw(now);

        CapturePayload payload = CapturePayloadFactory.Deserialise(record.CurrentCapture);
        Apply(application, record, payload, record.CurrentPayloadHash, command.ActorId, now, reason: "withdrawn");

        await SaveAsync(cancellationToken).ConfigureAwait(false);
        return record.ApplicationId;
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            DiscardPendingChanges();
            throw new OnboardingApplicationConflictException(
                "onboarding_revision_conflict",
                "The application was changed by another request.");
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException postgres
            && postgres.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            DiscardPendingChanges();
            throw new OnboardingApplicationConflictException(
                "onboarding_requirement_snapshot_conflict",
                "A requirement snapshot already exists for this application revision.");
        }
        catch (DbUpdateException)
        {
            DiscardPendingChanges();
            throw;
        }
    }

    /// <summary>
    /// Drops the rejected writes from the change tracker. The shared idempotency store saves again
    /// while it releases its claim, and a rejected insert that is still tracked would fail that save
    /// and replace the real failure with a confusing one.
    /// </summary>
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

    private async Task<OnboardingApplicationRecord> LoadAsync(
        InstitutionId institutionId,
        string applicationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationId);

        OnboardingApplicationRecord? record = await _context.OnboardingApplications
            .SingleOrDefaultAsync(
                candidate => candidate.InstitutionId == institutionId.Value && candidate.ApplicationId == applicationId,
                cancellationToken)
            .ConfigureAwait(false);

        return record ?? throw new OnboardingApplicationNotFoundException(applicationId);
    }

    private static void EnsureVersion(OnboardingApplicationRecord record, long expectedVersion)
    {
        if (record.ConcurrencyVersion != expectedVersion)
        {
            throw new OnboardingApplicationConflictException(
                "onboarding_revision_conflict",
                "The application changed since it was last read.");
        }
    }

    private static OnboardingApplication Restore(OnboardingApplicationRecord record) =>
        OnboardingApplication.Restore(
            InstitutionId.From(record.InstitutionId),
            record.ApplicationId,
            record.ApplicationReference,
            record.ApplicationType,
            record.BranchId,
            ParseStatus(record.Status),
            record.Revision,
            record.PartyId,
            record.RequirementSnapshotId,
            record.CreatedAt,
            record.UpdatedAt,
            record.SubmittedAt,
            record.WithdrawnAt,
            record.ConcurrencyVersion);

    private static void Apply(
        OnboardingApplication application,
        OnboardingApplicationRecord record,
        CapturePayload payload,
        string payloadHash,
        ActorId actorId,
        DateTimeOffset now,
        string reason)
    {
        string status = StatusText(application.Status);
        string capture = CapturePayloadFactory.Serialise(payload);

        record.Status = status;
        record.Revision = application.Revision;
        record.CurrentCapture = capture;
        record.CurrentPayloadHash = payloadHash;
        record.PartyId = application.PartyId;
        record.RequirementSnapshotId = application.RequirementSnapshotId;
        record.UpdatedAt = application.UpdatedAt;
        record.SubmittedAt = application.SubmittedAt;
        record.WithdrawnAt = application.WithdrawnAt;
        record.ConcurrencyVersion = application.ConcurrencyVersion;
        record.Revisions.Add(new OnboardingApplicationRevisionRecord
        {
            InstitutionId = record.InstitutionId,
            ApplicationId = record.ApplicationId,
            Revision = application.Revision,
            Status = status,
            PayloadHash = payloadHash,
            Capture = capture,
            Reason = reason,
            RequirementSnapshotId = application.RequirementSnapshotId,
            RecordedBy = actorId.Value,
            RecordedAt = now,
        });
    }

    private static PartyRecord ToPartyRecord(Party party)
    {
        PartyRecord record = new()
        {
            InstitutionId = party.InstitutionId.Value,
            PartyId = party.PartyId,
            PartyType = party.Type.ToString().ToLowerInvariant(),
            Status = party.Status.ToString().ToLowerInvariant(),
            CreatedAt = party.CreatedAt,
            UpdatedAt = party.UpdatedAt,
            ConcurrencyVersion = party.ConcurrencyVersion,
            Individual = new PartyIndividualRecord
            {
                InstitutionId = party.InstitutionId.Value,
                PartyId = party.PartyId,
                BirthDateToken = party.BirthDetails.BirthDateToken,
                BirthDateLookupToken = party.BirthDetails.BirthDateLookupToken,
                BirthDateMasked = party.BirthDetails.BirthDateMasked,
                BirthYear = party.BirthDetails.BirthYear,
                CountryOfBirth = party.BirthDetails.CountryOfBirth,
                CapturedAt = party.BirthDetails.CapturedAt,
            },
        };

        record.Names.Add(new PartyNameRecord
        {
            InstitutionId = party.InstitutionId.Value,
            NameId = Guid.NewGuid().ToString("N"),
            PartyId = party.PartyId,
            NameType = "legal",
            GivenName = party.LegalName.GivenName,
            MiddleName = party.LegalName.MiddleName,
            FamilyName = party.LegalName.FamilyName,
            Normalised = party.LegalName.Normalised,
            EffectiveFrom = party.CreatedAt,
        });

        foreach (ProtectedContactPoint contact in party.Contacts)
        {
            record.Contacts.Add(new PartyContactRecord
            {
                InstitutionId = party.InstitutionId.Value,
                ContactId = Guid.NewGuid().ToString("N"),
                PartyId = party.PartyId,
                ContactType = contact.Type.ToString().ToLowerInvariant(),
                ValueToken = contact.ValueToken,
                MaskedValue = contact.MaskedValue,
                LookupToken = contact.LookupToken,
                CapturedAt = contact.CapturedAt,
                EffectiveFrom = party.CreatedAt,
            });
        }

        foreach (ProtectedAddress address in party.Addresses)
        {
            record.Addresses.Add(new PartyAddressRecord
            {
                InstitutionId = party.InstitutionId.Value,
                AddressId = Guid.NewGuid().ToString("N"),
                PartyId = party.PartyId,
                AddressType = address.Type.ToString().ToLowerInvariant(),
                LineToken = address.LineToken,
                LookupToken = address.LookupToken,
                MaskedLine = address.MaskedLine,
                City = address.City,
                Region = address.Region,
                CountryCode = address.CountryCode,
                CapturedAt = address.CapturedAt,
                EffectiveFrom = party.CreatedAt,
            });
        }

        return record;
    }

    private static OnboardingApplicationView ToView(OnboardingApplicationRecord record, bool replayed)
    {
        CapturePayload payload = CapturePayloadFactory.Deserialise(record.CurrentCapture);
        OnboardingCaptureSnapshot snapshot = CapturePayloadFactory.ToSnapshot(payload, record.UpdatedAt);

        return new OnboardingApplicationView(
            record.ApplicationId,
            record.ApplicationReference,
            record.ApplicationType,
            record.BranchId,
            record.Status,
            record.Revision,
            record.ConcurrencyVersion,
            record.PartyId,
            snapshot.MissingFields(),
            record.CreatedAt,
            record.UpdatedAt,
            record.SubmittedAt,
            replayed);
    }

    /// <summary>
    /// A canonical description of what the applicant supplied. Only its hash is stored, so a retry of
    /// the same capture replays and a different capture under the same key conflicts.
    /// </summary>
    private static string DescribeCapture(OnboardingCapture capture)
    {
        List<string> parts =
        [
            capture.LegalName is null
                ? "name:none"
                : string.Format(CultureInfo.InvariantCulture, "name:{0}|{1}|{2}", capture.LegalName.GivenName, capture.LegalName.MiddleName, capture.LegalName.FamilyName),
            capture.BirthDetails is null
                ? "birth:none"
                : string.Format(CultureInfo.InvariantCulture, "birth:{0:yyyy-MM-dd}|{1}", capture.BirthDetails.BirthDate, capture.BirthDetails.CountryOfBirth),
        ];

        parts.AddRange(capture.Contacts.Select(contact =>
            string.Format(CultureInfo.InvariantCulture, "contact:{0}|{1}", contact.Type, contact.Value)));
        parts.AddRange(capture.Addresses.Select(address =>
            string.Format(CultureInfo.InvariantCulture, "address:{0}|{1}|{2}|{3}|{4}|{5}", address.Type, address.Line1, address.Line2, address.City, address.Region, address.CountryCode)));

        return string.Join('\n', parts);
    }

    private static string Fingerprint(params string[] parts) => IdempotencyKey.Fingerprint(string.Join('\n', parts));

    private static string StatusText(ApplicationStatus status) => status.ToString().ToLowerInvariant();

    private static ApplicationStatus ParseStatus(string status) =>
        Enum.TryParse(status, ignoreCase: true, out ApplicationStatus parsed)
            ? parsed
            : throw new InvalidOperationException($"The stored onboarding application has an unsupported status '{status}'.");

    private static bool IsReferenceConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres
        && string.Equals(postgres.ConstraintName, "ux_onboarding_applications_reference", StringComparison.Ordinal);
}
