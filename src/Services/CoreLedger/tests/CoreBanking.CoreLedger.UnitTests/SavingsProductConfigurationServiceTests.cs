using System;
using System.Linq;
using System.Threading;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.CoreLedger.Application.Products;
using CoreBanking.CoreLedger.Contracts;
using CoreBanking.CoreLedger.Domain.Products;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using CoreBanking.CoreLedger.Infrastructure.Products;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CoreBanking.CoreLedger.UnitTests;

public sealed class SavingsProductConfigurationServiceTests
{
    [Fact]
    public void Public_draft_contract_cannot_assign_approval_status_or_tenant_identity()
    {
        string[] properties = typeof(CreateSavingsEligibilityDraftRequestV1)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain("InstitutionId", properties);
        Assert.DoesNotContain("Status", properties);
        Assert.DoesNotContain("ApprovedByActorId", properties);
        Assert.DoesNotContain("ApprovalReference", properties);
    }

    [Fact]
    public void Savings_approval_contract_is_exactly_pinned()
    {
        Assert.Equal("product.savings.eligibility.publish.v1", SavingsEligibilityApprovalV1.WorkflowKey);
        Assert.Equal("savings-eligibility-publication-v1", SavingsEligibilityApprovalV1.WorkflowVersionId);
        Assert.Equal("product.savings.eligibility.version", SavingsEligibilityApprovalV1.SubjectType);
        Assert.Equal("product.version.approve", SavingsEligibilityApprovalV1.RequiredPermission);
        Assert.Equal(64, SavingsEligibilityApprovalV1.WorkflowDefinitionHash.Length);
        Assert.Equal("corebanking.core-ledger.approval.v1", CoreBanking.BuildingBlocks.Application.Messaging.EventContract.TopicFor("core-ledger", "approval", 1));
        Assert.Equal("corebanking.operations-workflow.approval.v1", SavingsEligibilityApprovalDecidedConsumer.TopicName);
    }

    [Fact]
    public void Product_publication_contract_preserves_the_exact_approved_version_and_period()
    {
        DateTimeOffset effectiveFrom = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        ProductVersionPublishedV1 payload = new(
            "institution-1",
            "product-1",
            "SAVINGS-001",
            "savings",
            "version-1",
            1,
            new string('a', 64),
            effectiveFrom,
            effectiveFrom.AddMonths(6),
            "publisher-1",
            "approval-request-1",
            effectiveFrom.AddDays(-1));

        ProductVersionPublishedV1 restored = EventContract.Deserialize<ProductVersionPublishedV1>(
            EventContract.Serialize(payload))!;

        Assert.Equal(payload, restored);
        Assert.Equal("corebanking.core-ledger.product.v1", EventContract.TopicFor("core-ledger", "product", 1));
    }

    [Fact]
    public async System.Threading.Tasks.Task Publication_fails_closed_for_a_cross_tenant_target()
    {
        AmbientTenantContext tenantContext = new();
        tenantContext.Establish(InstitutionId.From("institution-trusted"));
        await using CoreLedgerDbContext context = CreateContext(tenantContext);
        SavingsProductConfigurationService service = new(
            context,
            new IdempotencyService(context, TimeProvider.System),
            tenantContext,
            TimeProvider.System);
        PublishSavingsEligibilityCommand command = new(
            InstitutionId.From("institution-untrusted"),
            ActorId.From("product-publisher"),
            "idempotency-1",
            "publication-1",
            "version-1",
            new string('a', 64),
            new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero),
            null,
            "correlation-1");

        SavingsProductConfigurationRejectedException exception = await Assert.ThrowsAsync<SavingsProductConfigurationRejectedException>(
            () => service.PublishAsync(command, CancellationToken.None));

        Assert.Equal("cross_tenant_access_denied", exception.Code);
    }

    [Fact]
    public async System.Threading.Tasks.Task Eligibility_evaluation_fails_closed_before_querying_a_cross_tenant_product()
    {
        AmbientTenantContext tenantContext = new();
        tenantContext.Establish(InstitutionId.From("institution-trusted"));
        await using CoreLedgerDbContext context = CreateContext(tenantContext);
        SavingsEligibilityEvaluationService service = new(context, tenantContext);
        DateTimeOffset evaluatedAt = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        EvaluateSavingsEligibilityCommand command = new(
            InstitutionId.From("institution-untrusted"),
            "SAVINGS-001",
            new AuthoritativeProductEligibilityFacts(
                "customer-v1",
                "branch-v1",
                "currency-v1",
                evaluatedAt.AddMinutes(1),
                "INDIVIDUAL",
                "KYC-2",
                "RETAIL",
                "LOW",
                30,
                "LAGOS-001",
                "NG",
                "NGN"),
            evaluatedAt);

        SavingsEligibilityEvaluationRejectedException exception = await Assert.ThrowsAsync<SavingsEligibilityEvaluationRejectedException>(
            () => service.EvaluateAsync(command, CancellationToken.None));

        Assert.Equal("cross_tenant_access_denied", exception.Code);
    }

    [Fact]
    public async System.Threading.Tasks.Task Draft_creation_fails_closed_for_a_cross_tenant_target()
    {
        AmbientTenantContext tenantContext = new();
        tenantContext.Establish(InstitutionId.From("institution-trusted"));
        await using CoreLedgerDbContext context = CreateContext(tenantContext);
        SavingsProductConfigurationService service = new(
            context,
            new IdempotencyService(context, TimeProvider.System),
            tenantContext,
            TimeProvider.System);
        CreateSavingsEligibilityDraftCommand command = new(
            InstitutionId.From("institution-untrusted"),
            ActorId.From("product-maker"),
            "idempotency-1",
            "product-1",
            "SAVINGS-001",
            "version-1",
            1,
            Rules(),
            "correlation-1");

        SavingsProductConfigurationRejectedException exception = await Assert.ThrowsAsync<SavingsProductConfigurationRejectedException>(
            () => service.CreateDraftAsync(command, CancellationToken.None));

        Assert.Equal("cross_tenant_access_denied", exception.Code);
    }

    private static ProductEligibilityRules Rules() => ProductEligibilityRules.Create(
        "savings-eligibility", 1,
        EligibilityCodeCriterion.Only(["INDIVIDUAL"]),
        EligibilityCodeCriterion.Only(["KYC-2"]),
        EligibilityCodeCriterion.Any(),
        EligibilityCodeCriterion.Any(),
        EligibilityAgeCriterion.Between(18, 65),
        EligibilityCodeCriterion.Any(),
        EligibilityCodeCriterion.Only(["NG"]),
        EligibilityCodeCriterion.Only(["NGN"]));

    private static CoreLedgerDbContext CreateContext(AmbientTenantContext tenantContext)
    {
        DbContextOptions<CoreLedgerDbContext> options = new DbContextOptionsBuilder<CoreLedgerDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=model_only;Username=model;Password=model")
            .Options;
        return new CoreLedgerDbContext(options, tenantContext);
    }
}
