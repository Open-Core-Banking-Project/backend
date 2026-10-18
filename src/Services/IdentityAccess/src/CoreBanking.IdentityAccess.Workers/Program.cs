using System;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging.Kafka;
using CoreBanking.IdentityAccess.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CoreBanking.IdentityAccess.Workers;

/// <summary>
/// The Identity and Access worker host runs its outbox relay in a separate process from the API,
/// so a request can never publish and a relay failure can never fail a caller.
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
        builder.Services.AddDbContext<IdentityAccessDbContext>(options => options.UseNpgsql(connectionString));
        builder.Services.AddScoped<IIntegrationStore>(provider => provider.GetRequiredService<IdentityAccessDbContext>());
        builder.Services.AddSingleton(new OutboxRelayOptions());
        builder.Services.AddScoped<OutboxRelay>();
        builder.Services.AddKafkaEventPublisher(new KafkaMessagingOptions
        {
            BootstrapServers = bootstrapServers,
            ClientId = "identity-access-relay",
        });

        builder.Services.AddHostedService<OutboxRelayWorker>();

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
        Message = "Identity and Access outbox relay worker started.")]
    private static partial void LogStarted(ILogger logger);
}
