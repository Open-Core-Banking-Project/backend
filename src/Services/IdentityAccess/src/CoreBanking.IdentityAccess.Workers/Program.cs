using System;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Tenancy;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging.Kafka;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.IdentityAccess.Infrastructure.Institutions;
using CoreBanking.IdentityAccess.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CoreBanking.IdentityAccess.Workers;

/// <summary>
/// The Identity and Access worker host runs its outbox relay and institution-provisioning consumer
/// in a separate process from the API, so a request never publishes directly.
/// Configuration comes from the environment:
///   ConnectionStrings__IdentityAccess  the application principal connection string
///   Messaging__Kafka__BootstrapServers  the broker address
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
            "corebanking.identity-access.worker");

        string? connectionString = builder.Configuration.GetConnectionString("IdentityAccess");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:IdentityAccess is required; the worker owns no data without its own database.");
        }

        string? bootstrapServers = builder.Configuration["Messaging:Kafka:BootstrapServers"];
        if (string.IsNullOrWhiteSpace(bootstrapServers))
        {
            throw new InvalidOperationException("Messaging:Kafka:BootstrapServers is required; the broker stays replaceable behind configuration.");
        }

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<AmbientTenantContext>();
        builder.Services.AddSingleton<ITenantContext>(provider => provider.GetRequiredService<AmbientTenantContext>());
        builder.Services.AddDbContext<IdentityAccessDbContext>(options => options.UseNpgsql(connectionString));
        builder.Services.AddScoped<IIntegrationStore>(provider => provider.GetRequiredService<IdentityAccessDbContext>());
        builder.Services.AddScoped(provider => new InboxProcessor(
            provider.GetRequiredService<IIntegrationStore>(),
            provider.GetRequiredService<TimeProvider>()));
        builder.Services.AddScoped<InstitutionProvisioningConsumer>();
        builder.Services.AddScoped<IIntegrationEventConsumer>(provider =>
            provider.GetRequiredService<InstitutionProvisioningConsumer>());
        builder.Services.AddSingleton(new OutboxRelayOptions());
        builder.Services.AddScoped<OutboxRelay>();
        builder.Services.AddKafkaEventPublisher(new KafkaMessagingOptions
        {
            BootstrapServers = bootstrapServers,
            ClientId = "identity-access-relay",
        });
        builder.Services.AddSingleton(new KafkaConsumerOptions
        {
            BootstrapServers = bootstrapServers,
            GroupId = InstitutionProvisioningConsumer.ConsumerName,
            Topics = [InstitutionProvisioningConsumer.TopicName],
        });

        builder.Services.AddHostedService<OutboxRelayWorker>();
        builder.Services.AddHostedService<KafkaInboxConsumerWorker>();

        IHost host = builder.Build();

        OutboxBacklogGauges.Register(host.Services.GetRequiredService<IServiceScopeFactory>());

        ILogger startedLogger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("CoreBanking.IdentityAccess.Workers");
        LogStarted(startedLogger);

        await host.RunAsync().ConfigureAwait(false);

        return 0;
    }

    [LoggerMessage(
        EventId = 2000,
        Level = LogLevel.Information,
        Message = "Identity and Access messaging workers started.")]
    private static partial void LogStarted(ILogger logger);
}
