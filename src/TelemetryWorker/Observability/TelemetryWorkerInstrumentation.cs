using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace TelemetryWorker.Observability;

/// <summary>
/// Defines custom OpenTelemetry activities and metrics for telemetry processing.
/// </summary>
public static class TelemetryWorkerInstrumentation
{
    public const string ActivitySourceName = "Nexus.TelemetryWorker";
    public const string MeterName = "Nexus.TelemetryWorker";

    public static readonly ActivitySource ActivitySource =
        new(ActivitySourceName);

    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> MessagesProcessed =
        Meter.CreateCounter<long>("nexus.telemetry.messages.processed");

    public static readonly Counter<long> MessagesFailed =
        Meter.CreateCounter<long>("nexus.telemetry.messages.failed");

    public static readonly Counter<long> MessagesRetried =
        Meter.CreateCounter<long>("nexus.telemetry.messages.retried");

    public static readonly Counter<long> MessagesDeadLettered =
        Meter.CreateCounter<long>("nexus.telemetry.messages.dead_lettered");

    public static readonly Counter<long> DuplicateMessages =
        Meter.CreateCounter<long>("nexus.telemetry.messages.duplicates");

    public static readonly Histogram<double> ProcessingDuration =
        Meter.CreateHistogram<double>(
            "nexus.telemetry.processing.duration",
            unit: "ms");
}