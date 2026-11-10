using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.OperationsWorkflow.Application.Approvals;

namespace CoreBanking.OperationsWorkflow.Api.Authorisation;

/// <summary>
/// Calls Identity and Access through its service-authenticated eligibility boundary. Missing,
/// malformed or mismatched answers fail closed and never fall back to Workflow-owned policy.
/// </summary>
public sealed class IdentityApprovalEligibilityVerifier : IApprovalEligibilityVerifier
{
    private const string SourceSystem = "operations-workflow";
    private readonly HttpClient _client;

    public IdentityApprovalEligibilityVerifier(HttpClient client) =>
        _client = client ?? throw new ArgumentNullException(nameof(client));

    public async Task<ApprovalEligibilityVerification> VerifyAsync(
        InstitutionId institutionId,
        ActorId candidateActorId,
        ActorId makerActorId,
        string sourceRequestId,
        string requiredPermission,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentNullException.ThrowIfNull(candidateActorId);
        ArgumentNullException.ThrowIfNull(makerActorId);

        try
        {
            using HttpRequestMessage message = new(
                HttpMethod.Post,
                "/internal/v1/approvals/eligibility")
            {
                Content = JsonContent.Create(new ServiceApprovalEligibilityRequestV1
                {
                    InstitutionId = institutionId.Value,
                    SourceSystem = SourceSystem,
                    SourceRequestId = sourceRequestId,
                    RequiredPermission = requiredPermission,
                    MakerActorId = makerActorId.Value,
                    CandidateActorId = candidateActorId.Value,
                }),
            };
            message.Headers.Add("X-Institution-Id", institutionId.Value);

            using HttpResponseMessage response = await _client.SendAsync(message, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return Denied("auth.identity_unavailable");
            }

            ApprovalEligibilityResponseV1? result = await response.Content
                .ReadFromJsonAsync<ApprovalEligibilityResponseV1>(cancellationToken)
                .ConfigureAwait(false);
            if (result is null
                || !string.Equals(result.SourceSystem, SourceSystem, StringComparison.Ordinal)
                || !string.Equals(result.SourceRequestId, sourceRequestId, StringComparison.Ordinal)
                || !string.Equals(result.RequiredPermission, requiredPermission, StringComparison.Ordinal)
                || (string.Equals(result.Outcome, ApprovalEligibilityOutcomeV1.Eligible, StringComparison.Ordinal)
                    && (!result.MakerSeparated || result.EvaluatedEntitlementVersion <= 0)))
            {
                return Denied("auth.identity_response_mismatch");
            }

            return new ApprovalEligibilityVerification(
                string.Equals(result.Outcome, ApprovalEligibilityOutcomeV1.Eligible, StringComparison.Ordinal),
                result.Code,
                result.EvaluatedEntitlementVersion);
        }
        catch (HttpRequestException)
        {
            return Denied("auth.identity_unavailable");
        }
        catch (InvalidOperationException)
        {
            return Denied("auth.identity_unavailable");
        }
        catch (JsonException)
        {
            return Denied("auth.identity_response_mismatch");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Denied("auth.identity_unavailable");
        }
    }

    private static ApprovalEligibilityVerification Denied(string code) => new(false, code, 0);
}

/// <summary>Explicit Development-only stand-in; production always uses Identity and Access.</summary>
public sealed class DevelopmentApprovalEligibilityVerifier : IApprovalEligibilityVerifier
{
    public Task<ApprovalEligibilityVerification> VerifyAsync(
        InstitutionId institutionId,
        ActorId candidateActorId,
        ActorId makerActorId,
        string sourceRequestId,
        string requiredPermission,
        CancellationToken cancellationToken) =>
        Task.FromResult(candidateActorId == makerActorId
            ? new ApprovalEligibilityVerification(false, ApprovalEligibilityCodeV1.SegregationOfDuties, 0)
            : new ApprovalEligibilityVerification(true, ApprovalEligibilityCodeV1.Eligible, 1));
}
