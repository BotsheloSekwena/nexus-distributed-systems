namespace Contracts;

/// <summary>
/// Identifies the category of telemetry being published.
/// </summary>
public enum TelemetryCategory
{
    Infrastructure,
    Sensor
}

/// <summary>
/// Represents a versioned telemetry event shared between NEXUS services.
/// </summary>
public sealed record TelemetryEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();

    public TelemetryCategory Category { get; init; }

    public int SchemaVersion { get; init; } = 1;

    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Identifies the service or sensor that generated the measurement.
    /// </summary>
    public required string Source { get; init; }

    public required TelemetryMeasurement Data { get; init; }
}

/// <summary>
/// Contains a telemetry measurement and its optional contextual dimensions.
/// </summary>
public sealed record TelemetryMeasurement
{
    public required string Metric { get; init; }

    public double Value { get; init; }

    public required string Unit { get; init; }

    public Dictionary<string, string> Dimensions { get; init; } = [];
}