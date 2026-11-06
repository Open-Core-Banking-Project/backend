using System;
using System.Collections.Generic;

namespace CoreBanking.CoreLedger.Contracts;

/// <summary>Request bodies for the institution currency configuration lifecycle. Decision: P1-E04-D17.</summary>
public sealed record CreateCurrencyConfigurationRequestV1(
    string VersionId,
    string BaseCurrency,
    IReadOnlyList<string> SupportedCurrencies);

public sealed record SimulateCurrencyConfigurationRequestV1(
    string SimulationId,
    string ContentHash);

public sealed record SubmitCurrencyConfigurationRequestV1(
    string RequestId,
    string SourceRequestKey,
    string ContentHash);

public sealed record PublishCurrencyConfigurationRequestV1(
    string PublicationId,
    string ContentHash,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo);
