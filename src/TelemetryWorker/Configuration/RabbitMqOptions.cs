namespace TelemetryWorker.Configuration;

/// <summary>
/// Defines the RabbitMQ connection and queue settings.
/// </summary>
public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMQ";

    public string Host { get; set; } = "rabbitmq";

    public int Port { get; set; } = 5672;

    public string User { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string Exchange { get; set; } = "nexus.telemetry";

    public int MaxRetryAttempts { get; set; } = 3;

    public int RetryDelayMilliseconds { get; set; } = 10000;

    public string InfrastructureQueue { get; set; } =
        "nexus.telemetry.infrastructure";

    public string SensorQueue { get; set; } =
        "nexus.telemetry.sensor";

    public string RetryExchange { get; set; } =
        "nexus.telemetry.retry";

    public string DeadLetterExchange { get; set; } =
        "nexus.telemetry.dlx";

    public string InfrastructureRetryQueue { get; set; } =
        "nexus.telemetry.infrastructure.retry";

    public string SensorRetryQueue { get; set; } =
        "nexus.telemetry.sensor.retry";

    public string DeadLetterQueue { get; set; } =
        "nexus.telemetry.dead-letter";
}