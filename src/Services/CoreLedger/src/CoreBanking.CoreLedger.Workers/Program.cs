using System;
using System.Linq;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Tenancy;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging.Kafka;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.CoreLedger.Infrastructure.Institutions;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CoreBanking.CoreLedger.Workers;

/// <summary>
/// Runs Core Ledger's outbox relay and institution acknowledgement consumer outside the API.
/// </summary>
public static partial class Program
{
    public static async Task<int> Main(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
        CoreBanking.BuildingBlocks.Observability.CoreBankingObservability.AddCoreBankingObservability(
            builder.Services,
            builder.Configuration,
            builder.Environment,
            "corebanking.core-ledger.worker");

        string? connectionString = builder.Configuration.GetConnectionString("CoreLedger");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:CoreLedger is required; the worker owns no data without its own database.");
        }

        string? bootstrapServers = builder.Configuration["Messaging:Kafka:BootstrapServers"];
        if (string.IsNullOrWhiteSpace(bootstrapServers))
        {
            throw new InvalidOperationException("Messaging:Kafka:BootstrapServers is required; the broker stays replaceable behind configuration.");
        }

        string[] acknowledgementServices = builder.Configuration
            .GetSection("InstitutionProvisioning:RequiredServices")
            .Get<string[]>()?
            .Select(service => service.Trim().ToLowerInvariant())
            .Where(service => !string.Equals(service, InstitutionCreationService.Producer, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? [];
        if (acknowledgementServices.Length == 0)
        {
            throw new InvalidOperationException("InstitutionProvisioning:RequiredServices must include at least one remote service.");
        }

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<AmbientTenantContext>();
        builder.Services.AddSingleton<ITenantContext>(provider => provider.GetRequiredService<AmbientTenantContext>());
        builder.Services.AddDbContext<CoreLedgerDbContext>(options => options.UseNpgsql(connectionString));
        builder.Services.AddScoped<IIntegrationStore>(provider => provider.GetRequiredService<CoreLedgerDbContext>());
        builder.Services.AddScoped(provider => new InboxProcessor(
            provider.GetRequiredService<IIntegrationStore>(),
            provider.GetRequiredService<TimeProvider>()));
        builder.Services.AddScoped<InstitutionBoundaryAcknowledgementConsumer>();
        builder.Services.AddScoped<IIntegrationEventConsumer>(provider =>
            provider.GetRequiredService<InstitutionBoundaryAcknowledgementConsumer>());
        builder.Services.AddSingleton(new OutboxRelayOptions());
        builder.Services.AddScoped<OutboxRelay>();
        builder.Services.AddKafkaEventPublisher(new KafkaMessagingOptions
        {
            BootstrapServers = bootstrapServers,
            ClientId = "core-ledger-relay",
        });
        builder.Services.AddSingleton(new KafkaConsumerOptions
        {
            BootstrapServers = bootstrapServers,
            GroupId = InstitutionBoundaryAcknowledgementConsumer.ConsumerName,
            Topics = acknowledgementServices
                .Select(InstitutionBoundaryAcknowledgementConsumer.TopicForService)
                .ToArray(),
        });

        builder.Services.AddHostedService<OutboxRelayWorker>();
        builder.Services.AddHostedService<KafkaInboxConsumerWorker>();

        IHost host = builder.Build();
        OutboxBacklogGauges.Register(host.Services.GetRequiredService<IServiceScopeFactory>());

        ILogger logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("CoreBanking.CoreLedger.Workers");
        LogStarted(logger);

        await host.RunAsync().ConfigureAwait(false);
        return 0;
    }

    [LoggerMessage(
        EventId = 2000,
        Level = LogLevel.Information,
        Message = "Core Ledger messaging workers started.")]
    private static partial void LogStarted(ILogger logger);
}
