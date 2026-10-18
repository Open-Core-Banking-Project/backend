using System.Collections.Concurrent;
using System.Diagnostics;
using CoreBanking.BuildingBlocks.Observability;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Xunit;

namespace CoreBanking.IdentityAccess.UnitTests.Observability;

[Trait("Category", "Unit")]
public sealed class SensitiveTelemetryRedactionProcessorTests
{
    [Fact]
    public void Removes_raw_urls_and_database_statements_but_keeps_safe_attributes()
    {
        using Activity activity = new("redaction-test");
        activity.SetTag("url.path", "/api/v1/accounts/123456789");
        activity.SetTag("url.query", "access_token=secret");
        activity.SetTag("db.statement", "SELECT * FROM accounts");
        activity.SetTag("http.response.status_code", 200);

        SensitiveTelemetryRedactionProcessor processor = new();
        processor.OnEnd(activity);

        Assert.Null(activity.GetTagItem("url.path"));
        Assert.Null(activity.GetTagItem("url.query"));
        Assert.Null(activity.GetTagItem("db.statement"));
        Assert.Equal(200, activity.GetTagItem("http.response.status_code"));
    }

    [Fact]
    public void Exporter_receives_only_the_redacted_activity()
    {
        const string sourceName = "CoreBanking.Redaction.ExporterTest";
        CapturingActivityExporter exporter = new();

        using TracerProvider provider = Sdk.CreateTracerProviderBuilder()
            .AddSource(sourceName)
            .AddProcessor(new SensitiveTelemetryRedactionProcessor())
            .AddProcessor(new SimpleActivityExportProcessor(exporter))
            .Build();
        using ActivitySource source = new(sourceName);

        using (Activity? activity = source.StartActivity("export-redaction-test"))
        {
            Assert.NotNull(activity);
            activity!.SetTag("url.full", "https://local.test/accounts/123?access_token=secret");
            activity.SetTag("db.query.text", "SELECT secret FROM accounts");
            activity.SetTag("messaging.operation.type", "publish");
        }

        Activity exported = Assert.Single(exporter.Exported);
        Assert.Null(exported.GetTagItem("url.full"));
        Assert.Null(exported.GetTagItem("db.query.text"));
        Assert.Equal("publish", exported.GetTagItem("messaging.operation.type"));
    }

    private sealed class CapturingActivityExporter : BaseExporter<Activity>
    {
        public ConcurrentQueue<Activity> Exported { get; } = new();

        public override ExportResult Export(in Batch<Activity> batch)
        {
            foreach (Activity activity in batch)
            {
                Exported.Enqueue(activity);
            }

            return ExportResult.Success;
        }
    }
}
