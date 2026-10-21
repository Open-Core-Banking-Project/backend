using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CoreLedger.Domain.Products;

namespace CoreBanking.CoreLedger.Application.Products;

public sealed record EvaluateSavingsEligibilityCommand(
    InstitutionId InstitutionId,
    string ProductCode,
    AuthoritativeProductEligibilityFacts Facts,
    DateTimeOffset EvaluatedAt);

public sealed record SavingsEligibilityEvaluationResult(
    string InstitutionId,
    string ProductId,
    string ProductCode,
    string ProductVersionId,
    int ProductVersionNumber,
    string ProductContentHash,
    DateTimeOffset ProductEffectiveFrom,
    DateTimeOffset? ProductEffectiveTo,
    bool Eligible,
    bool FactsCurrent,
    string CustomerFactsVersion,
    string BranchReferenceVersion,
    string CurrencyReferenceVersion,
    IReadOnlyCollection<EligibilityRuleTrace> Trace,
    string Code,
    DateTimeOffset EvaluatedAt);

public interface ISavingsEligibilityEvaluationService
{
    Task<SavingsEligibilityEvaluationResult> EvaluateAsync(
        EvaluateSavingsEligibilityCommand command,
        CancellationToken cancellationToken);
}

public sealed class SavingsEligibilityEvaluationRejectedException : Exception
{
    public SavingsEligibilityEvaluationRejectedException(string code, string message) : base(message) => Code = code;
    public SavingsEligibilityEvaluationRejectedException(string code, string message, Exception inner) : base(message, inner) => Code = code;
    public string Code { get; }
}
