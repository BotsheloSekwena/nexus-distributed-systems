namespace Api.Configuration;

/// <summary>
/// Defines the connection settings for the NEXUS RabbitMQ broker.
/// </summary>
public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMQ";

    public string Host { get; set; } = "rabbitmq";

    public int Port { get; set; } = 5672;

    public string User { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string Exchange { get; set; } = "nexus.telemetry";
}