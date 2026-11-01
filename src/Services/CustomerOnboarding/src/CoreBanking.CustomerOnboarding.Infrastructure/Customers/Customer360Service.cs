using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.CustomerOnboarding.Application.Customers;
using CoreBanking.CustomerOnboarding.Application.Onboarding;
using CoreBanking.CustomerOnboarding.Domain.Customers;
using CoreBanking.CustomerOnboarding.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.CustomerOnboarding.Infrastructure.Customers;

/// <summary>
/// Read-only Customer 360 composition. Three sections are answered from this service's own tables; the rest are
/// reported as unavailable with a stable reason until their owning service publishes a read contract. Nothing
/// here writes, and only masked or aggregate values leave the service. Decision: P1-E03-D13.
/// </summary>
public sealed class Customer360Service : ICustomer360Service
{
    private const string OwnedSource = "customer-onboarding";
    private const string UnavailableReason = "source_not_integrated";
    private const int MaximumSections = 8;

    private static readonly Dictionary<string, SectionDefinition> Sections = new(StringComparer.Ordinal)
    {
        ["profile"] = new("profile", OwnedSource, "customer.profile.view", IsAvailable: true),
        ["kyc"] = new("kyc", OwnedSource, "customer.kyc.view", IsAvailable: true),
        ["merges"] = new("merges", OwnedSource, "customer.merge.propose", IsAvailable: true),
        ["accounts"] = new("accounts", "core-banking", "customer.profile.view", IsAvailable: false),
        ["cases"] = new("cases", "operations-workflow", "customer.profile.view", IsAvailable: false),
        ["risk"] = new("risk", "risk-reporting", "customer.profile.view", IsAvailable: false),
        ["documents"] = new("documents", "document-storage", "customer.kyc.view", IsAvailable: false),
        ["channels"] = new("channels", "channels-engagement", "customer.profile.view", IsAvailable: false),
    };

    private readonly CustomerOnboardingDbContext _context;
    private readonly TimeProvider _timeProvider;

