using System;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Tenancy;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging.Kafka;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.PaymentsVtu.Infrastructure.Institutions;
using CoreBanking.PaymentsVtu.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CoreBanking.PaymentsVtu.Workers;

public static partial class Program
{
    public static async Task<int> Main(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
        CoreBanking.BuildingBlocks.Observability.CoreBankingObservability.AddCoreBankingObservability(
            builder.Services,
            builder.Configuration,
            builder.Environment,
            "corebanking.payments-vtu.worker");

        string? connectionString = builder.Configuration.GetConnectionString("PaymentsVtu");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:PaymentsVtu is required.");
        }

        string? bootstrapServers = builder.Configuration["Messaging:Kafka:BootstrapServers"];
        if (string.IsNullOrWhiteSpace(bootstrapServers))
        {
            throw new InvalidOperationException("Messaging:Kafka:BootstrapServers is required.");
        }

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<AmbientTenantContext>();
        builder.Services.AddSingleton<ITenantContext>(provider => provider.GetRequiredService<AmbientTenantContext>());
        builder.Services.AddDbContext<PaymentsVtuDbContext>(options => options.UseNpgsql(connectionString));
        builder.Services.AddScoped<IIntegrationStore>(provider => provider.GetRequiredService<PaymentsVtuDbContext>());
        builder.Services.AddScoped(provider => new InboxProcessor(
            provider.GetRequiredService<IIntegrationStore>(),
            provider.GetRequiredService<TimeProvider>()));
        builder.Services.AddScoped<InstitutionProvisioningConsumer>();
        builder.Services.AddScoped<IIntegrationEventConsumer>(provider => provider.GetRequiredService<InstitutionProvisioningConsumer>());
        builder.Services.AddSingleton(new OutboxRelayOptions());
        builder.Services.AddScoped<OutboxRelay>();
        builder.Services.AddKafkaEventPublisher(new KafkaMessagingOptions
        {
            BootstrapServers = bootstrapServers,
            ClientId = "payments-vtu-relay",
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
        ILogger logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("CoreBanking.PaymentsVtu.Workers");
        LogStarted(logger);
        await host.RunAsync().ConfigureAwait(false);
        return 0;
    }

    [LoggerMessage(EventId = 2500, Level = LogLevel.Information, Message = "Payments and VTU messaging workers started.")]
    private static partial void LogStarted(ILogger logger);
}
