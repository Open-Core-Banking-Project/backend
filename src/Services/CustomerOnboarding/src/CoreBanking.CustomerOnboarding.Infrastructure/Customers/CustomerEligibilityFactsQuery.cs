using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.CustomerOnboarding.Application.Customers;
using CoreBanking.CustomerOnboarding.Application.Onboarding;
using CoreBanking.CustomerOnboarding.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.CustomerOnboarding.Infrastructure.Customers;

/// <summary>
/// Resolves the authoritative facts for one customer from owned state only: the customer relationship,
/// its approved classification and the protected birth date. It never reads a request-supplied value.
/// Decision: P1-E04-D17.
/// </summary>
public sealed class CustomerEligibilityFactsQuery : ICustomerEligibilityFactsQuery
{
    private const string BirthDateFormat = "yyyy-MM-dd";

    private readonly CustomerOnboardingDbContext _context;
    private readonly IProtectedValueProtector _protector;

    public CustomerEligibilityFactsQuery(
        CustomerOnboardingDbContext context,
        IProtectedValueProtector protector)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
    }

    public async Task<CustomerEligibilityFacts?> GetAsync(
        string institutionId,
        string customerId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(institutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);

        CustomerRecord? customer = await _context.Customers
            .AsNoTracking()
            .Include(record => record.Classifications)
            .SingleOrDefaultAsync(
                record => record.InstitutionId == institutionId && record.CustomerId == customerId,
                cancellationToken)
            .ConfigureAwait(false);
        if (customer is null)
        {
            return null;
        }

        PartyRecord? party = await _context.Parties
            .AsNoTracking()
            .SingleOrDefaultAsync(
                record => record.InstitutionId == institutionId && record.PartyId == customer.PartyId,
                cancellationToken)
            .ConfigureAwait(false);
        PartyIndividualRecord? individual = await _context.PartyIndividuals
            .AsNoTracking()
            .SingleOrDefaultAsync(
                record => record.InstitutionId == institutionId && record.PartyId == customer.PartyId,
                cancellationToken)
            .ConfigureAwait(false);
        if (party is null || individual is null)
        {
            throw new CustomerEligibilityFactsException(
                "customer_party_unavailable",
                "The customer relationship has no complete party record.");
        }

        CustomerClassificationFacts? classification = CustomerEligibilityFactsPolicy.SelectEffective(
            customer.Classifications.Select(record => new CustomerClassificationFacts(
                record.Version,
                record.Tier,
                record.Segment,
                record.RiskClass,
                record.RequiresEnhancedDueDiligence,
                record.Status,
                record.ApprovedAt)),
            asOf);
        if (classification is null)
        {
            throw new CustomerEligibilityFactsException(
                "customer_classification_unavailable",
                "No approved classification is effective at the requested instant.");
        }

        DateOnly birthDate = ReadBirthDate(individual.BirthDateToken);
        int ageYears = CustomerEligibilityFactsPolicy.ComputeAgeYears(birthDate, DateOnly.FromDateTime(asOf.UtcDateTime));

        return new CustomerEligibilityFacts(
            customer.InstitutionId,
            customer.CustomerId,
            customer.Status,
            classification.Version,
            classification.Tier,
            classification.Segment,
            classification.RiskClass,
            classification.RequiresEnhancedDueDiligence,
            party.PartyType,
            ageYears,
            CustomerEligibilityFactsPolicy.FactsVersion(customer.ConcurrencyVersion),
            asOf,
            CustomerEligibilityFactsPolicy.ValidUntil(asOf));
    }

    private DateOnly ReadBirthDate(string birthDateToken)
    {
        string plaintext;
        try
        {
            plaintext = _protector.Unprotect(birthDateToken);
        }
        catch (ProtectedValueException exception)
        {
            throw new CustomerEligibilityFactsException(
                "customer_birth_date_unavailable",
                "The stored birth date could not be recovered.",
                exception);
        }

        if (!DateOnly.TryParseExact(plaintext, BirthDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly birthDate))
        {
            throw new CustomerEligibilityFactsException(
                "customer_birth_date_invalid",
                "The stored birth date does not use the approved format.");
        }

        return birthDate;
    }
}
