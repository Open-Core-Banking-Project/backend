using System;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CoreLedger.Application.Products;

namespace CoreBanking.CoreLedger.Api.Authorisation;

/// <summary>
/// Reads authoritative customer facts from their owner over the approved internal route. A non-success
/// answer becomes a stable rejection; it is never retried into a cached value. Decision: P1-E04-D17.
/// </summary>
public sealed class RemoteCustomerEligibilityFactsSource : ICustomerEligibilityFactsSource
{
    private const string RoutePrefix = "/internal/v1/customers/";
    private const string RouteSuffix = "/eligibility-facts";

    private readonly HttpClient _client;

    public RemoteCustomerEligibilityFactsSource(HttpClient client) =>
        _client = client ?? throw new ArgumentNullException(nameof(client));

    public async Task<CustomerFactsLookup> GetAsync(
        InstitutionId institutionId,
        string customerId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);

        string path = string.Concat(
            RoutePrefix,
            Uri.EscapeDataString(customerId),
            RouteSuffix,
            "?asOf=",
            Uri.EscapeDataString(asOf.ToString("O", CultureInfo.InvariantCulture)));

        using HttpRequestMessage request = new(HttpMethod.Get, path);
        request.Headers.Add("X-Institution-Id", institutionId.Value);

        HttpResponseMessage response;
        try
        {
            response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return CustomerFactsLookup.Rejected("eligibility_facts_unavailable");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return CustomerFactsLookup.Rejected("eligibility_facts_unavailable");
        }

        using (response)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return CustomerFactsLookup.Rejected("customer_facts_not_found");
            }

            if (response.StatusCode is System.Net.HttpStatusCode.Conflict
                or System.Net.HttpStatusCode.UnprocessableEntity)
            {
                return CustomerFactsLookup.Rejected(await ReadCodeAsync(response, cancellationToken).ConfigureAwait(false)
                    ?? "eligibility_facts_unavailable");
            }

            if (!response.IsSuccessStatusCode)
            {
                return CustomerFactsLookup.Rejected("eligibility_facts_unavailable");
            }

            CustomerEligibilityFactsWireV1? facts;
            try
            {
                facts = await response.Content
                    .ReadFromJsonAsync<CustomerEligibilityFactsWireV1>(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (System.Text.Json.JsonException)
            {
                return CustomerFactsLookup.Rejected("eligibility_facts_malformed");
            }

            if (facts is null
                || facts.SchemaVersion != 1
                || string.IsNullOrWhiteSpace(facts.InstitutionId)
                || string.IsNullOrWhiteSpace(facts.CustomerId)
                || string.IsNullOrWhiteSpace(facts.CustomerStatus)
                || string.IsNullOrWhiteSpace(facts.KycTier)
                || string.IsNullOrWhiteSpace(facts.Segment)
                || string.IsNullOrWhiteSpace(facts.RiskClass)
                || string.IsNullOrWhiteSpace(facts.CustomerType)
                || string.IsNullOrWhiteSpace(facts.FactsVersion)
                || facts.AgeYears < 0)
            {
                return CustomerFactsLookup.Rejected("eligibility_facts_malformed");
            }

            return CustomerFactsLookup.Found(new CustomerEligibilitySnapshot(
                facts.InstitutionId,
                facts.CustomerId,
                facts.CustomerStatus,
                facts.KycTier,
                facts.Segment,
                facts.RiskClass,
                facts.RequiresEnhancedDueDiligence,
                facts.CustomerType,
                facts.AgeYears,
                facts.FactsVersion,
                facts.AsOf,
                facts.ValidUntil));
        }
    }

    private static async Task<string?> ReadCodeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            CustomerFactsErrorWireV1? error = await response.Content
                .ReadFromJsonAsync<CustomerFactsErrorWireV1>(cancellationToken)
                .ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(error?.Code) ? null : error!.Code;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
