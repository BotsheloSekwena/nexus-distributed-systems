const { NodeSDK } = require("@opentelemetry/sdk-node");
const { resourceFromAttributes } = require("@opentelemetry/resources");
const { getNodeAutoInstrumentations } = require("@opentelemetry/auto-instrumentations-node");
const { OTLPTraceExporter } = require("@opentelemetry/exporter-trace-otlp-http");
const { OTLPMetricExporter } = require("@opentelemetry/exporter-metrics-otlp-http");
const { PeriodicExportingMetricReader } = require("@opentelemetry/sdk-metrics");

const sdk = new NodeSDK({
    resource: resourceFromAttributes({
        "service.name": "nexus-auth",
        "service.namespace": "nexus",
        "deployment.environment": process.env.NODE_ENV || "development"
    }),

    traceExporter: new OTLPTraceExporter({
        url: process.env.OTEL_EXPORTER_OTLP_TRACES_ENDPOINT ||
            "http://otel-collector:4318/v1/traces"
    }),

    metricReader: new PeriodicExportingMetricReader({
        exporter: new OTLPMetricExporter({
            url: process.env.OTEL_EXPORTER_OTLP_METRICS_ENDPOINT ||
                "http://otel-collector:4318/v1/metrics"
        }),
        exportIntervalMillis: 30000
    }),

    instrumentations: [
        getNodeAutoInstrumentations()
    ]
});

sdk.start();

process.on("SIGTERM", () => {
    sdk.shutdown()
        .then(() => process.exit(0))
        .catch((error) => {
            console.error("OpenTelemetry shutdown failed:", error);
            process.exit(1);
        });
});

process.on("SIGINT", () => {
    sdk.shutdown()
        .then(() => process.exit(0))
        .catch((error) => {
            console.error("OpenTelemetry shutdown failed:", error);
            process.exit(1);
        });
});