using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Tenancy;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CoreLedger.Application.Accounts;
using CoreBanking.CoreLedger.Application.Institutions;
using CoreBanking.CoreLedger.Domain.Accounts;
using CoreBanking.CoreLedger.Domain.Institutions;
using CoreBanking.CoreLedger.Domain.Products;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.CoreLedger.Infrastructure.Accounts;

/// <summary>
/// Authors <c>institution.account-opening-policy</c> through the shared configuration lifecycle and
/// refuses a ceiling that would sit below an already published product rule. Decision: P1-E04-D07.
/// </summary>
public sealed class AccountOpeningPolicyConfigurationService : IAccountOpeningPolicyConfigurationService
{
    private static readonly InstitutionConfigurationArea Definition = new(
        AccountOpeningPolicy.Area,
        AccountOpeningPolicy.Schema,
        "institution",
        "institution-account-opening-policy",
        "account_opening_policy",
        contentJson => AccountOpeningPolicy.Parse(contentJson).ToConfigurationContent());

    private readonly IInstitutionConfigurationAreaLifecycle _lifecycle;
    private readonly CoreLedgerDbContext _context;
    private readonly ITenantContext _tenantContext;

    public AccountOpeningPolicyConfigurationService(
        IInstitutionConfigurationAreaLifecycle lifecycle,
        CoreLedgerDbContext context,
        ITenantContext tenantContext)
    {
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    }

    public async Task<ConfigurationDraftOutcome> CreateDraftAsync(
        CreateAccountOpeningPolicyDraftCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        AccountOpeningPolicy policy;
        try
        {
            policy = AccountOpeningPolicy.Create(
                command.LimitKey,
                command.MaximumOpenAccounts,
                command.OverrideAdditionalAccounts);
        }
        catch (ArgumentException exception)
        {
            throw new InstitutionConfigurationRejectedException("account_opening_policy_invalid", exception.Message, exception);
        }

        return await _lifecycle.CreateDraftAsync(
            Definition,
            command.InstitutionId,
            command.ActorId,
            command.IdempotencyKey,
            command.VersionId,
            policy.ToConfigurationContent(),
            "institution.account_opening_policy.draft.create",
            command.CorrelationId,
            cancellationToken).ConfigureAwait(false);
    }

    public Task<ConfigurationSimulationOutcome> SimulateAsync(
        SimulateAccountOpeningPolicyCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return _lifecycle.SimulateAsync(
            Definition,
            command.InstitutionId,
            command.ActorId,
            command.IdempotencyKey,
            command.VersionId,
            command.SimulationId,
            command.ContentHash,
            command.CorrelationId,
            cancellationToken);
    }

    public Task<ConfigurationSubmissionOutcome> SubmitForApprovalAsync(
        SubmitAccountOpeningPolicyCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return _lifecycle.SubmitAsync(
            Definition,
            command.InstitutionId,
            command.ActorId,
            command.IdempotencyKey,
            command.RequestId,
            command.SourceRequestKey,
            command.VersionId,
            command.ContentHash,
            command.CorrelationId,
            cancellationToken);
    }

    public Task<ConfigurationPublicationOutcome> PublishAsync(
        PublishAccountOpeningPolicyCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return _lifecycle.PublishAsync(
            Definition,
            command.InstitutionId,
            command.ActorId,
            command.IdempotencyKey,
            command.PublicationId,
            command.VersionId,
            command.ContentHash,
            command.EffectiveFrom,
            command.EffectiveTo,
            command.CorrelationId,
            cancellationToken,
            (contentJson, _, token) => EnsureProductsDoNotExceedCeilingAsync(command.InstitutionId, contentJson, token));
    }

    /// <summary>
    /// A published product rule may never allow more accounts than the ceiling being published.
    /// Decision: P1-E04-D07.
    /// </summary>
    private async Task EnsureProductsDoNotExceedCeilingAsync(
        InstitutionId institutionId,
        string contentJson,
        CancellationToken cancellationToken)
    {
        AccountOpeningPolicy policy;
        try
        {
            policy = AccountOpeningPolicy.Parse(contentJson);
        }
        catch (ArgumentException exception)
        {
            throw new InstitutionConfigurationRejectedException("account_opening_policy_invalid", exception.Message, exception);
        }

        string[] publishedProductVersions = await _context.ConfigurationVersions
            .Where(candidate => candidate.InstitutionId == institutionId.Value
                && candidate.Schema == "product_eligibility.v1"
                && candidate.Status == "published")
            .Select(candidate => candidate.VersionId)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        foreach (string versionId in publishedProductVersions)
        {
            ConfigurationVersionRecord? productVersion = await _context.ConfigurationVersions
                .SingleOrDefaultAsync(candidate => candidate.VersionId == versionId, cancellationToken)
                .ConfigureAwait(false);
            if (productVersion is null)
            {
                continue;
            }

            ConfigurationContent content = ConfigurationContent.Create(productVersion.ContentJson);
            ProductEligibilityRules rules;
            try
            {
                rules = ProductEligibilityRules.FromConfigurationContent(content);
            }
            catch (ArgumentException exception)
            {
                throw new InstitutionConfigurationRejectedException(
                    "account_opening_policy_product_rule_invalid",
                    "A published product rule could not be validated against the ceiling.",
                    exception);
            }

            if (!rules.ExistingAccounts.AllowsAny
                && rules.ExistingAccounts.MaximumAccounts > policy.MaximumOpenAccounts)
            {
                throw new InstitutionConfigurationRejectedException(
                    "account_opening_policy_below_product_limit",
                    "A published product rule allows more accounts than the ceiling being published.");
            }
        }
    }
}
