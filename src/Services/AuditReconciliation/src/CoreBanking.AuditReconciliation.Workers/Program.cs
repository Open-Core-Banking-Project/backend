using System;
using System.Threading.Tasks;
using CoreBanking.AuditReconciliation.Infrastructure.Foundation;
using CoreBanking.AuditReconciliation.Infrastructure.Institutions;
using CoreBanking.AuditReconciliation.Infrastructure.Messaging;
using CoreBanking.AuditReconciliation.Infrastructure.Persistence;
using CoreBanking.BuildingBlocks.Application.Tenancy;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging.Kafka;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CoreBanking.AuditReconciliation.Workers;

/// <summary>Runs the foundation-event consumer using Audit Reconciliation's own database.</summary>
public static partial class Program
{
    public static async Task<int> Main(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
        CoreBanking.BuildingBlocks.Observability.CoreBankingObservability.AddCoreBankingObservability(
            builder.Services,
            builder.Configuration,
            builder.Environment,
            "corebanking.audit-reconciliation.worker");

        string? connectionString = builder.Configuration.GetConnectionString("AuditReconciliation");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:AuditReconciliation is required; the consumer must use its own database.");
        }

        string? bootstrapServers = builder.Configuration["Messaging:Kafka:BootstrapServers"];
        if (string.IsNullOrWhiteSpace(bootstrapServers))
        {
            throw new InvalidOperationException("Messaging:Kafka:BootstrapServers is required; the broker stays replaceable behind configuration.");
        }

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<AmbientTenantContext>();
        builder.Services.AddSingleton<ITenantContext>(provider => provider.GetRequiredService<AmbientTenantContext>());
        builder.Services.AddDbContext<AuditReconciliationDbContext>(options => options.UseNpgsql(connectionString));
        builder.Services.AddScoped<IIntegrationStore>(provider => provider.GetRequiredService<AuditReconciliationDbContext>());
        builder.Services.AddScoped<IUntrustedMessageQuarantine, PersistentUntrustedMessageQuarantine>();
        builder.Services.AddScoped(provider => new InboxProcessor(
            provider.GetRequiredService<IIntegrationStore>(),
            provider.GetRequiredService<TimeProvider>()));
        builder.Services.AddScoped<AuditFoundationEventConsumer>();
        builder.Services.AddScoped<InstitutionProvisioningConsumer>();
        builder.Services.AddScoped<AuditReconciliationEventRouter>();
        builder.Services.AddScoped<IIntegrationEventConsumer>(provider =>
            provider.GetRequiredService<AuditReconciliationEventRouter>());
        builder.Services.AddSingleton(new OutboxRelayOptions());
        builder.Services.AddScoped<OutboxRelay>();
        builder.Services.AddKafkaEventPublisher(new KafkaMessagingOptions
        {
            BootstrapServers = bootstrapServers,
            ClientId = "audit-reconciliation-relay",
        });
        builder.Services.AddSingleton(new KafkaConsumerOptions
        {
            BootstrapServers = bootstrapServers,
            GroupId = "audit-reconciliation.events",
            Topics = [AuditFoundationEventConsumer.TopicName, InstitutionProvisioningConsumer.TopicName],
        });
        builder.Services.AddHostedService<OutboxRelayWorker>();
        builder.Services.AddHostedService<KafkaInboxConsumerWorker>();

        IHost host = builder.Build();
        MessagingBacklogGauges.Register(
            host.Services.GetRequiredService<IServiceScopeFactory>(),
            host.Services.GetRequiredService<TimeProvider>());

        ILogger startedLogger = host.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("CoreBanking.AuditReconciliation.Workers");
        LogStarted(startedLogger);

        await host.RunAsync().ConfigureAwait(false);
        return 0;
    }

    [LoggerMessage(
        EventId = 2300,
        Level = LogLevel.Information,
        Message = "Audit Reconciliation messaging workers started.")]
    private static partial void LogStarted(ILogger logger);
}
