using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using CoreBanking.CoreLedger.Application.Products;

namespace CoreBanking.CoreLedger.Infrastructure.Products;

/// <summary>
/// Bounded product and account configuration signals. Tags carry only area, command and bounded outcome
/// codes; never a customer, account, payload or monetary value. Decision: P1-E04 slice 14.
/// </summary>
public sealed class ProductAccountMetrics
{
    public const string MeterName = "CoreBanking.ProductAccounts";

    private ProductAccountMetrics(Meter meter)
    {
        Meter = meter;
        ConfigurationCommands = meter.CreateCounter<long>(
            "corebanking.product_account.configuration.commands",
            description: "Configuration lifecycle commands by area, command and bounded outcome.");
        ReferenceFactLookups = meter.CreateCounter<long>(
            "corebanking.product_account.reference_facts.lookups",
            description: "Authoritative reference-fact lookups by bounded outcome code.");
        AccountCommands = meter.CreateCounter<long>(
            "corebanking.product_account.account.commands",
            description: "Account-application commands by command and bounded outcome.");
    }

    public static ProductAccountMetrics Shared { get; } = new(new Meter(MeterName));

    public Meter Meter { get; }

    public Counter<long> ConfigurationCommands { get; }

    public Counter<long> ReferenceFactLookups { get; }

    public Counter<long> AccountCommands { get; }

    public void RecordConfigurationCommand(string area, string command, string outcome) =>
        ConfigurationCommands.Add(1, new KeyValuePair<string, object?>("area", area), new KeyValuePair<string, object?>("command", command), new KeyValuePair<string, object?>("outcome", outcome));

    public void RecordAccountCommand(string command, string outcome) =>
        AccountCommands.Add(1, new KeyValuePair<string, object?>("command", command), new KeyValuePair<string, object?>("outcome", outcome));

    public void RecordReferenceFactLookup(string outcome) =>
        ReferenceFactLookups.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
}

/// <summary>Adapter that lets the application resolver emit bounded outcomes without referencing telemetry.</summary>
public sealed class ProductAccountTelemetry(ProductAccountMetrics metrics) : IReferenceFactTelemetry
{
    private readonly ProductAccountMetrics _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));

    public void Record(string outcome) => _metrics.RecordReferenceFactLookup(outcome);
}
