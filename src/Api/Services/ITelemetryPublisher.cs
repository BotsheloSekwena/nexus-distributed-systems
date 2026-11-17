using Contracts;

namespace Api.Services;

/// <summary>
/// Defines the contract for publishing telemetry events to the messaging system.
/// </summary>
public interface ITelemetryPublisher
{
    /// <summary>
    /// Publishes a telemetry event asynchronously.
    /// </summary>
    Task PublishAsync(
        TelemetryEvent telemetryEvent,
        CancellationToken cancellationToken = default);
}