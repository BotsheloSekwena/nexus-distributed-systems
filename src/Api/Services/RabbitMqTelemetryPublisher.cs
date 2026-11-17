using System.Text.Json;
using Api.Configuration;
using Contracts;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Api.Services;

/// <summary>
/// Publishes versioned NEXUS telemetry events to RabbitMQ.
/// </summary>
public sealed class RabbitMqTelemetryPublisher : ITelemetryPublisher, IAsyncDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqTelemetryPublisher> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private IConnection? _connection;
    private IChannel? _channel;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public RabbitMqTelemetryPublisher(
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqTelemetryPublisher> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Publishes a telemetry event using a durable RabbitMQ exchange.
    /// </summary>
    public async Task PublishAsync(
        TelemetryEvent telemetryEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(telemetryEvent);

        await _lock.WaitAsync(cancellationToken);

        try
        {
            await EnsureConnectedAsync(cancellationToken);

            var body = JsonSerializer.SerializeToUtf8Bytes(
                telemetryEvent,
                JsonOptions);

            var properties = new BasicProperties
            {
                ContentType = "application/json",
                DeliveryMode = DeliveryModes.Persistent,
                MessageId = telemetryEvent.EventId.ToString()
            };

            await _channel!.BasicPublishAsync(
                exchange: _options.Exchange,
                routingKey: telemetryEvent.Category.ToString().ToLowerInvariant(),
                mandatory: true,
                basicProperties: properties,
                body: body,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Published telemetry event {EventId} in category {Category}",
                telemetryEvent.EventId,
                telemetryEvent.Category);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to publish telemetry event {EventId}",
                telemetryEvent.EventId);

            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Establishes the broker connection and declares the telemetry exchange.
    /// </summary>
    private async Task EnsureConnectedAsync(
        CancellationToken cancellationToken)
    {
        if (_connection is { IsOpen: true } &&
            _channel is { IsOpen: true })
        {
            return;
        }

        if (_channel is not null)
        {
            await _channel.DisposeAsync();
            _channel = null;
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }

        var factory = new ConnectionFactory
        {
            HostName = _options.Host,
            Port = _options.Port,
            UserName = _options.User,
            Password = _options.Password
        };

        _connection = await factory.CreateConnectionAsync(cancellationToken);
        _channel = await _connection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true),
            cancellationToken);

        await _channel.ExchangeDeclareAsync(
            exchange: _options.Exchange,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);

        _logger.LogInformation(
            "Connected to RabbitMQ and declared exchange {Exchange}",
            _options.Exchange);
    }

    public async ValueTask DisposeAsync()
    {
        await _lock.WaitAsync();

        try
        {
            if (_channel is not null)
                await _channel.DisposeAsync();

            if (_connection is not null)
                await _connection.DisposeAsync();
        }
        finally
        {
            _lock.Release();
            _lock.Dispose();
        }
    }
}