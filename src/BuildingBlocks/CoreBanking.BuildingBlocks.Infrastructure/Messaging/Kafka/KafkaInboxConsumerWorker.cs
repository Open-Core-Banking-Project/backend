using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CoreBanking.BuildingBlocks.Infrastructure.Messaging.Kafka;

/// <summary>
/// Shared Kafka transport adapter. A source offset is committed only after the owning service has
/// durably processed, rejected, or deduplicated the event in its own database.
/// </summary>
public sealed partial class KafkaInboxConsumerWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly KafkaConsumerOptions _options;
    private readonly ILogger<KafkaInboxConsumerWorker> _logger;

    public KafkaInboxConsumerWorker(
        IServiceScopeFactory scopeFactory,
        KafkaConsumerOptions options,
        ILogger<KafkaInboxConsumerWorker> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using IConsumer<string, string> consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = _options.GroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            AllowAutoCreateTopics = _options.AllowAutoCreateTopics,
            BrokerAddressFamily = BrokerAddressFamily.V4,
        }).Build();

        consumer.Subscribe(_options.Topic);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string>? result;
                try
                {
                    result = consumer.Consume(TimeSpan.FromMilliseconds(500));
                }
                catch (ConsumeException exception)
                {
                    LogConsumeFailure(_logger, exception.Error.Code.ToString());
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);
                    continue;
                }

                if (result?.Message?.Value is not string json)
                {
                    continue;
                }

                InboxResult outcome = await HandleAsync(json, stoppingToken).ConfigureAwait(false);
                if (outcome is InboxResult.Processed or InboxResult.Duplicate or InboxResult.Rejected)
                {
                    try
                    {
                        consumer.Commit(result);
                    }
                    catch (KafkaException exception)
                    {
                        LogCommitFailure(_logger, exception.Error.Code.ToString());
                    }
                }
                else
                {
                    LogDeferred(_logger, result.Message.Key ?? string.Empty);
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            consumer.Close();
        }
    }

    private async Task<InboxResult> HandleAsync(string json, CancellationToken cancellationToken)
    {
        IntegrationEventEnvelope? envelope;
        try
        {
            envelope = EventContract.Deserialize<IntegrationEventEnvelope>(json);
        }
        catch (JsonException exception)
        {
            LogUnreadable(_logger, exception.GetType().Name);
            return InboxResult.Deferred;
        }

        if (envelope is null
            || string.IsNullOrWhiteSpace(envelope.EventId)
            || string.IsNullOrWhiteSpace(envelope.InstitutionId)
            || string.IsNullOrWhiteSpace(envelope.EventType)
            || string.IsNullOrWhiteSpace(envelope.Producer)
            || string.IsNullOrWhiteSpace(envelope.Payload))
        {
            LogUnreadable(_logger, "EnvelopeMissingRequiredFields");
            return InboxResult.Deferred;
        }

        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        IIntegrationEventConsumer handler = scope.ServiceProvider.GetRequiredService<IIntegrationEventConsumer>();
        try
        {
            return await handler.HandleAsync(envelope, cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception)
        {
            LogPersistenceFailure(_logger, exception.GetType().Name);
            return InboxResult.Deferred;
        }
        catch (ArgumentException exception)
        {
            LogUnreadable(_logger, exception.GetType().Name);
            return InboxResult.Deferred;
        }
    }

    [LoggerMessage(EventId = 2201, Level = LogLevel.Warning, Message = "Kafka consume failed with broker error {Code}; the offset remains uncommitted")]
    private static partial void LogConsumeFailure(ILogger logger, string code);

    [LoggerMessage(EventId = 2202, Level = LogLevel.Warning, Message = "Event {Key} was deferred and will be redelivered")]
    private static partial void LogDeferred(ILogger logger, string key);

    [LoggerMessage(EventId = 2203, Level = LogLevel.Error, Message = "An unreadable message was retained for operator repair: {Reason}")]
    private static partial void LogUnreadable(ILogger logger, string reason);

    [LoggerMessage(EventId = 2204, Level = LogLevel.Error, Message = "Kafka offset commit failed with broker error {Code}; the event may be delivered again")]
    private static partial void LogCommitFailure(ILogger logger, string code);

    [LoggerMessage(EventId = 2205, Level = LogLevel.Error, Message = "Consumer persistence failed with {Failure}; the offset remains uncommitted")]
    private static partial void LogPersistenceFailure(ILogger logger, string failure);
}

/// <summary>Connection and subscription settings for a Kafka inbox worker.</summary>
public sealed record KafkaConsumerOptions
{
    public required string BootstrapServers { get; init; }

    public required string GroupId { get; init; }

    public required string Topic { get; init; }

    public bool AllowAutoCreateTopics { get; init; } = true;
}
