using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Tenancy;
using CoreBanking.CoreLedger.Application.Products;
using CoreBanking.CoreLedger.Domain.Institutions;
using CoreBanking.CoreLedger.Domain.Products;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.CoreLedger.Infrastructure.Products;

public sealed class SavingsEligibilityEvaluationService : ISavingsEligibilityEvaluationService
{
    private const string Schema = "product_eligibility.v1";

    private readonly CoreLedgerDbContext _context;
    private readonly ITenantContext _tenantContext;

    public SavingsEligibilityEvaluationService(
        CoreLedgerDbContext context,
        ITenantContext tenantContext)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    }

    public async Task<SavingsEligibilityEvaluationResult> EvaluateAsync(
        EvaluateSavingsEligibilityCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.InstitutionId);
        ArgumentNullException.ThrowIfNull(command.Facts);
        if (_tenantContext.CurrentInstitution != command.InstitutionId)
            throw Reject("cross_tenant_access_denied", "The authenticated institution does not match the eligibility target.");

        ProductCode code = ProductCode.From(command.ProductCode);
        InstitutionRecord? institution = await _context.Institutions
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (institution is null || !string.Equals(institution.Status, "active", StringComparison.Ordinal))
            throw Reject("institution_inactive", "Savings eligibility requires an active institution.");

        ProductRecord product = await _context.Products
            .SingleOrDefaultAsync(x => x.Code == code.Value && x.Family == "savings", cancellationToken)
            .ConfigureAwait(false)
            ?? throw Reject("product_not_found", "The Savings product is not available in this institution.");
        ConfigurationPublicationRecord[] effective = await _context.ConfigurationPublications
            .Where(x => x.ConfigurationSetId == product.ConfigurationSetId
                && x.EffectiveFrom <= command.EvaluatedAt
                && (x.EffectiveTo == null || command.EvaluatedAt < x.EffectiveTo))
            .Take(2)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        if (effective.Length == 0)
            throw Reject("product_configuration_not_effective", "No published Savings eligibility version is effective at the requested instant.");
        if (effective.Length > 1)
            throw Reject("product_configuration_ambiguous", "Multiple Savings eligibility versions are effective at the requested instant.");

        ConfigurationPublicationRecord publication = effective[0];
        ConfigurationVersionRecord version = await _context.ConfigurationVersions
            .SingleOrDefaultAsync(x => x.VersionId == publication.VersionId, cancellationToken).ConfigureAwait(false)
            ?? throw Reject("product_publication_integrity_failed", "The effective publication has no matching product version.");
        if (!string.Equals(version.Schema, Schema, StringComparison.Ordinal)
            || !string.Equals(version.Status, "published", StringComparison.Ordinal)
            || !string.Equals(version.ConfigurationSetId, product.ConfigurationSetId, StringComparison.Ordinal)
            || !string.Equals(version.ContentHash, publication.ContentHash, StringComparison.Ordinal)
            || version.EffectiveFrom != publication.EffectiveFrom
            || version.EffectiveTo != publication.EffectiveTo)
            throw Reject("product_publication_integrity_failed", "The effective publication does not match its immutable product version.");

        ConfigurationContent content = ConfigurationContent.Create(version.ContentJson);
        if (!string.Equals(content.Hash, version.ContentHash, StringComparison.Ordinal))
            throw Reject("product_content_integrity_failed", "The effective product content does not match its recorded hash.");
        ProductEligibilityRules rules;
        try
        {
            rules = ProductEligibilityRules.FromConfigurationContent(content);
        }
        catch (ArgumentException exception)
        {
            throw new SavingsEligibilityEvaluationRejectedException(
                "product_rule_invalid", "The effective Savings eligibility rules are invalid.", exception);
        }

        FinalEligibilityDecision decision = ProductEligibilityApprovalPolicy.RecheckForFinalApproval(
            rules,
            command.Facts,
            command.EvaluatedAt);
        return new SavingsEligibilityEvaluationResult(
            command.InstitutionId.Value,
            product.ProductId,
            product.Code,
            version.VersionId,
            version.VersionNumber,
            version.ContentHash,
            publication.EffectiveFrom,
            publication.EffectiveTo,
            decision.Eligible,
            decision.FactsCurrent,
            decision.CustomerFactsVersion,
            decision.BranchReferenceVersion,
            decision.CurrencyReferenceVersion,
            decision.Trace,
            decision.Code,
            command.EvaluatedAt);
    }

    private static SavingsEligibilityEvaluationRejectedException Reject(string code, string message) => new(code, message);
}
