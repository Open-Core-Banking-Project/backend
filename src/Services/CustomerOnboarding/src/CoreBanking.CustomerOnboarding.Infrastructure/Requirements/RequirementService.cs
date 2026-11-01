using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.CustomerOnboarding.Application.Onboarding;
using CoreBanking.CustomerOnboarding.Application.Requirements;
using CoreBanking.CustomerOnboarding.Domain;
using CoreBanking.CustomerOnboarding.Domain.Requirements;
using CoreBanking.CustomerOnboarding.Infrastructure.Audit;
using CoreBanking.CustomerOnboarding.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Npgsql;

namespace CoreBanking.CustomerOnboarding.Infrastructure.Requirements;

/// <summary>
/// Institution-owned requirement packs, their read-only snapshots and document reference metadata.
/// Publishing a newer pack supersedes the previous one for future submissions only, and a document is
/// never treated as clean without a quarantine answer. Decision: P1-E03-D01.
/// </summary>
public sealed class RequirementService : IRequirementService
{
    private const string EffectivePackStatus = "effective";
    private const string SupersededPackStatus = "superseded";

    private readonly CustomerOnboardingDbContext _context;
    private readonly IDocumentQuarantine _quarantine;
    private readonly TimeProvider _timeProvider;

    public RequirementService(
        CustomerOnboardingDbContext context,
        IDocumentQuarantine quarantine,
        TimeProvider timeProvider)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _quarantine = quarantine ?? throw new ArgumentNullException(nameof(quarantine));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<RequirementPackView> PublishPackAsync(
        PublishRequirementPackCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        RequirementPackRecord? effective = await _context.RequirementPacks
            .SingleOrDefaultAsync(pack => pack.Status == EffectivePackStatus, cancellationToken)
            .ConfigureAwait(false);

        DateTimeOffset now = _timeProvider.GetUtcNow();
        int version = 1;
        if (effective is not null)
        {
            version = effective.Version + 1;
            effective.Status = SupersededPackStatus;
            effective.SupersededAt = now;
        }

        RequirementPack pack;
        try
        {
            pack = RequirementPack.Create(command.PackCode, version, command.Requirements);
        }
        catch (OnboardingRuleException exception)
        {
            throw new OnboardingApplicationConflictException(exception.Code, exception.Message);
        }

        RequirementPackRecord record = new()
        {
            InstitutionId = command.InstitutionId.Value,
            PackCode = pack.PackCode,
            Version = pack.Version,
            ContentHash = pack.ContentHash,
            Requirements = RequirementPayload.Serialise(pack.Requirements),
            Status = EffectivePackStatus,
            PublishedBy = command.ActorId.Value,
            PublishedAt = now,
        };

        _context.RequirementPacks.Add(record);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            DiscardPendingChanges();
            throw new OnboardingApplicationConflictException(
                "requirement_pack_conflict",
                "Another pack version was published at the same time.");
        }

