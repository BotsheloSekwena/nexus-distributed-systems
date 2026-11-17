namespace Api.Models;

/// <summary>
/// Represents telemetry submitted to the NEXUS API.
/// </summary>
public sealed record TelemetryRequest
{
    public required string Category { get; init; }

    public required string Source { get; init; }

    public required string Metric { get; init; }

    public double Value { get; init; }

    public required string Unit { get; init; }

    public Dictionary<string, string> Dimensions { get; init; } = [];
}