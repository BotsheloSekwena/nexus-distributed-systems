using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;
using OpenTelemetry.Context.Propagation;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TelemetryWorker.Configuration;
using TelemetryWorker.Data;
using TelemetryWorker.Models;
using TelemetryWorker.Observability;

namespace TelemetryWorker.Services;

/// <summary>
/// Consumes telemetry events, persists them and handles failed messages.
/// </summary>
public sealed class TelemetryConsumer : BackgroundService
{
    private const string RetryCountHeader = "x-retry-count";
    private const int SupportedSchemaVersion = 1;

    private readonly RabbitMqOptions _options;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TelemetryConsumer> _logger;

    private IConnection? _connection;
    private IChannel? _consumerChannel;
    private IChannel? _publisherChannel;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public TelemetryConsumer(
        IOptions<RabbitMqOptions> options,
        IServiceScopeFactory scopeFactory,
        ILogger<TelemetryConsumer> logger)
    {
        _options = options.Value;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        try
        {
            var factory = new ConnectionFactory
            {
                HostName = _options.Host,
                Port = _options.Port,
                UserName = _options.User,
                Password = _options.Password
            };

            _connection =
                await factory.CreateConnectionAsync(stoppingToken);

            _consumerChannel =
                await _connection.CreateChannelAsync(
                    cancellationToken: stoppingToken);

            _publisherChannel =
                await _connection.CreateChannelAsync(
                    new CreateChannelOptions(
                        publisherConfirmationsEnabled: true,
                        publisherConfirmationTrackingEnabled: true),
                    stoppingToken);

            await _consumerChannel.BasicQosAsync(
                prefetchSize: 0,
                prefetchCount: 10,
                global: false,
                cancellationToken: stoppingToken);

            var consumer =
                new AsyncEventingBasicConsumer(_consumerChannel);

            consumer.ReceivedAsync += async (_, eventArgs) =>
            {
                await ProcessMessageAsync(eventArgs, stoppingToken);
            };

            await _consumerChannel.BasicConsumeAsync(
                queue: _options.InfrastructureQueue,
                autoAck: false,
                consumer: consumer,
                cancellationToken: stoppingToken);

            await _consumerChannel.BasicConsumeAsync(
                queue: _options.SensorQueue,
                autoAck: false,
                consumer: consumer,
                cancellationToken: stoppingToken);

            _logger.LogInformation(
                "Telemetry consumer listening to infrastructure and sensor queues.");

            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Telemetry consumer is stopping.");
        }
        finally
        {
            if (_consumerChannel is not null)
                await _consumerChannel.DisposeAsync();

            if (_publisherChannel is not null)
                await _publisherChannel.DisposeAsync();

            if (_connection is not null)
                await _connection.DisposeAsync();
        }
    }

    /// <summary>
    /// Processes a message and records its observability data.
    /// </summary>
    private async Task ProcessMessageAsync(
        BasicDeliverEventArgs eventArgs,
        CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var category = "unknown";

        // Continue the publisher's trace when the message carries W3C trace
        // context. Messages without it (e.g. older ones) start a new trace.
        var parentContext = Propagators.DefaultTextMapPropagator.Extract(
            default,
            eventArgs.BasicProperties,
            ReadTraceContextHeader);

        using var activity =
            TelemetryWorkerInstrumentation.ActivitySource.StartActivity(
                "telemetry.process",
                ActivityKind.Consumer,
                parentContext.ActivityContext);

        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.destination.name", eventArgs.RoutingKey);

        try
        {
            var telemetryEvent =
                JsonSerializer.Deserialize<TelemetryEvent>(
                    eventArgs.Body.Span,
                    JsonOptions);

            ValidateTelemetryEvent(telemetryEvent);

            category = telemetryEvent!.Category.ToString().ToLowerInvariant();

            activity?.SetTag("nexus.telemetry.category", category);
            activity?.SetTag(
                "nexus.telemetry.schema_version",
                telemetryEvent.SchemaVersion);

            var persisted = await PersistTelemetryAsync(
                telemetryEvent,
                cancellationToken);

            await _consumerChannel!.BasicAckAsync(
                eventArgs.DeliveryTag,
                multiple: false,
                cancellationToken: cancellationToken);

            if (persisted)
            {
                TelemetryWorkerInstrumentation.MessagesProcessed.Add(
                    1,
                    new KeyValuePair<string, object?>("category", category));
            }
            else
            {
                TelemetryWorkerInstrumentation.DuplicateMessages.Add(
                    1,
                    new KeyValuePair<string, object?>("category", category));
            }

            activity?.SetTag(
                "nexus.telemetry.outcome",
                persisted ? "processed" : "duplicate");

            _logger.LogInformation(
                "Processed telemetry event {EventId} in category {Category}.",
                telemetryEvent.EventId,
                telemetryEvent.Category);
        }
        catch (Exception exception)
        {
            TelemetryWorkerInstrumentation.MessagesFailed.Add(
                1,
                new KeyValuePair<string, object?>("category", category));

            activity?.SetStatus(ActivityStatusCode.Error);

            activity?.SetTag(
                "nexus.telemetry.outcome",
                "failed");

            _logger.LogError(
                exception,
                "Failed to process delivery {DeliveryTag}.",
                eventArgs.DeliveryTag);

            try
            {
                if (exception is JsonException or InvalidDataException)
                {
                    await SendToDeadLetterQueueAsync(
                        eventArgs,
                        exception.Message,
                        cancellationToken);
                }
                else
                {
                    await HandleRetryAsync(
                        eventArgs,
                        exception,
                        cancellationToken);
                }
            }
            catch (Exception publishingException)
            {
                _logger.LogError(
                    publishingException,
                    "Could not route failed message. Requeuing original delivery.");

                await _consumerChannel!.BasicNackAsync(
                    eventArgs.DeliveryTag,
                    multiple: false,
                    requeue: true,
                    cancellationToken: cancellationToken);
            }
        }
        finally
        {
            TelemetryWorkerInstrumentation.ProcessingDuration.Record(
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
                new KeyValuePair<string, object?>("category", category));
        }
    }

