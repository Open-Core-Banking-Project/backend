using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.CoreLedger.Application.Accounts;
using CoreBanking.CoreLedger.Application.Products;
using CoreBanking.CoreLedger.Domain.Accounts;
using CoreBanking.CoreLedger.Infrastructure.Accounts;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.CoreLedger.UnitTests.Persistence;

/// <summary>
/// Builds the approved-application and opened-account states the account database suites start from, so a suite
/// exercises controls, liens, summaries or statements against a real account row instead of a model.
/// </summary>
internal static class AccountDatabaseScenario
{
    internal static AccountApplicationService CreateApplicationService(
        CoreLedgerDbContext context,
        AmbientTenantContext tenantContext,
        TimeProvider timeProvider) =>
        new(
            context,
            new IdempotencyService(context, timeProvider),
            tenantContext,
            timeProvider,
            new AuthoritativeEligibilityFactsResolver(
                new FixedCustomerEligibilityFactsSource(),
                new FixedBranchReferenceSource(),
                new FixedCurrencyReferenceSource()),
            new FixedAccountOpeningPolicySource(),
            new AccountHoldingsReader(context));

    internal static AccountOpeningService CreateOpeningService(
        CoreLedgerDbContext context,
        AmbientTenantContext tenantContext,
        TimeProvider timeProvider) =>
        new(
            context,
            new IdempotencyService(context, timeProvider),
            tenantContext,
            timeProvider,
            new FixedAccountNumberSchemeSource(),
            new AccountNumberAllocator(context, timeProvider),
            new LedgerAccountInitialisationService(context, timeProvider));

    internal static async Task<long> CreateEligibleApplicationAsync(
        CoreLedgerDatabaseScope scope,
        string applicationId,
        string customerId,
        ActorId actor,
        TimeProvider timeProvider)
    {
        AmbientTenantContext tenantContext = scope.NewTenant();
        await using CoreLedgerDbContext context = scope.CreateContext(tenantContext);
        AccountApplicationService applications = CreateApplicationService(context, tenantContext, timeProvider);

        AccountApplicationCreatedResult created = await applications.CreateAsync(
            new CreateAccountApplicationCommand(
                scope.Institution,
                actor,
                applicationId + "-create",
                applicationId,
                applicationId + "-request",
                customerId,
                scope.ProductId,
                scope.BranchId,
                "NGN",
                "SAVINGS",
                "Individual",
                applicationId + "-create-correlation"),
            CancellationToken.None);

        AccountEligibilityResult evaluated = await applications.EvaluateAsync(
            new EvaluateAccountEligibilityCommand(
                scope.Institution,
                actor,
                applicationId + "-evaluate",
                applicationId,
                created.Version,
                "customer_request",
                applicationId + "-evaluate-correlation"),
            CancellationToken.None);
        return evaluated.Version;
    }

    /// <summary>
    /// The Workflow decision is applied by its own consumer in production; the suite records the same terminal
    /// status and the same bound approval evidence so opening and activation can be exercised without the
    /// Workflow stack.
    /// </summary>
    internal static async Task<(string ApplicationId, long Version)> CreateApprovedApplicationAsync(
        CoreLedgerDatabaseScope scope,
        string applicationId,
        string customerId,
        ActorId actor,
        TimeProvider timeProvider)
    {
        long eligibleVersion = await CreateEligibleApplicationAsync(scope, applicationId, customerId, actor, timeProvider);

        await using CoreLedgerDbContext context = scope.CreateContext();
        AccountApplicationRecord application = await context.AccountApplications
            .SingleAsync(record => record.AccountApplicationId == applicationId);
        application.Status = nameof(AccountApplicationStatus.Approved);
        application.ApprovalRequestId = applicationId + "-approval-request";
        application.ApprovedByActorId = "db-checker";
        application.ApprovedAt = CoreLedgerDatabaseScope.SeededAt;
        application.Version = eligibleVersion + 1;
        await context.SaveChangesAsync();

        return (applicationId, application.Version);
    }

    internal static async Task<string> OpenAccountAsync(
        CoreLedgerDatabaseScope scope,
        string applicationId,
        string customerId,
        ActorId actor,
        TimeProvider timeProvider)
    {
        (string id, long version) = await CreateApprovedApplicationAsync(scope, applicationId, customerId, actor, timeProvider);

        AmbientTenantContext tenantContext = scope.NewTenant();
        await using CoreLedgerDbContext context = scope.CreateContext(tenantContext);
        AccountOpenedResult opened = await CreateOpeningService(context, tenantContext, timeProvider).OpenAsync(
            new OpenAccountCommand(
                scope.Institution,
                actor,
                applicationId + "-open",
                id,
                version,
                applicationId + "-open-correlation"),
            CancellationToken.None);
        return opened.AccountId;
    }

    /// <summary>
    /// Opens and then activates the account through the real activation transition, which needs the ledger
    /// initialisation, the bound approval evidence and current authoritative facts.
    /// </summary>
    internal static async Task<(string AccountId, long Version)> OpenAndActivateAccountAsync(
        CoreLedgerDatabaseScope scope,
        string applicationId,
        string customerId,
        ActorId actor,
        TimeProvider timeProvider)
    {
        string accountId = await OpenAccountAsync(scope, applicationId, customerId, actor, timeProvider);

        AmbientTenantContext tenantContext = scope.NewTenant();
        await using CoreLedgerDbContext context = scope.CreateContext(tenantContext);
        long version = await context.Accounts
            .Where(record => record.AccountId == accountId)
            .Select(record => record.Version)
            .SingleAsync();

        AccountActivationService activation = new(
            context,
            new IdempotencyService(context, timeProvider),
            tenantContext,
            timeProvider,
            new AuthoritativeEligibilityFactsResolver(
                new FixedCustomerEligibilityFactsSource(),
                new FixedBranchReferenceSource(),
                new FixedCurrencyReferenceSource()),
            new FixedAccountNumberSchemeSource());

        AccountActivatedResult activated = await activation.ActivateAsync(
            new ActivateAccountCommand(
                scope.Institution,
                actor,
                applicationId + "-activate",
                accountId,
                version,
                applicationId + "-activate-correlation"),
            CancellationToken.None);
        return (accountId, version + 1);
    }
}
