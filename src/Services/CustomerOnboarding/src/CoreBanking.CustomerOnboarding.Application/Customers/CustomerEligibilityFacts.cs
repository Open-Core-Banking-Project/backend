using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CoreBanking.CustomerOnboarding.Application.Customers;

/// <summary>
/// Authoritative customer facts a product or account decision may use. Every classified customer has
/// an effective classification; when none is effective the query raises
/// <see cref="CustomerEligibilityFactsException"/> with <c>customer_classification_unavailable</c>.
/// Decision: P1-E04-D17.
/// </summary>
public sealed record CustomerEligibilityFacts(
    string InstitutionId,
    string CustomerId,
    string CustomerStatus,
    int? EffectiveClassificationVersion,
    string KycTier,
    string Segment,
    string RiskClass,
    bool RequiresEnhancedDueDiligence,
    string CustomerType,
    int AgeYears,
    string FactsVersion,
    DateTimeOffset AsOf,
    DateTimeOffset ValidUntil);

/// <summary>Reads authoritative facts for one customer. Absence means the customer is not visible in the institution.</summary>
public interface ICustomerEligibilityFactsQuery
{
    Task<CustomerEligibilityFacts?> GetAsync(
        string institutionId,
        string customerId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken);
}

/// <summary>A fact the owner cannot prove. The code is stable and carries no personal data.</summary>
public sealed class CustomerEligibilityFactsException : Exception
{
    public CustomerEligibilityFactsException(string code, string message) : base(message) => Code = code;

    public CustomerEligibilityFactsException(string code, string message, Exception inner) : base(message, inner) => Code = code;

    public string Code { get; }
}

/// <summary>The approved classification state a decision may read.</summary>
public sealed record CustomerClassificationFacts(
    int Version,
    string Tier,
    string Segment,
    string RiskClass,
    bool RequiresEnhancedDueDiligence,
    string Status,
    DateTimeOffset? ApprovedAt);

/// <summary>Pure rules behind the facts snapshot: effective selection, age, version and validity window.</summary>
public static class CustomerEligibilityFactsPolicy
{
    public const string ApprovedClassificationStatus = "approved";
    public const string FactsVersionPrefix = "c";

    /// <summary>Matches the approved 60-second ceiling. A consumer may never extend it.</summary>
    public static readonly TimeSpan ValidityWindow = TimeSpan.FromSeconds(60);

    public static string FactsVersion(long concurrencyVersion) =>
        string.Concat(FactsVersionPrefix, concurrencyVersion.ToString(CultureInfo.InvariantCulture));

    public static DateTimeOffset ValidUntil(DateTimeOffset asOf) => asOf.Add(ValidityWindow);

    /// <summary>
    /// The approved classification effective at the instant: approved before or at <paramref name="asOf"/>,
    /// highest version first. Unapproved or future approvals never qualify.
    /// </summary>
    public static CustomerClassificationFacts? SelectEffective(
        IEnumerable<CustomerClassificationFacts> classifications,
        DateTimeOffset asOf)
    {
        ArgumentNullException.ThrowIfNull(classifications);

        return classifications
            .Where(classification => string.Equals(classification.Status, ApprovedClassificationStatus, StringComparison.Ordinal))
            .Where(classification => classification.ApprovedAt is not null && classification.ApprovedAt <= asOf)
            .OrderByDescending(classification => classification.Version)
            .FirstOrDefault();
    }

    /// <summary>Completed years at the instant. A future birth date is invalid, not a negative age.</summary>
    public static int ComputeAgeYears(DateOnly birthDate, DateOnly asOf)
    {
        if (birthDate > asOf)
        {
            throw new CustomerEligibilityFactsException(
                "customer_birth_date_invalid",
                "The stored birth date is later than the evaluation instant.");
        }

        int age = asOf.Year - birthDate.Year;
        // A leap-day birth date ages on the first of March in a non-leap year, so compare month and
        // day instead of relying on calendar-year clamping.
        if (asOf.Month < birthDate.Month || (asOf.Month == birthDate.Month && asOf.Day < birthDate.Day))
        {
            age--;
        }

        return age;
    }
}

/// <summary>Claims a service caller must prove before the facts route answers.</summary>
public sealed record ServiceCallerClaims(
    string? ActorType,
    string? ClientId,
    string? InstitutionId,
    IReadOnlyCollection<string> Audiences,
    IReadOnlyCollection<string> Scopes);

/// <summary>The authorisation outcome for one service call.</summary>
public sealed record ServiceCallDecision(bool IsAllowed, string? Code, string? InstitutionId, string? ClientId)
{
    public static ServiceCallDecision Allow(string institutionId, string clientId) => new(true, null, institutionId, clientId);

    public static ServiceCallDecision Deny(string code) => new(false, code, null, null);
}

/// <summary>
/// Audience-bound, allow-listed service authorisation for the internal facts route. Decision: P1-E04-D17.
/// </summary>
public static class ServiceCallAuthorisation
{
    public const string RequiredCapability = "service.customer.eligibility_facts_read";
    public const string RequiredAudience = "corebanking-customer-api";
    public const string ServiceActorType = "service";

    public static ServiceCallDecision Authorise(
        ServiceCallerClaims? claims,
        IReadOnlyCollection<string> allowedClientIds)
    {
        ArgumentNullException.ThrowIfNull(allowedClientIds);

        if (claims is null)
        {
            return ServiceCallDecision.Deny("service_authentication_required");
        }

        if (!string.Equals(claims.ActorType, ServiceActorType, StringComparison.Ordinal))
        {
            return ServiceCallDecision.Deny("service_actor_required");
        }

        if (string.IsNullOrWhiteSpace(claims.ClientId)
            || !allowedClientIds.Contains(claims.ClientId, StringComparer.Ordinal))
        {
            return ServiceCallDecision.Deny("service_client_not_allowed");
        }

        if (!claims.Audiences.Contains(RequiredAudience, StringComparer.Ordinal))
        {
            return ServiceCallDecision.Deny("service_audience_mismatch");
        }

        if (!claims.Scopes.Contains(RequiredCapability, StringComparer.Ordinal))
        {
            return ServiceCallDecision.Deny("service_capability_missing");
        }

        if (string.IsNullOrWhiteSpace(claims.InstitutionId))
        {
            return ServiceCallDecision.Deny("institution_context_missing");
        }

        return ServiceCallDecision.Allow(claims.InstitutionId, claims.ClientId);
    }
}
