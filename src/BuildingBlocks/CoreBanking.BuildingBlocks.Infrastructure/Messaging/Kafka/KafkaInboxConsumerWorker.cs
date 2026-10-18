using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using CoreBanking.BuildingBlocks.Application.Diagnostics;
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

        if (_options.Topics.Count == 0)
        {
            throw new InvalidOperationException("At least one Kafka topic must be configured.");
        }

        consumer.Subscribe(_options.Topics);

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

                using Activity? processingActivity = StartProcessingActivity(result.Topic, result.Message.Headers);
                InboxResult outcome = await HandleAsync(result, json, stoppingToken).ConfigureAwait(false);
                if (outcome == InboxResult.Deferred)
                {
                    processingActivity?.SetStatus(ActivityStatusCode.Error);
                }

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

    private static Activity? StartProcessingActivity(string topic, Headers? headers)
    {
        string? traceParent = ReadHeader(headers, "traceparent");
        string? traceState = ReadHeader(headers, "tracestate");
        ActivityContext parentContext = default;
        bool hasParent = ActivityContext.TryParse(traceParent, traceState, isRemote: true, out parentContext);

        Activity? activity = hasParent
            ? CoreBankingActivitySource.Instance.StartActivity("messaging.process", ActivityKind.Consumer, parentContext)
            : CoreBankingActivitySource.Instance.StartActivity("messaging.process", ActivityKind.Consumer);

        activity?.SetTag("messaging.system", "kafka");
        activity?.SetTag("messaging.destination.name", topic);
        activity?.SetTag("messaging.operation.type", "process");
        return activity;
    }

    private static string? ReadHeader(Headers? headers, string name)
    {
        if (headers is null)
        {
            return null;
        }

        for (int index = headers.Count - 1; index >= 0; index--)
        {
            IHeader header = headers[index];
            if (string.Equals(header.Key, name, StringComparison.Ordinal))
            {
                byte[]? value = header.GetValueBytes();
                return value is null ? null : Encoding.UTF8.GetString(value);
            }
        }

        return null;
    }

    private async Task<InboxResult> HandleAsync(
        ConsumeResult<string, string> result,
        string json,
        CancellationToken cancellationToken)
    {
        IntegrationEventEnvelope? envelope;
        try
        {
            envelope = EventContract.Deserialize<IntegrationEventEnvelope>(json);
        }
        catch (JsonException exception)
        {
            LogUnreadable(_logger, exception.GetType().Name);
            return await QuarantineAsync(result, json, exception.GetType().Name, cancellationToken).ConfigureAwait(false);
        }

        if (envelope is null
            || string.IsNullOrWhiteSpace(envelope.EventId)
            || string.IsNullOrWhiteSpace(envelope.InstitutionId)
            || string.IsNullOrWhiteSpace(envelope.EventType)
            || string.IsNullOrWhiteSpace(envelope.Producer)
            || string.IsNullOrWhiteSpace(envelope.Payload))
        {
            LogUnreadable(_logger, "EnvelopeMissingRequiredFields");
            return await QuarantineAsync(result, json, "EnvelopeMissingRequiredFields", cancellationToken).ConfigureAwait(false);
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

    private async Task<InboxResult> QuarantineAsync(
        ConsumeResult<string, string> result,
        string payload,
        string reason,
        CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        IUntrustedMessageQuarantine quarantine = scope.ServiceProvider.GetRequiredService<IUntrustedMessageQuarantine>();

        try
        {
            await quarantine.QuarantineAsync(
                new UntrustedMessage(
                    "kafka",
                    result.Topic,
                    result.Partition.Value,
                    result.Offset.Value,
                    result.Message.Key,
                    payload,
                    reason),
                cancellationToken).ConfigureAwait(false);

            return InboxResult.Rejected;
        }
        catch (DbUpdateException exception)
        {
            LogPersistenceFailure(_logger, exception.GetType().Name);
            return InboxResult.Deferred;
        }
    }

    [LoggerMessage(EventId = 2201, Level = LogLevel.Warning, Message = "Kafka consume failed with broker error {Code}; the offset remains uncommitted")]
    private static partial void LogConsumeFailure(ILogger logger, string code);

    [LoggerMessage(EventId = 2202, Level = LogLevel.Warning, Message = "Event {Key} was deferred and will be redelivered")]
    private static partial void LogDeferred(ILogger logger, string key);

    [LoggerMessage(EventId = 2203, Level = LogLevel.Error, Message = "An unreadable message was deferred without logging its payload: {Reason}")]
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

    public required IReadOnlyList<string> Topics { get; init; }

    public bool AllowAutoCreateTopics { get; init; } = true;
}