    /// <summary>
    /// Validates the telemetry event before persistence.
    /// </summary>
    private static void ValidateTelemetryEvent(
        TelemetryEvent? telemetryEvent)
    {
        if (telemetryEvent is null)
            throw new JsonException("Telemetry message contained no event.");

        if (telemetryEvent.EventId == Guid.Empty)
            throw new InvalidDataException("Event ID is required.");

        if (telemetryEvent.SchemaVersion != SupportedSchemaVersion)
            throw new InvalidDataException("Unsupported telemetry schema version.");

        if (!Enum.IsDefined(telemetryEvent.Category))
            throw new InvalidDataException("Invalid telemetry category.");

        if (string.IsNullOrWhiteSpace(telemetryEvent.Source) ||
            telemetryEvent.Source.Length > 150)
            throw new InvalidDataException("Invalid telemetry source.");

        if (telemetryEvent.Data is null)
            throw new InvalidDataException("Telemetry measurement is required.");

        if (string.IsNullOrWhiteSpace(telemetryEvent.Data.Metric) ||
            telemetryEvent.Data.Metric.Length > 100)
            throw new InvalidDataException("Invalid telemetry metric.");

        if (!double.IsFinite(telemetryEvent.Data.Value))
            throw new InvalidDataException("Telemetry value must be finite.");

        if (string.IsNullOrWhiteSpace(telemetryEvent.Data.Unit) ||
            telemetryEvent.Data.Unit.Length > 32)
            throw new InvalidDataException("Invalid telemetry unit.");

        if (telemetryEvent.Data.Dimensions is null ||
            telemetryEvent.Data.Dimensions.Count > 20 ||
            telemetryEvent.Data.Dimensions.Any(dimension =>
                string.IsNullOrWhiteSpace(dimension.Key) ||
                dimension.Key.Length > 100 ||
                dimension.Value is null ||
                dimension.Value.Length > 250))
        {
            throw new InvalidDataException("Invalid telemetry dimensions.");
        }
    }

    /// <summary>
    /// Handles temporary failures using bounded retry delivery.
    /// </summary>
    private async Task HandleRetryAsync(
        BasicDeliverEventArgs eventArgs,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var retryCount = GetRetryCount(eventArgs.BasicProperties);

        if (retryCount >= _options.MaxRetryAttempts)
        {
            await SendToDeadLetterQueueAsync(
                eventArgs,
                $"Retry limit exhausted: {exception.Message}",
                cancellationToken);

            return;
        }

        var nextAttempt = retryCount + 1;
        var routingKey = eventArgs.RoutingKey;

        var retryQueue = routingKey switch
        {
            "infrastructure" => _options.InfrastructureRetryQueue,
            "sensor" => _options.SensorRetryQueue,
            _ => throw new InvalidDataException(
                $"Unsupported telemetry routing key: {routingKey}")
        };

        var properties = CreateMessageProperties(
            eventArgs,
            nextAttempt,
            exception.Message);

        await _publisherChannel!.BasicPublishAsync(
            exchange: _options.RetryExchange,
            routingKey: routingKey,
            mandatory: true,
            basicProperties: properties,
            body: eventArgs.Body,
            cancellationToken: cancellationToken);

        await _consumerChannel!.BasicAckAsync(
            eventArgs.DeliveryTag,
            multiple: false,
            cancellationToken: cancellationToken);

        TelemetryWorkerInstrumentation.MessagesRetried.Add(
            1,
            new KeyValuePair<string, object?>("routing_key", routingKey));

        _logger.LogWarning(
            "Telemetry message scheduled for retry {Attempt}/{Maximum}. Retry queue: {Queue}.",
            nextAttempt,
            _options.MaxRetryAttempts,
            retryQueue);
    }

