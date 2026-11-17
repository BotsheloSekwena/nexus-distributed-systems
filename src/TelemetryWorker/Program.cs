using TelemetryWorker.Configuration;
using Microsoft.EntityFrameworkCore;
using TelemetryWorker.Data;
using TelemetryWorker.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddOptions<RabbitMqOptions>()
    .Bind(builder.Configuration.GetSection(RabbitMqOptions.SectionName))
    .Validate(options =>
        !string.IsNullOrWhiteSpace(options.Host) &&
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

var connectionString =
    builder.Configuration.GetConnectionString("NexusDatabase")
    ?? throw new InvalidOperationException(
        "NexusDatabase connection string is missing.");

builder.Services.AddDbContext<TelemetryDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddHostedService<RabbitMqTopologyInitializer>();

builder.Services.AddHostedService<TelemetryConsumer>();

var host = builder.Build();
host.Run();