using TelemetryWorker.Configuration;
using Microsoft.EntityFrameworkCore;
using TelemetryWorker.Data;
using TelemetryWorker.Services;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using TelemetryWorker.Observability;

var builder = Host.CreateApplicationBuilder(args);

// Configure OpenTelemetry observability.
builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(resource =>
        resource.AddService(serviceName: "nexus-telemetry-worker"))
    .WithTracing(tracing =>
    {
        tracing
            .AddSource(TelemetryWorkerInstrumentation.ActivitySourceName)
            .AddHttpClientInstrumentation();
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddMeter(TelemetryWorkerInstrumentation.MeterName)
            .AddRuntimeInstrumentation();
    })
    .UseOtlpExporter();

// Configure RabbitMQ options.
builder.Services
    .AddOptions<RabbitMqOptions>()
    .Bind(builder.Configuration.GetSection(RabbitMqOptions.SectionName))
    .Validate(options =>
        options.Port > 0 &&
        !string.IsNullOrWhiteSpace(options.User) &&
        !string.IsNullOrWhiteSpace(options.Password) &&
        !string.IsNullOrWhiteSpace(options.Exchange) &&
        !string.IsNullOrWhiteSpace(options.InfrastructureQueue) &&
        !string.IsNullOrWhiteSpace(options.SensorQueue) &&
        !string.IsNullOrWhiteSpace(options.RetryExchange) &&
        !string.IsNullOrWhiteSpace(options.DeadLetterExchange) &&
        !string.IsNullOrWhiteSpace(options.InfrastructureRetryQueue) &&
        !string.IsNullOrWhiteSpace(options.SensorRetryQueue) &&
        !string.IsNullOrWhiteSpace(options.DeadLetterQueue) &&
        options.MaxRetryAttempts > 0 &&
        options.RetryDelayMilliseconds > 0,
        "RabbitMQ configuration is incomplete.")
    .ValidateOnStart();

// Configure PostgreSQL.
var connectionString =
    builder.Configuration.GetConnectionString("NexusDatabase")
    ?? throw new InvalidOperationException(
        "NexusDatabase connection string is missing.");

builder.Services.AddDbContext<TelemetryDbContext>(options =>
    options.UseNpgsql(connectionString));

// Register background services.
builder.Services.AddHostedService<RabbitMqTopologyInitializer>();
builder.Services.AddHostedService<TelemetryConsumer>();

var host = builder.Build();

host.Run();