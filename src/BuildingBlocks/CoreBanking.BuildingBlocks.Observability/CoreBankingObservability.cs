using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using CoreBanking.BuildingBlocks.Application.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace CoreBanking.BuildingBlocks.Observability;

public static class CoreBankingObservability
{
    public static void AddCoreBankingObservability(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        string serviceName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        Activity.DefaultIdFormat = ActivityIdFormat.W3C;
        Activity.ForceDefaultIdFormat = true;

        string? endpoint = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        if (string.IsNullOrWhiteSpace(endpoint) && environment.IsDevelopment())
        {
            endpoint = "http://127.0.0.1:4317";
        }
        else if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new InvalidOperationException(
                "OTEL_EXPORTER_OTLP_ENDPOINT is required outside Development so telemetry has an explicit export destination.");
        }

        string configuredServiceName = configuration["OTEL_SERVICE_NAME"] ?? serviceName;
        string serviceVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
        string protocol = configuration["OTEL_EXPORTER_OTLP_PROTOCOL"] ?? "grpc";
        Uri endpointUri = new(endpoint, UriKind.Absolute);

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(configuredServiceName, serviceNamespace: "CoreBanking", serviceVersion: serviceVersion)
                .AddAttributes([new KeyValuePair<string, object>("deployment.environment.name", environment.EnvironmentName)]))
            .WithTracing(tracing =>
            {
                tracing
                    .SetSampler(new ParentBasedSampler(new AlwaysOnSampler()))
                    .AddSource(CoreBankingActivitySource.Name)
                    .AddSource("Npgsql")
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddProcessor(new SensitiveTelemetryRedactionProcessor());

                tracing.AddOtlpExporter(options =>
                {
                    options.Endpoint = endpointUri;
                    options.Protocol = protocol switch
                    {
                        "grpc" => OtlpExportProtocol.Grpc,
                        "http/protobuf" => OtlpExportProtocol.HttpProtobuf,
                        _ => throw new InvalidOperationException(
                            "OTEL_EXPORTER_OTLP_PROTOCOL must be 'grpc' or 'http/protobuf'."),
                    };
                });
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddMeter("CoreBanking.Messaging")
                    .AddMeter("CoreBanking.Institutions")
                    .AddOtlpExporter(options =>
                    {
                        options.Endpoint = endpointUri;
                        options.Protocol = protocol switch
                        {
                            "grpc" => OtlpExportProtocol.Grpc,
                            "http/protobuf" => OtlpExportProtocol.HttpProtobuf,
                            _ => throw new InvalidOperationException(
                                "OTEL_EXPORTER_OTLP_PROTOCOL must be 'grpc' or 'http/protobuf'."),
                        };
                    });
            });
    }
}

public sealed class SensitiveTelemetryRedactionProcessor : BaseProcessor<Activity>
{
    private static readonly string[] SensitiveTags =
    [
        "url.full",
        "url.path",
        "url.query",
        "http.url",
        "http.target",
        "db.statement",
        "db.query.text",
    ];

    public override void OnEnd(Activity activity)
    {
        foreach (string tag in SensitiveTags)
        {
            activity.SetTag(tag, null);
        }
    }
}
