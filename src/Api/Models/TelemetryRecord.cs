namespace Api.Models;

/// <summary>
/// Represents a telemetry event persisted by the NEXUS platform.
/// </summary>
public sealed class TelemetryRecord
{
    /// <summary>
    /// Unique event identifier, also used for duplicate detection.
    /// </summary>
    public Guid EventId { get; set; }

    public string Category { get; set; } = string.Empty;

    public int SchemaVersion { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public string Source { get; set; } = string.Empty;

    public string Metric { get; set; } = string.Empty;

    public double Value { get; set; }

    public string Unit { get; set; } = string.Empty;

    /// <summary>
    /// Stores additional measurement dimensions as JSON.
    /// </summary>
    public string DimensionsJson { get; set; } = "{}";

    /// <summary>
    /// Records when the event was persisted by NEXUS.
    /// </summary>
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
}