        AuditRecorder.Record(
            _context,
            command.InstitutionId,
            command.ActorId.Value,
            AuditRecorder.ActionRequirementPackPublished,
            AuditRecorder.SubjectRequirementPack,
            pack.PackCode,
            command.CorrelationId,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["packCode"] = pack.PackCode,
                ["version"] = pack.Version.ToString(CultureInfo.InvariantCulture),
                ["contentHash"] = pack.ContentHash,
            },
            now);

        return ToView(record, pack);
    }

    public async Task<DocumentReferenceView> RegisterDocumentAsync(
        RegisterDocumentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        bool applicationExists = await _context.OnboardingApplications
            .AnyAsync(record => record.ApplicationId == command.ApplicationId, cancellationToken)
            .ConfigureAwait(false);
        if (!applicationExists)
        {
            throw new OnboardingApplicationNotFoundException(command.ApplicationId);
        }

        DocumentReferenceRecord record = new()
        {
            InstitutionId = command.InstitutionId.Value,
            DocumentReferenceId = Guid.NewGuid().ToString("N"),
            ApplicationId = command.ApplicationId,
            RequirementCode = ValidateCode(command.RequirementCode, "requirement_code_invalid"),
            ContentType = ValidateText(command.ContentType, 128, "document_content_type_invalid"),
            ContentLength = ValidateLength(command.ContentLength),
            ContentHash = ValidateHash(command.ContentHash),
            StorageReference = ValidateText(command.StorageReference, 512, "document_storage_reference_invalid"),
            RegisteredBy = command.ActorId.Value,
            RegisteredAt = _timeProvider.GetUtcNow(),
        };

        DocumentScanResult scan = await _quarantine
            .SubmitAsync(
                new DocumentScanRequest(
                    command.InstitutionId.Value,
                    command.ApplicationId,
                    record.RequirementCode,
                    record.ContentHash,
                    record.ContentLength,
                    record.ContentType,
                    record.StorageReference),
                cancellationToken)
            .ConfigureAwait(false);

        record.ScanStatus = scan.Status switch
        {
            DocumentScanStatus.Clean => "clean",
            DocumentScanStatus.Rejected => "rejected",
            _ => "pending_scan",
        };
        record.ProviderReference = scan.ProviderReference;

        _context.DocumentReferences.Add(record);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new DocumentReferenceView(
            record.DocumentReferenceId,
            record.ApplicationId,
            record.RequirementCode,
            record.ContentType,
            record.ContentLength,
            record.ContentHash,
            record.ScanStatus,
            record.RegisteredAt);
    }

    public async Task<RequirementSnapshotView> GetSnapshotAsync(
        GetRequirementSnapshotQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        RequirementSnapshotRecord record = await _context.RequirementSnapshots
            .AsNoTracking()
            .SingleOrDefaultAsync(snapshot => snapshot.RequirementSnapshotId == query.RequirementSnapshotId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new RequirementSnapshotNotFoundException(query.RequirementSnapshotId);

        RequirementSnapshot snapshot = RequirementPayload.ToModel(record);

        return new RequirementSnapshotView(
            snapshot.RequirementSnapshotId,
            snapshot.ApplicationId,
            snapshot.ApplicationRevision,
            snapshot.PackCode,
            snapshot.PackVersion,
            snapshot.ContentHash,
            snapshot.CapturedAt,
            snapshot.CapturedBy,
            [.. snapshot.Requirements.Select(requirement => new RequirementView(
                requirement.Code,
                requirement.Label,
                requirement.Mandatory,
                requirement.EvidenceKinds))]);
    }

    private static RequirementPackView ToView(RequirementPackRecord record, RequirementPack pack) =>
        new(
            record.PackCode,
            record.Version,
            record.ContentHash,
            record.PublishedAt,
            record.PublishedBy,
            [.. pack.Requirements.Select(requirement => new RequirementView(
                requirement.Code,
                requirement.Label,
                requirement.Mandatory,
                requirement.EvidenceKinds))]);

    private static string ValidateCode(string value, string code)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > 64)
        {
            throw new OnboardingApplicationConflictException(code, "A requirement code is required.");
        }

        return value.Trim().ToLowerInvariant();
    }

    private static string ValidateText(string value, int maximumLength, string code)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > maximumLength)
        {
            throw new OnboardingApplicationConflictException(code, "A value is required and must be within its length limit.");
        }

        return value.Trim();
    }

    private static long ValidateLength(long value) => value > 0
        ? value
        : throw new OnboardingApplicationConflictException("document_content_length_invalid", "A document requires a positive length.");

    private static string ValidateHash(string value)
    {
        string trimmed = (value ?? string.Empty).Trim();
        bool valid = trimmed.Length == 64 && trimmed.All(char.IsAsciiHexDigitLower);
        return valid
            ? trimmed
            : throw new OnboardingApplicationConflictException("document_content_hash_invalid", "A document requires a lowercase SHA-256 content hash.");
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgres
        && postgres.SqlState == PostgresErrorCodes.UniqueViolation;

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
