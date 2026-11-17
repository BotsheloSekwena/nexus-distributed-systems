using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using TelemetryWorker.Configuration;

namespace TelemetryWorker.Services;

/// <summary>
/// Creates the durable RabbitMQ exchanges, queues and routing bindings.
/// </summary>
public sealed class RabbitMqTopologyInitializer : IHostedService
{
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqTopologyInitializer> _logger;

    public RabbitMqTopologyInitializer(
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqTopologyInitializer> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.Host,
            Port = _options.Port,
            UserName = _options.User,
            Password = _options.Password
        };

        await using var connection =
            await factory.CreateConnectionAsync(cancellationToken);

        await using var channel =
            await connection.CreateChannelAsync(
                cancellationToken: cancellationToken);

        // Main telemetry exchange.
        await channel.ExchangeDeclareAsync(
            exchange: _options.Exchange,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);

        // Retry exchange.
        await channel.ExchangeDeclareAsync(
            exchange: _options.RetryExchange,
            type: ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);

        // Dead-letter exchange.
        await channel.ExchangeDeclareAsync(
            exchange: _options.DeadLetterExchange,
            type: ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);

        // Main telemetry queues.
        await channel.QueueDeclareAsync(
            queue: _options.InfrastructureQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            queue: _options.SensorQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: cancellationToken);

        // Main queue bindings.
        await channel.QueueBindAsync(
            queue: _options.InfrastructureQueue,
            exchange: _options.Exchange,
            routingKey: "infrastructure",
            cancellationToken: cancellationToken);

        await channel.QueueBindAsync(
            queue: _options.SensorQueue,
            exchange: _options.Exchange,
            routingKey: "sensor",
            cancellationToken: cancellationToken);

        // Retry queue configuration.
        var infrastructureRetryArguments =
            new Dictionary<string, object?>
            {
                ["x-message-ttl"] = _options.RetryDelayMilliseconds,
                ["x-dead-letter-exchange"] = _options.Exchange,
                ["x-dead-letter-routing-key"] = "infrastructure"
            };

        var sensorRetryArguments =
            new Dictionary<string, object?>
            {
                ["x-message-ttl"] = _options.RetryDelayMilliseconds,
                ["x-dead-letter-exchange"] = _options.Exchange,
                ["x-dead-letter-routing-key"] = "sensor"
            };

        // Infrastructure retry queue.
        await channel.QueueDeclareAsync(
            queue: _options.InfrastructureRetryQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: infrastructureRetryArguments,
            cancellationToken: cancellationToken);

        // Sensor retry queue.
        await channel.QueueDeclareAsync(
            queue: _options.SensorRetryQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: sensorRetryArguments,
            cancellationToken: cancellationToken);

        // Retry queue bindings.
        await channel.QueueBindAsync(
            queue: _options.InfrastructureRetryQueue,
            exchange: _options.RetryExchange,
            routingKey: "infrastructure",
            cancellationToken: cancellationToken);

        await channel.QueueBindAsync(
            queue: _options.SensorRetryQueue,
            exchange: _options.RetryExchange,
            routingKey: "sensor",
            cancellationToken: cancellationToken);

        // Dead-letter queue.
        await channel.QueueDeclareAsync(
            queue: _options.DeadLetterQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: cancellationToken);

        await channel.QueueBindAsync(
            queue: _options.DeadLetterQueue,
            exchange: _options.DeadLetterExchange,
            routingKey: "dead-letter",
            cancellationToken: cancellationToken);

        _logger.LogInformation(
            "RabbitMQ topology initialized. Main exchange: {Exchange}, retry exchange: {RetryExchange}, dead-letter exchange: {DeadLetterExchange}",
            _options.Exchange,
            _options.RetryExchange,
            _options.DeadLetterExchange);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}