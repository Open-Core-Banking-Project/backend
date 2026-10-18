using System.Diagnostics;

namespace CoreBanking.BuildingBlocks.Application.Diagnostics;

public static class CoreBankingActivitySource
{
    public const string Name = "CoreBanking.Messaging";

    public static ActivitySource Instance { get; } = new(Name);
}
