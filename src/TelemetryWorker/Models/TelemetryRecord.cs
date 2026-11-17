namespace TelemetryWorker.Models;

/// <summary>
/// Represents a telemetry event persisted by the NEXUS platform.
/// </summary>
public sealed class TelemetryRecord
{
    public Guid EventId { get; set; }

    public string Category { get; set; } = string.Empty;

    public int SchemaVersion { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public string Source { get; set; } = string.Empty;

    public string Metric { get; set; } = string.Empty;

    public double Value { get; set; }

    public string Unit { get; set; } = string.Empty;

    public string DimensionsJson { get; set; } = "{}";

    public DateTimeOffset ReceivedAt { get; set; } =
        DateTimeOffset.UtcNow;
}