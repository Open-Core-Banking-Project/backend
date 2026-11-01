using System;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Tenancy;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging.Kafka;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.CustomerOnboarding.Application.Audit;
using CoreBanking.CustomerOnboarding.Application.Customers;
using CoreBanking.CustomerOnboarding.Application.Kyc;
using CoreBanking.CustomerOnboarding.Infrastructure.Approvals;
using CoreBanking.CustomerOnboarding.Infrastructure.Audit;
using CoreBanking.CustomerOnboarding.Infrastructure.Customers;
using CoreBanking.CustomerOnboarding.Infrastructure.Institutions;
using CoreBanking.CustomerOnboarding.Infrastructure.Integration;
using CoreBanking.CustomerOnboarding.Infrastructure.Kyc;
using CoreBanking.CustomerOnboarding.Infrastructure.Messaging;
using CoreBanking.CustomerOnboarding.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CoreBanking.CustomerOnboarding.Workers;

public static partial class Program
{
    public static async Task<int> Main(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
        CoreBanking.BuildingBlocks.Observability.CoreBankingObservability.AddCoreBankingObservability(
            builder.Services,
            builder.Configuration,
            builder.Environment,
            "corebanking.customer-onboarding.worker");

        string? connectionString = builder.Configuration.GetConnectionString("CustomerOnboarding");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:CustomerOnboarding is required.");
        }

        string? bootstrapServers = builder.Configuration["Messaging:Kafka:BootstrapServers"];
        if (string.IsNullOrWhiteSpace(bootstrapServers))
        {
            throw new InvalidOperationException("Messaging:Kafka:BootstrapServers is required.");
        }

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<AmbientTenantContext>();
        builder.Services.AddSingleton<ITenantContext>(provider => provider.GetRequiredService<AmbientTenantContext>());
        builder.Services.AddDbContext<CustomerOnboardingDbContext>(options => options.UseNpgsql(connectionString));
        builder.Services.AddScoped<IIntegrationStore>(provider => provider.GetRequiredService<CustomerOnboardingDbContext>());
        builder.Services.AddScoped(provider => new InboxProcessor(
            provider.GetRequiredService<IIntegrationStore>(),
            provider.GetRequiredService<TimeProvider>()));
        builder.Services.AddScoped<InstitutionProvisioningConsumer>();
        builder.Services.AddScoped<CustomerClassificationApprovalDecidedConsumer>();
        builder.Services.AddScoped<CustomerMergeApprovalDecidedConsumer>();
        builder.Services.AddScoped<ICustomerMergeService, CustomerMergeService>();
        builder.Services.AddScoped<IIntegrationEventConsumer, CustomerOnboardingEventRouter>();
        builder.Services.AddSingleton(new OutboxRelayOptions());
        builder.Services.AddScoped<OutboxRelay>();
        builder.Services.AddKafkaEventPublisher(new KafkaMessagingOptions
        {
            BootstrapServers = bootstrapServers,
            ClientId = "customer-onboarding-relay",
        });
        builder.Services.AddSingleton(new KafkaConsumerOptions
        {
            BootstrapServers = bootstrapServers,
            GroupId = InstitutionProvisioningConsumer.ConsumerName,
            Topics =
            [
                InstitutionProvisioningConsumer.TopicName,
                CustomerClassificationApprovalDecidedConsumer.TopicName,
            ],
        });
        builder.Services.AddHostedService<OutboxRelayWorker>();
        builder.Services.AddHostedService<KafkaInboxConsumerWorker>();
        builder.Services.AddScoped<ICustomerReconciliationService, CustomerReconciliationService>();
        builder.Services.AddScoped<IKycCaseService, KycCaseService>();
        builder.Services.AddSingleton(KycVerificationRunOptions.Default);
        // Phase 1 binds the deterministic synthetic adapter only; a real route arrives with the provider agreement.
        builder.Services.AddSingleton<IKycProviderPort>(_ =>
            new SyntheticKycProviderAdapter(SyntheticKycProviderScript.Passed));
        builder.Services.AddScoped<IKycVerificationRunner, KycVerificationRunner>();
        builder.Services.AddScoped<ICustomerService, CustomerService>();
        builder.Services.AddScoped<ICustomerCorrectionService, CustomerCorrectionService>();
        builder.Services.AddSingleton(CustomerReconciliationOptions.FromConfiguration(builder.Configuration));
        builder.Services.AddHostedService<CustomerReconciliationWorker>();

        IHost host = builder.Build();
        OutboxBacklogGauges.Register(host.Services.GetRequiredService<IServiceScopeFactory>());
        ILogger logger = host.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("CoreBanking.CustomerOnboarding.Workers");
        LogStarted(logger);
        await host.RunAsync().ConfigureAwait(false);
        return 0;
    }

    [LoggerMessage(EventId = 2400, Level = LogLevel.Information, Message = "Customer Onboarding messaging workers started.")]
    private static partial void LogStarted(ILogger logger);
}
