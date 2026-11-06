using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.OperationsWorkflow.Api.Authorisation;
using CoreBanking.OperationsWorkflow.Application.Approvals;
using Xunit;

namespace CoreBanking.OperationsWorkflow.UnitTests;

[Trait("Category", "Unit")]
public sealed class ApprovalEligibilityAdapterTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly InstitutionId Institution = InstitutionId.From("institution-1");
    private static readonly ActorId Candidate = ActorId.From("approver-1");
    private static readonly ActorId Maker = ActorId.From("maker-1");

    [Fact]
    public async Task An_exact_identity_answer_is_accepted()
    {
        StubHandler handler = new(HttpStatusCode.OK, new ApprovalEligibilityResponseV1
        {
            Outcome = ApprovalEligibilityOutcomeV1.Eligible,
            Code = ApprovalEligibilityCodeV1.Eligible,
            RequiredPermission = "product.version.approve",
            SourceSystem = "operations-workflow",
            SourceRequestId = "request-1",
            MakerSeparated = true,
            EvaluatedEntitlementVersion = 7,
            EvaluatedAt = DateTimeOffset.UtcNow,
        });
        IdentityApprovalEligibilityVerifier verifier = Create(handler);

        ApprovalEligibilityVerification result = await verifier.VerifyAsync(
            Institution, Candidate, Maker, "request-1", "product.version.approve", CancellationToken.None);

        Assert.True(result.IsEligible);
        Assert.Equal(7, result.EvaluatedEntitlementVersion);
        ServiceApprovalEligibilityRequestV1 request = Assert.IsType<ServiceApprovalEligibilityRequestV1>(handler.Request);
        Assert.Equal(Candidate.Value, request.CandidateActorId);
        Assert.Equal(Maker.Value, request.MakerActorId);
    }

    [Fact]
    public async Task A_mismatched_identity_answer_fails_closed()
    {
        StubHandler handler = new(HttpStatusCode.OK, new ApprovalEligibilityResponseV1
        {
            Outcome = ApprovalEligibilityOutcomeV1.Eligible,
            Code = ApprovalEligibilityCodeV1.Eligible,
            RequiredPermission = "product.version.approve",
            SourceSystem = "operations-workflow",
            SourceRequestId = "another-request",
            MakerSeparated = true,
            EvaluatedEntitlementVersion = 7,
            EvaluatedAt = DateTimeOffset.UtcNow,
        });

        ApprovalEligibilityVerification result = await Create(handler).VerifyAsync(
            Institution, Candidate, Maker, "request-1", "product.version.approve", CancellationToken.None);

        Assert.False(result.IsEligible);
        Assert.Equal("auth.identity_response_mismatch", result.Code);
    }

    [Fact]
    public async Task An_identity_failure_fails_closed()
    {
        ApprovalEligibilityVerification result = await Create(new StubHandler(HttpStatusCode.ServiceUnavailable, null))
            .VerifyAsync(Institution, Candidate, Maker, "request-1", "product.version.approve", CancellationToken.None);

        Assert.False(result.IsEligible);
        Assert.Equal("auth.identity_unavailable", result.Code);
    }

    private static IdentityApprovalEligibilityVerifier Create(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://identity.test") });

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly object? _response;

        public StubHandler(HttpStatusCode status, object? response)
        {
            _status = status;
            _response = response;
        }

        public object? Request { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                string json = await request.Content.ReadAsStringAsync(cancellationToken);
                Request = JsonSerializer.Deserialize<ServiceApprovalEligibilityRequestV1>(json, SerializerOptions);
            }

            HttpResponseMessage response = new(_status);
            if (_response is not null)
            {
                response.Content = new StringContent(JsonSerializer.Serialize(_response));
                response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            }

            return response;
        }
    }
}
