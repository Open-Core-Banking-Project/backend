using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CustomerOnboarding.Application.Duplicates;
using CoreBanking.CustomerOnboarding.Application.Onboarding;
using CoreBanking.CustomerOnboarding.Domain;
using CoreBanking.CustomerOnboarding.Domain.Duplicates;
using CoreBanking.CustomerOnboarding.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Npgsql;

namespace CoreBanking.CustomerOnboarding.Infrastructure.Duplicates;

/// <summary>
/// Exact duplicate detection and reviewer decisions over protected tokens. Comparison never reads
/// plaintext, a candidate set is bounded, a decided set is final and a superseded set is retained as
/// evidence. Decision: P1-E03-D04, P1-E03-D05.
/// </summary>
public sealed class DuplicateDetectionService : IDuplicateDetectionService
{
    private const string LegalNameType = "legal";

    private readonly CustomerOnboardingDbContext _context;
    private readonly TimeProvider _timeProvider;

    public DuplicateDetectionService(CustomerOnboardingDbContext context, TimeProvider timeProvider)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<DuplicateCandidateSetView> DetectAsync(
        DetectDuplicateCandidatesCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        OnboardingApplicationRecord application = await _context.OnboardingApplications
            .AsNoTracking()
            .SingleOrDefaultAsync(record => record.ApplicationId == command.ApplicationId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new OnboardingApplicationNotFoundException(command.ApplicationId);

        if (string.IsNullOrWhiteSpace(application.PartyId))
        {
            throw Conflict(
                "duplicate_detection_requires_submitted_application",
                "Duplicate detection runs against a submitted application that has a recorded party.");
        }

        if (application.Revision != command.ExpectedRevision)
        {
            throw Conflict(
                "onboarding_revision_conflict",
                "The application changed since the revision the detection run was requested for.");
        }

        DuplicateSubject subject = await LoadSubjectAsync(application.PartyId, cancellationToken).ConfigureAwait(false)
            ?? throw Conflict(
                "duplicate_detection_subject_missing",
                "The application party is no longer visible in this institution.");

        IReadOnlyList<DuplicateSubject> related = await LoadRelatedSubjectsAsync(subject, cancellationToken).ConfigureAwait(false);
        DateTimeOffset now = _timeProvider.GetUtcNow();

        List<DuplicateCandidate> candidates = related
            .Select(candidate => new { Subject = candidate, Factors = DuplicateMatchRules.Compare(subject, candidate) })
            .Where(match => match.Factors.Count > 0)
            .Select(match => DuplicateCandidate.Create(match.Subject.PartyId, match.Factors, now))
            .OrderBy(candidate => candidate.CandidatePartyId, StringComparer.Ordinal)
            .ToList();

        DuplicateCandidateSetRecord? live = await _context.DuplicateCandidateSets
            .Include(set => set.Candidates)
            .SingleOrDefaultAsync(
                set => set.ApplicationId == application.ApplicationId
                    && set.ApplicationRevision == application.Revision
                    && set.Status != DuplicateCodes.Superseded,
                cancellationToken)
            .ConfigureAwait(false);

        if (live is not null)
        {
            if (live.Status == DuplicateCodes.Decided)
            {
                throw Conflict(
                    "duplicate_review_already_decided",
                    "This application revision already has a decided duplicate review; a new revision is required.");
            }

            DuplicateCandidateSet previous = ToModel(live, application);
            previous.Supersede(now);
            Apply(previous, live, now);
        }

        DuplicateCandidateSet set = DuplicateCandidateSet.Open(
            command.InstitutionId,
            application.ApplicationId,
            application.Revision,
            command.ActorId.Value,
            candidates,
            now);

        DuplicateCandidateSetRecord record = ToRecord(set);
        _context.DuplicateCandidateSets.Add(record);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (IsIntegrityConflict(exception))
        {
            DiscardPendingChanges();
            throw Conflict(
                "duplicate_detection_conflict",
                "Another detection run recorded a candidate set for this application revision.");
        }
        catch (DbUpdateException)
        {
            DiscardPendingChanges();
            throw;
        }

        return await ToViewAsync(record, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DuplicateCandidateSetView> DecideAsync(
        DecideDuplicateCandidatesCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        DuplicateCandidateSetRecord record = await _context.DuplicateCandidateSets
            .Include(set => set.Candidates)
            .SingleOrDefaultAsync(set => set.SetId == command.SetId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new DuplicateCandidateSetNotFoundException(command.SetId);

        OnboardingApplicationRecord application = await _context.OnboardingApplications
            .AsNoTracking()
            .SingleAsync(applicationRecord => applicationRecord.ApplicationId == record.ApplicationId, cancellationToken)
            .ConfigureAwait(false);

        DuplicateCandidateSet set = ToModel(record, application);
        DuplicateSubject? subject = string.IsNullOrWhiteSpace(application.PartyId)
            ? null
            : await LoadSubjectAsync(application.PartyId, cancellationToken).ConfigureAwait(false);

        await EnsureCandidatesAreCurrentAsync(set, subject, cancellationToken).ConfigureAwait(false);

        DateTimeOffset now = _timeProvider.GetUtcNow();
        try
        {
            set.Decide(command.Decision, command.ActorId.Value, command.Reason, command.ExpectedVersion, now);
        }
        catch (OnboardingRuleException exception)
        {
            throw Conflict(exception.Code, exception.Message);
        }

        Apply(set, record, now);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            DiscardPendingChanges();
            throw Conflict("duplicate_candidate_set_stale_version", "The candidate set was decided or replaced by another reviewer.");
        }

        return await ToViewAsync(record, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DuplicateCandidateSetView> GetAsync(
        GetDuplicateCandidateSetQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        DuplicateCandidateSetRecord record = await _context.DuplicateCandidateSets
            .AsNoTracking()
            .Include(set => set.Candidates)
            .SingleOrDefaultAsync(set => set.SetId == query.SetId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new DuplicateCandidateSetNotFoundException(query.SetId);

        return await ToViewAsync(record, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Rechecks the candidate evidence before a decision is recorded. Protected data that changed since
    /// detection makes the decision stale instead of silently deciding on old evidence.
    /// Decision: P1-E03-D04 section 3.
    /// </summary>
    private async Task EnsureCandidatesAreCurrentAsync(
        DuplicateCandidateSet set,
        DuplicateSubject? subject,
        CancellationToken cancellationToken)
    {
        if (subject is null)
        {
            throw Conflict("duplicate_candidates_stale", "The application party is no longer visible; rerun duplicate detection.");
        }

        IReadOnlyList<DuplicateSubject> current = await LoadSubjectsAsync(
            [.. set.Candidates.Select(candidate => candidate.CandidatePartyId)],
            cancellationToken).ConfigureAwait(false);

        foreach (DuplicateCandidate candidate in set.Candidates)
        {
            DuplicateSubject? candidateSubject = current.SingleOrDefault(subjectRecord =>
                string.Equals(subjectRecord.PartyId, candidate.CandidatePartyId, StringComparison.Ordinal));

            if (candidateSubject is null)
            {
                throw Conflict("duplicate_candidates_stale", "A candidate party is no longer visible; rerun duplicate detection.");
            }

            IReadOnlyList<DuplicateMatchFactor> factors = DuplicateMatchRules.Compare(subject, candidateSubject);
            if (!SameFactors(factors, candidate.Factors))
            {
                throw Conflict("duplicate_candidates_stale", "Candidate evidence changed since detection; rerun duplicate detection.");
            }
        }
    }

    private static bool SameFactors(
        IReadOnlyList<DuplicateMatchFactor> left,
        IReadOnlyList<DuplicateMatchFactor> right)
    {
        HashSet<string> leftCodes = [.. left.Select(factor => DuplicateCodes.ToCode(factor.Kind) + ":" + factor.Basis)];
        HashSet<string> rightCodes = [.. right.Select(factor => DuplicateCodes.ToCode(factor.Kind) + ":" + factor.Basis)];
        return leftCodes.SetEquals(rightCodes);
    }

    private async Task<DuplicateCandidateSetView> ToViewAsync(
        DuplicateCandidateSetRecord record,
        CancellationToken cancellationToken)
    {
        OnboardingApplicationRecord application = await _context.OnboardingApplications
            .AsNoTracking()
            .SingleAsync(applicationRecord => applicationRecord.ApplicationId == record.ApplicationId, cancellationToken)
            .ConfigureAwait(false);

        string[] candidatePartyIds = [.. record.Candidates.Select(candidate => candidate.CandidatePartyId)];
        List<DuplicateSubject> subjects = [.. await LoadSubjectsAsync(candidatePartyIds, cancellationToken).ConfigureAwait(false)];

        DuplicateSubject? subject = string.IsNullOrWhiteSpace(application.PartyId)
            ? null
            : await LoadSubjectAsync(application.PartyId, cancellationToken).ConfigureAwait(false);

        Dictionary<string, string> statuses = await _context.Parties
            .AsNoTracking()
            .Where(party => candidatePartyIds.Contains(party.PartyId))
            .Select(party => new { party.PartyId, party.Status })
            .ToDictionaryAsync(entry => entry.PartyId, entry => entry.Status, cancellationToken)
            .ConfigureAwait(false);

        List<DuplicateCandidateView> candidates = [];
        foreach (DuplicateCandidateRecord candidate in record.Candidates.OrderBy(candidate => candidate.CandidatePartyId, StringComparer.Ordinal))
        {
            IReadOnlyList<DuplicateMatchFactor> factors = DeserialiseFactors(candidate.Factors);
            DuplicateSubject? candidateSubject = subjects.SingleOrDefault(subjectRecord =>
                string.Equals(subjectRecord.PartyId, candidate.CandidatePartyId, StringComparison.Ordinal));

            IReadOnlyList<string> masked = subject is null || candidateSubject is null
                ? []
                : DuplicateMatchRules.MaskedMatches(subject, candidateSubject, factors);

            candidates.Add(new DuplicateCandidateView(
                candidate.CandidatePartyId,
                statuses.TryGetValue(candidate.CandidatePartyId, out string? status) ? status : "unknown",
                DuplicateCodes.FromBandCode(candidate.Band),
                candidate.Band,
                [.. factors.Select(factor => new DuplicateMatchFactorView(
                    factor.Kind,
                    DuplicateCodes.ToCode(factor.Kind),
                    factor.Basis))],
                masked));
        }

        return new DuplicateCandidateSetView(
            record.SetId,
            record.ApplicationId,
            record.ApplicationRevision,
            record.RuleVersion,
            DuplicateCodes.FromStatusCode(record.Status),
            record.Status,
            record.Version,
            record.CreatedAt,
            record.DecidedAt,
            record.DecidedBy,
            record.Decision is null ? null : DuplicateCodes.FromDecisionCode(record.Decision),
            record.Decision,
            record.DecisionReason,
            candidates);
    }

    private async Task<DuplicateSubject?> LoadSubjectAsync(string partyId, CancellationToken cancellationToken)
    {
        IReadOnlyList<DuplicateSubject> subjects = await LoadSubjectsAsync([partyId], cancellationToken).ConfigureAwait(false);
        return subjects.SingleOrDefault();
    }

    /// <summary>
    /// Loads the protected comparison view of the given parties: active contacts, active legal names,
    /// individual attributes and active addresses. No plaintext value is read.
    /// </summary>
    private async Task<IReadOnlyList<DuplicateSubject>> LoadSubjectsAsync(
        IReadOnlyCollection<string> partyIds,
        CancellationToken cancellationToken)
    {
        if (partyIds.Count == 0)
        {
            return [];
        }

        string[] ids = [.. partyIds];

        List<PartyRecord> parties = await _context.Parties
            .AsNoTracking()
            .Where(party => ids.Contains(party.PartyId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<PartyNameRecord> names = await _context.PartyNames
            .AsNoTracking()
            .Where(name => ids.Contains(name.PartyId) && name.EffectiveTo == null && name.NameType == LegalNameType)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<PartyIndividualRecord> individuals = await _context.PartyIndividuals
            .AsNoTracking()
            .Where(individual => ids.Contains(individual.PartyId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<PartyContactRecord> contacts = await _context.PartyContacts
            .AsNoTracking()
            .Where(contact => ids.Contains(contact.PartyId) && contact.EffectiveTo == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<PartyAddressRecord> addresses = await _context.PartyAddresses
            .AsNoTracking()
            .Where(address => ids.Contains(address.PartyId) && address.EffectiveTo == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. parties.Select(party =>
            {
                PartyIndividualRecord? individual = individuals.FirstOrDefault(row =>
                    string.Equals(row.PartyId, party.PartyId, StringComparison.Ordinal));
                PartyNameRecord? name = names.FirstOrDefault(row =>
                    string.Equals(row.PartyId, party.PartyId, StringComparison.Ordinal));

                return new DuplicateSubject(
                    party.PartyId,
                    name?.Normalised ?? string.Empty,
                    individual?.BirthDateLookupToken ?? string.Empty,
                    individual?.BirthYear ?? 0,
                    [.. contacts
                        .Where(contact => string.Equals(contact.PartyId, party.PartyId, StringComparison.Ordinal))
                        .Select(contact => new DuplicateContactToken(contact.ContactType, contact.LookupToken, contact.MaskedValue))],
                    [.. addresses
                        .Where(address => string.Equals(address.PartyId, party.PartyId, StringComparison.Ordinal))
                        .Select(address => new DuplicateAddressToken(address.LookupToken, address.MaskedLine))]);
            }),
        ];
    }

    /// <summary>
    /// The bounded candidate search: parties sharing a contact lookup token, an address line token or
    /// the same normalised legal name as the subject. Nothing else is scanned.
    /// Decision: P1-E03-D04-A.
    /// </summary>
    private async Task<IReadOnlyList<DuplicateSubject>> LoadRelatedSubjectsAsync(
        DuplicateSubject subject,
        CancellationToken cancellationToken)
    {
        List<string> contactTokens = [.. subject.Contacts
            .Select(contact => contact.LookupToken)
            .Where(token => !string.IsNullOrEmpty(token))
            .Distinct(StringComparer.Ordinal)];

        List<string> addressTokens = [.. subject.Addresses
            .Select(address => address.LineToken)
            .Where(token => !string.IsNullOrEmpty(token))
            .Distinct(StringComparer.Ordinal)];
        IQueryable<string> related = _context.PartyContacts
            .AsNoTracking()
            .Where(contact => contact.PartyId != subject.PartyId
                && contact.EffectiveTo == null
                && contactTokens.Contains(contact.LookupToken))
            .Select(contact => contact.PartyId)
            .Union(_context.PartyAddresses
                .AsNoTracking()
                .Where(address => address.PartyId != subject.PartyId
                    && address.EffectiveTo == null
                    && addressTokens.Contains(address.LookupToken))
                .Select(address => address.PartyId))
            .Union(_context.PartyNames
                .AsNoTracking()
                .Where(name => name.PartyId != subject.PartyId
                    && name.EffectiveTo == null
                    && name.NameType == LegalNameType
                    && !string.IsNullOrEmpty(subject.NormalisedName)
                    && name.Normalised == subject.NormalisedName)
                .Select(name => name.PartyId));

        List<string> partyIds = await related
            .OrderBy(partyId => partyId)
            .Take(DuplicateMatchRules.MaximumCandidates + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (partyIds.Count > DuplicateMatchRules.MaximumCandidates)
        {
            throw Conflict(
                "duplicate_candidates_too_many",
                "The exact-match filter returned more candidates than a review run may hold.");
        }

        return await LoadSubjectsAsync(partyIds, cancellationToken).ConfigureAwait(false);
    }

    private static DuplicateCandidateSet ToModel(DuplicateCandidateSetRecord record, OnboardingApplicationRecord application)
    {
        List<DuplicateCandidate> candidates =
        [
            .. record.Candidates.Select(candidate => DuplicateCandidate.Restore(
                candidate.CandidatePartyId,
                DeserialiseFactors(candidate.Factors),
                candidate.DetectedAt)),
        ];

        return DuplicateCandidateSet.Restore(
            InstitutionId.From(record.InstitutionId),
            record.SetId,
            application.ApplicationId,
            record.ApplicationRevision,
            record.RuleVersion,
            DuplicateCodes.FromStatusCode(record.Status),
            record.Version,
            record.CreatedBy,
            candidates,
            record.CreatedAt,
            record.DecidedAt,
            record.DecidedBy,
            record.Decision is null ? null : DuplicateCodes.FromDecisionCode(record.Decision),
            record.DecisionReason);
    }

    private static void Apply(DuplicateCandidateSet set, DuplicateCandidateSetRecord record, DateTimeOffset now)
    {
        record.Status = DuplicateCodes.ToCode(set.Status);
        record.Version = set.Version;
        record.UpdatedAt = now;
        record.DecidedAt = set.DecidedAt;
        record.DecidedBy = set.DecidedBy;
        record.Decision = set.Decision is null ? null : DuplicateCodes.ToCode(set.Decision.Value);
        record.DecisionReason = set.DecisionReason;
    }

    private static DuplicateCandidateSetRecord ToRecord(DuplicateCandidateSet set) => new()
    {
        InstitutionId = set.InstitutionId.Value,
        SetId = set.SetId,
        ApplicationId = set.ApplicationId,
        ApplicationRevision = set.ApplicationRevision,
        RuleVersion = set.RuleVersion,
        Status = DuplicateCodes.ToCode(set.Status),
        Version = set.Version,
        CreatedBy = set.CreatedBy,
        CreatedAt = set.CreatedAt,
        UpdatedAt = set.CreatedAt,
        DecidedAt = set.DecidedAt,
        DecidedBy = set.DecidedBy,
        Decision = set.Decision is null ? null : DuplicateCodes.ToCode(set.Decision.Value),
        DecisionReason = set.DecisionReason,
        Candidates =
        [
            .. set.Candidates.Select(candidate => new DuplicateCandidateRecord
            {
                InstitutionId = set.InstitutionId.Value,
                SetId = set.SetId,
                CandidatePartyId = candidate.CandidatePartyId,
                Band = DuplicateCodes.ToCode(candidate.Band),
                Factors = SerialiseFactors(candidate.Factors),
                DetectedAt = candidate.DetectedAt,
            }),
        ],
    };

    private static string SerialiseFactors(IReadOnlyList<DuplicateMatchFactor> factors) =>
        JsonSerializer.Serialize(factors.Select(factor => new DuplicateFactorPayload(
            DuplicateCodes.ToCode(factor.Kind),
            factor.Basis)));

    private static IReadOnlyList<DuplicateMatchFactor> DeserialiseFactors(string factors)
    {
        List<DuplicateFactorPayload>? payloads = JsonSerializer.Deserialize<List<DuplicateFactorPayload>>(factors);
        if (payloads is null || payloads.Count == 0)
        {
            throw new OnboardingRuleException("duplicate_factors_missing", "A stored candidate has no match factors.");
        }

        return
        [
            .. payloads.Select(payload => DuplicateMatchFactor.Create(
                DuplicateCodes.FromKindCode(payload.Kind),
                payload.Basis)),
        ];
    }

    private static bool IsIntegrityConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgres
        && postgres.SqlState is PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation;

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

    private sealed record DuplicateFactorPayload(string Kind, string Basis);
}
