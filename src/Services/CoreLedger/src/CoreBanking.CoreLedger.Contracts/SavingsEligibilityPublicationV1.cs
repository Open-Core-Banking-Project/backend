using System;

namespace CoreBanking.CoreLedger.Contracts;

public sealed record PublishSavingsEligibilityRequestV1(
    string PublicationId,
    string ContentHash,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo);

public sealed record SavingsEligibilityPublicationResponseV1(
    string InstitutionId,
    string PublicationId,
    string ProductId,
    string VersionId,
    string ContentHash,
    string EventId,
    string Status,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    DateTimeOffset PublishedAt,
    bool Replayed);

public sealed record ProductVersionPublishedV1(
    string InstitutionId,
    string ProductId,
    string ProductCode,
    string ProductFamily,
    string VersionId,
    int VersionNumber,
    string ContentHash,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    string PublishedByActorId,
    string ApprovalReference,
    DateTimeOffset PublishedAt);