    public Customer360Service(CustomerOnboardingDbContext context, TimeProvider timeProvider)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<Customer360View> GetAsync(GetCustomer360Query query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(query.Sections);

        if (query.Sections.Count == 0)
        {
            return new Customer360View(query.CustomerId, _timeProvider.GetUtcNow(), []);
        }

        if (query.Sections.Count > MaximumSections)
        {
            throw Conflict("customer360_section_invalid", "A Customer 360 request is limited to the defined sections.");
        }

        List<string> requested = [];
        foreach (string section in query.Sections)
        {
            string code = (section ?? string.Empty).Trim().ToLowerInvariant();
            if (!Sections.ContainsKey(code))
            {
                throw Conflict("customer360_section_unknown", "The requested section is not part of Customer 360.");
            }

            if (!requested.Contains(code, StringComparer.Ordinal))
            {
                requested.Add(code);
            }
        }

        CustomerRecord customer = await _context.Customers
            .AsNoTracking()
            .Include(record => record.Classifications)
            .SingleOrDefaultAsync(record => record.CustomerId == query.CustomerId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new CustomerNotFoundException(query.CustomerId);

        DateTimeOffset now = _timeProvider.GetUtcNow();
        List<Customer360SectionView> sections = [];

        foreach (string code in requested)
        {
            SectionDefinition definition = Sections[code];
            sections.Add(definition.IsAvailable
                ? await BuildOwnedSectionAsync(code, definition, customer, now, cancellationToken).ConfigureAwait(false)
                : Unavailable(definition));
        }

        return new Customer360View(customer.CustomerId, now, sections);
    }

    private async Task<Customer360SectionView> BuildOwnedSectionAsync(
        string code,
        SectionDefinition definition,
        CustomerRecord customer,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        code switch
        {
            "profile" => await ProfileAsync(definition, customer, now, cancellationToken).ConfigureAwait(false),
            "kyc" => await KycAsync(definition, customer, now, cancellationToken).ConfigureAwait(false),
            "merges" => await MergesAsync(definition, customer, now, cancellationToken).ConfigureAwait(false),
            _ => throw Conflict("customer360_section_unknown", "The requested section is not part of Customer 360."),
        };

    private async Task<Customer360SectionView> ProfileAsync(
        SectionDefinition definition,
        CustomerRecord customer,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string> fields = new(StringComparer.Ordinal)
        {
            ["customerNumber"] = customer.CustomerNumber,
            ["status"] = customer.Status,
            ["partyId"] = customer.PartyId,
        };

        if (!string.IsNullOrWhiteSpace(customer.BranchId))
        {
            fields["branchId"] = customer.BranchId;
        }

        CustomerClassificationRecord? classification = customer.Classifications
            .Where(candidate => candidate.Status == "approved")
            .OrderByDescending(candidate => candidate.Version)
            .FirstOrDefault();

        if (classification is not null)
        {
            fields["classificationVersion"] = classification.Version.ToString(CultureInfo.InvariantCulture);
            fields["tier"] = classification.Tier;
            fields["segment"] = classification.Segment;
            fields["riskClass"] = classification.RiskClass;
            fields["enhancedDueDiligence"] = classification.RequiresEnhancedDueDiligence ? "true" : "false";
        }

        List<PartyContactRecord> contacts = await _context.PartyContacts
            .AsNoTracking()
            .Where(contact => contact.PartyId == customer.PartyId && contact.EffectiveTo == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (PartyContactRecord contact in contacts)
        {
            fields.TryAdd("masked" + char.ToUpperInvariant(contact.ContactType[0]) + contact.ContactType[1..], contact.MaskedValue);
        }

        PartyAddressRecord? address = await _context.PartyAddresses
            .AsNoTracking()
            .Where(candidate => candidate.PartyId == customer.PartyId && candidate.EffectiveTo == null)
            .OrderBy(candidate => candidate.AddressType)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (address is not null)
        {
            fields["maskedAddress"] = address.MaskedLine;
            fields["country"] = address.CountryCode;
        }

        return Available(definition, now, fields);
    }

    private async Task<Customer360SectionView> KycAsync(
        SectionDefinition definition,
        CustomerRecord customer,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        string? applicationId = await _context.OnboardingApplications
            .AsNoTracking()
            .Where(application => application.PartyId == customer.PartyId && application.Status == "submitted")
            .Select(application => application.ApplicationId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<string, string> fields = new(StringComparer.Ordinal);
        if (applicationId is null)
        {
            fields["caseStatus"] = "none";
            return Available(definition, now, fields);
        }

        KycCaseRecord? kycCase = await _context.KycCases
            .AsNoTracking()
            .Include(record => record.Requirements)
            .FirstOrDefaultAsync(record => record.ApplicationId == applicationId, cancellationToken)
            .ConfigureAwait(false);

        if (kycCase is null)
        {
            fields["caseStatus"] = "none";
            return Available(definition, now, fields);
        }

        fields["caseId"] = kycCase.CaseId;
        fields["caseStatus"] = kycCase.Status;
        fields["outstandingRequirements"] = kycCase.Requirements
            .Count(requirement => requirement.Status is not ("passed" or "waived"))
            .ToString(CultureInfo.InvariantCulture);

        foreach (IGrouping<string, KycRequirementRecord> group in kycCase.Requirements.GroupBy(requirement => requirement.Status))
        {
            fields["requirements_" + group.Key] = group.Count().ToString(CultureInfo.InvariantCulture);
        }

        return Available(definition, now, fields);
    }

    private async Task<Customer360SectionView> MergesAsync(
        SectionDefinition definition,
        CustomerRecord customer,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        int openMerges = await _context.MergeRequests
            .AsNoTracking()
            .CountAsync(
                request => request.SourceCustomerId == customer.CustomerId
                    && request.Status != "completed"
                    && request.Status != "rejected"
                    && request.Status != "withdrawn",
                cancellationToken)
            .ConfigureAwait(false);

        Dictionary<string, string> fields = new(StringComparer.Ordinal)
        {
            ["openMergeRequests"] = openMerges.ToString(CultureInfo.InvariantCulture),
        };

        CustomerAliasRecord? alias = await _context.CustomerAliases
            .AsNoTracking()
            .FirstOrDefaultAsync(record => record.AliasCustomerId == customer.CustomerId, cancellationToken)
            .ConfigureAwait(false);

        if (alias is not null)
        {
            fields["mergedInto"] = alias.SurvivorCustomerId;
        }

        return Available(definition, now, fields);
    }

    private static Customer360SectionView Available(
        SectionDefinition definition,
        DateTimeOffset now,
        IReadOnlyDictionary<string, string> fields) =>
        new(
            definition.Code,
            Customer360SectionStatus.Available,
            "available",
            definition.Source,
            definition.Permission,
            now,
            ReasonCode: null,
            fields);

    private static Customer360SectionView Unavailable(SectionDefinition definition) =>
        new(
            definition.Code,
            Customer360SectionStatus.Unavailable,
            "unavailable",
            definition.Source,
            definition.Permission,
            AsOf: null,
            UnavailableReason,
            new Dictionary<string, string>(StringComparer.Ordinal));

    private static OnboardingApplicationConflictException Conflict(string code, string message) => new(code, message);

    private sealed record SectionDefinition(string Code, string Source, string Permission, bool IsAvailable);
}
