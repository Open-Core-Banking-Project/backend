using System.Threading;
using System.Threading.Tasks;
using CoreBanking.CustomerOnboarding.Application.Requirements;

namespace CoreBanking.CustomerOnboarding.Infrastructure.Requirements;

/// <summary>
/// Placeholder quarantine adapter used until the approved document storage component is wired. It answers
/// "pending scan" for everything, so no document is ever treated as clean by default; submission only
/// refuses on an explicit rejection. Decision: P1-E03-D01, epic 03 ownership table.
/// </summary>
public sealed class PendingScanDocumentQuarantine : IDocumentQuarantine
{
    public Task<DocumentScanResult> SubmitAsync(DocumentScanRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new DocumentScanResult(DocumentScanStatus.PendingScan, null));
}