    /// <summary>
    /// Routes permanently failed or exhausted messages to the dead-letter queue.
    /// </summary>
    private async Task SendToDeadLetterQueueAsync(
        BasicDeliverEventArgs eventArgs,
        string reason,
        CancellationToken cancellationToken)
    {
        var properties = CreateMessageProperties(
            eventArgs,
            GetRetryCount(eventArgs.BasicProperties),
            reason);

        await _publisherChannel!.BasicPublishAsync(
            exchange: _options.DeadLetterExchange,
            routingKey: "dead-letter",
            mandatory: true,
            basicProperties: properties,
            body: eventArgs.Body,
            cancellationToken: cancellationToken);

        await _consumerChannel!.BasicAckAsync(
            eventArgs.DeliveryTag,
            multiple: false,
            cancellationToken: cancellationToken);

        TelemetryWorkerInstrumentation.MessagesDeadLettered.Add(1);

        _logger.LogWarning(
            "Message {DeliveryTag} routed to the dead-letter queue. Reason: {Reason}",
            eventArgs.DeliveryTag,
            reason);
    }

    /// <summary>
    /// Creates persistent message properties while preserving event metadata.
    /// </summary>
    private static BasicProperties CreateMessageProperties(
        BasicDeliverEventArgs eventArgs,
        int retryCount,
        string failureReason)
    {
        var headers = new Dictionary<string, object?>();

        if (eventArgs.BasicProperties.Headers is not null)
        {
            foreach (var header in eventArgs.BasicProperties.Headers)
                headers[header.Key] = header.Value;
        }

        headers[RetryCountHeader] = retryCount;
        headers["x-last-failure"] = failureReason;

        return new BasicProperties
        {
            Persistent = true,
            MessageId = eventArgs.BasicProperties.MessageId,
            CorrelationId = eventArgs.BasicProperties.CorrelationId,
            ContentType = eventArgs.BasicProperties.ContentType,
            Headers = headers
        };
    }

    /// <summary>
    /// Reads a W3C trace context header. RabbitMQ delivers string header
    /// values as UTF-8 byte arrays.
    /// </summary>
    private static IEnumerable<string> ReadTraceContextHeader(
        IReadOnlyBasicProperties properties,
        string key)
    {
        if (properties.Headers is null ||
            !properties.Headers.TryGetValue(key, out var value))
        {
            return [];
        }

        return value switch
        {
            byte[] bytes => [Encoding.UTF8.GetString(bytes)],
            string text => [text],
            _ => []
        };
    }

    /// <summary>
    /// Reads the retry count from the incoming message headers.
    /// </summary>
    private static int GetRetryCount(
        IReadOnlyBasicProperties properties)
    {
        if (properties.Headers is null ||
            !properties.Headers.TryGetValue(
                RetryCountHeader,
                out var value))
        {
            return 0;
        }

        return value switch
        {
            byte number => number,
            short number => number,
            int number when number >= 0 => number,
            long number when number <= int.MaxValue &&
                             number >= 0 => (int)number,
            byte[] bytes when int.TryParse(
                Encoding.UTF8.GetString(bytes), out var parsed) &&
                              parsed >= 0 => parsed,
            _ => 0
        };
    }

    /// <summary>
    /// Persists a telemetry event while preventing duplicate event records.
    /// </summary>
    private async Task<bool> PersistTelemetryAsync(
        TelemetryEvent telemetryEvent,
        CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();

        var dbContext =
            scope.ServiceProvider.GetRequiredService<TelemetryDbContext>();

        var exists = await dbContext.TelemetryRecords
            .AnyAsync(
                record => record.EventId == telemetryEvent.EventId,
                cancellationToken);

        if (exists)
        {
            _logger.LogInformation(
                "Telemetry event {EventId} was already processed.",
                telemetryEvent.EventId);

            return false;
        }

        var record = new TelemetryRecord
        {
            EventId = telemetryEvent.EventId,
            Category = telemetryEvent.Category.ToString(),
            SchemaVersion = telemetryEvent.SchemaVersion,
            OccurredAt = telemetryEvent.OccurredAt,
            Source = telemetryEvent.Source,
            Metric = telemetryEvent.Data.Metric,
            Value = telemetryEvent.Data.Value,
            Unit = telemetryEvent.Data.Unit,
            DimensionsJson = JsonSerializer.Serialize(
                telemetryEvent.Data.Dimensions),
            ReceivedAt = DateTimeOffset.UtcNow
        };

        dbContext.TelemetryRecords.Add(record);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation
            })
        {
            _logger.LogInformation(
                "Telemetry event {EventId} was concurrently persisted.",
                telemetryEvent.EventId);

            return false;
        }
    }

    public override async Task StopAsync(
        CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
    }
}