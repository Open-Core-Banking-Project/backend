using System;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CoreLedger.Application.Products;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.CoreLedger.Infrastructure.Products;

/// <summary>
/// Resolves the branch reference from the institution module in this service. The reference version
/// combines the branch record version with the institution record version so a consumer can prove the
/// exact state it used. Decision: P1-E04-D17.
/// </summary>
public sealed class BranchReferenceSource : IBranchReferenceSource
{
    private readonly CoreLedgerDbContext _context;

    public BranchReferenceSource(CoreLedgerDbContext context) =>
        _context = context ?? throw new ArgumentNullException(nameof(context));

    public async Task<BranchReferenceSnapshot?> FindAsync(
        InstitutionId institutionId,
        string branchId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);

        BranchRecord? branch = await _context.Branches
            .AsNoTracking()
            .SingleOrDefaultAsync(
                record => record.InstitutionId == institutionId.Value
                    && (record.BranchId == branchId || record.Code == branchId),
                cancellationToken)
            .ConfigureAwait(false);
        if (branch is null)
        {
            return null;
        }

        InstitutionRecord? institution = await _context.Institutions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                record => record.InstitutionId == institutionId.Value,
                cancellationToken)
            .ConfigureAwait(false);

        return new BranchReferenceSnapshot(
            branch.BranchId,
            branch.Code,
            branch.Status,
            string.Concat("b", branch.Version.ToString(System.Globalization.CultureInfo.InvariantCulture), ".i", (institution?.Version ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }
}
