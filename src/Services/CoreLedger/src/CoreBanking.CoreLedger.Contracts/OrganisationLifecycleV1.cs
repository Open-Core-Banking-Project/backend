using System;

namespace CoreBanking.CoreLedger.Contracts;

public sealed record CreateRegionRequestV1(string RegionId, string Code, string Name);
public sealed record CreateBranchRequestV1(string BranchId, string RegionId, string Code, string Name);
public sealed record ChangeOrganisationStatusRequestV1(string? Reason);
public sealed record OrganisationLifecycleResponseV1(string InstitutionId, string ResourceId, string EventId, string Status, DateTimeOffset ChangedAt, bool Replayed);
public sealed record RegionCreatedV1(string InstitutionId, string RegionId, string Code, string Name, DateTimeOffset CreatedAt);
public sealed record RegionStatusChangedV1(string InstitutionId, string RegionId, string PreviousStatus, string Status, string? Reason, DateTimeOffset ChangedAt);
public sealed record BranchCreatedV1(string InstitutionId, string BranchId, string RegionId, string Code, string Name, DateTimeOffset CreatedAt);
public sealed record BranchStatusChangedV1(string InstitutionId, string BranchId, string RegionId, string PreviousStatus, string Status, string? Reason, DateTimeOffset ChangedAt);
