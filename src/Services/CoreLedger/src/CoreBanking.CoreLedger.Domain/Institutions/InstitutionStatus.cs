namespace CoreBanking.CoreLedger.Domain.Institutions;

public enum InstitutionStatus
{
    Draft = 1,
    Provisioning = 2,
    ProvisioningFailed = 3,
    PendingActivation = 4,
    Active = 5,
    Suspended = 6,
}
