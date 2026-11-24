using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Api.Data;
using Api.Configuration;
using Api.Services;
using Contracts;
using Api.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("NexusDatabase");

var jwtSecret = builder.Configuration["Jwt:Secret"];
var jwtIssuer = builder.Configuration["Jwt:Issuer"];
var jwtAudience = builder.Configuration["Jwt:Audience"];

var authValidationUrl = builder.Configuration["Auth:ValidationUrl"];
var authInternalApiKey = builder.Configuration["Auth:InternalApiKey"];

if (string.IsNullOrWhiteSpace(jwtSecret) ||
    string.IsNullOrWhiteSpace(jwtIssuer) ||
    string.IsNullOrWhiteSpace(jwtAudience))
{
    throw new InvalidOperationException("JWT configuration is incomplete.");
}

if (!Uri.TryCreate(authValidationUrl, UriKind.Absolute, out var validationUri) ||
    string.IsNullOrWhiteSpace(authInternalApiKey))
{
    throw new InvalidOperationException("Auth token validation configuration is incomplete.");
}

// Configure OpenTelemetry observability.
builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(resource =>
        resource.AddService(serviceName: "nexus-api"))
    .WithTracing(tracing =>
    {
        tracing
            .AddSource(RabbitMqTelemetryPublisher.ActivitySourceName)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation();
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddMeter(
                "Microsoft.AspNetCore.Hosting",
                "Microsoft.AspNetCore.Server.Kestrel",
                "System.Net.Http")
            .AddRuntimeInstrumentation();
    })
    .UseOtlpExporter();

// Register application services.
builder.Services.AddOpenApi();

builder.Services.AddDbContext<NexusDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddHttpClient("AuthTokenValidation", client =>
{
    client.Timeout = TimeSpan.FromSeconds(3);
});

// Configure JWT authentication.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtSecret)),

            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,

            ValidateAudience = true,
            ValidAudience = jwtAudience,

            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.FromSeconds(30),

            NameClaimType = "username",
            RoleClaimType = "role"
        };

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("JwtTokenValidation");

                var userId = context.Principal?.FindFirst("sub")?.Value;
                var role = context.Principal?.FindFirst("role")?.Value;
                var versionClaim = context.Principal?.FindFirst("tokenVersion")?.Value;

                if (string.IsNullOrWhiteSpace(userId) ||
                    string.IsNullOrWhiteSpace(role) ||
                    !int.TryParse(versionClaim, out var tokenVersion) ||
                    tokenVersion < 0)
                {
                    context.Fail("Required token claims are missing or invalid.");
                    return;
                }

                try
                {
                    var httpClient = context.HttpContext.RequestServices
                        .GetRequiredService<IHttpClientFactory>()
                        .CreateClient("AuthTokenValidation");

                    using var request = new HttpRequestMessage(
                        HttpMethod.Post, validationUri);

                    request.Headers.Add(
                        "X-Internal-Api-Key", authInternalApiKey);

                    request.Content = JsonContent.Create(new
                    {
                        userId,
                        tokenVersion,
                        role
                    });

                    using var response = await httpClient.SendAsync(
                        request,
                        context.HttpContext.RequestAborted);

                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        context.Fail("The token is no longer valid.");
                        return;
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        logger.LogWarning(
                            "Auth token validation returned HTTP {StatusCode}.",
                            (int)response.StatusCode);

                        context.HttpContext.Items["AuthValidationUnavailable"] = true;
                        context.Fail("Auth token validation is unavailable.");
                        return;
                    }

                    var validationResult =
                        await response.Content.ReadFromJsonAsync<TokenValidationResponse>(
                            cancellationToken: context.HttpContext.RequestAborted);

                    if (validationResult?.Valid != true)
                    {
                        context.Fail("The token is no longer valid.");
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Auth token validation could not be completed.");

                    context.HttpContext.Items["AuthValidationUnavailable"] = true;
                    context.Fail("Auth token validation is unavailable.");
                }
            },

            OnChallenge = async context =>
            {
                if (context.HttpContext.Items.ContainsKey("AuthValidationUnavailable"))
                {
                    context.HandleResponse();
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;

                    await context.Response.WriteAsJsonAsync(new
                    {
                        status = "unavailable",
                        message = "Authentication validation is temporarily unavailable."
                    });
                }
            }
        };
    });

// Register authorization services.
builder.Services.AddAuthorization();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services
    .AddOptions<RabbitMqOptions>()
    .Bind(builder.Configuration.GetSection(RabbitMqOptions.SectionName))
    .Validate(options =>
        !string.IsNullOrWhiteSpace(options.Host) &&
        options.Port > 0 &&
        !string.IsNullOrWhiteSpace(options.User) &&
        !string.IsNullOrWhiteSpace(options.Password) &&
        !string.IsNullOrWhiteSpace(options.Exchange),
        "RabbitMQ configuration is incomplete.")
    .ValidateOnStart();

builder.Services.AddSingleton<ITelemetryPublisher, RabbitMqTelemetryPublisher>();

var app = builder.Build();

// Configure development API documentation.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Configure authentication and authorization middleware.
app.UseAuthentication();
app.UseAuthorization();

// Public API health check.
app.MapGet("/health", () =>
{
    return Results.Ok(new
    {
        status = "healthy",
        service = "nexus-api"
    });
})
.WithName("HealthCheck");

// Public database health check.
app.MapGet("/health/database", async (NexusDbContext db) =>
{
    var connection = db.Database.GetDbConnection();

    try
    {
        var canConnect = await db.Database.CanConnectAsync();

        return Results.Ok(new
        {
            status = canConnect ? "healthy" : "unhealthy",
            database = connection.Database,
            dataSource = connection.DataSource,
            canConnect
        });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Database health check failed");

        return Results.Problem(
            detail: "The database health check failed.",
            statusCode: 500);
    }
})
.WithName("DatabaseHealthCheck");

app.MapPost("/telemetry", async (
    TelemetryRequest request,
    ITelemetryPublisher publisher,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    if (!Enum.TryParse<TelemetryCategory>(
            request.Category,
            ignoreCase: true,
            out var category) ||
        !Enum.IsDefined(category))
    {
        return Results.BadRequest(new
        {
            error = "Category must be Infrastructure or Sensor."
        });
    }

    if (string.IsNullOrWhiteSpace(request.Source) ||
        request.Source.Length > 150 ||
        string.IsNullOrWhiteSpace(request.Metric) ||
        request.Metric.Length > 100 ||
        string.IsNullOrWhiteSpace(request.Unit) ||
        request.Unit.Length > 32 ||
        !double.IsFinite(request.Value) ||
        request.Dimensions.Count > 20 ||
        request.Dimensions.Any(dimension =>
            string.IsNullOrWhiteSpace(dimension.Key) ||
            dimension.Key.Length > 100 ||
            dimension.Value.Length > 250))
    {
        return Results.BadRequest(new
        {
            error = "The telemetry payload contains invalid values."
        });
    }

    var telemetryEvent = new TelemetryEvent
    {
        Category = category,
        Source = request.Source,
        Data = new TelemetryMeasurement
        {
            Metric = request.Metric,
            Value = request.Value,
            Unit = request.Unit,
            Dimensions = request.Dimensions
        }
    };

    try
    {
        await publisher.PublishAsync(
            telemetryEvent,
            cancellationToken);

        return Results.Accepted(
            $"/telemetry/{telemetryEvent.EventId}",
            new
            {
                eventId = telemetryEvent.EventId,
                status = "accepted"
            });
    }
    catch (Exception exception)
    {
        logger.LogError(
            exception,
            "Unable to publish telemetry event {EventId}",
            telemetryEvent.EventId);

        return Results.Problem(
            title: "Telemetry publishing failed.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
})
.RequireAuthorization();

// Protected endpoint for JWT authentication verification.
app.MapGet("/secure", (ClaimsPrincipal user) =>
{
    return Results.Ok(new
    {
        status = "authenticated",
        userId = user.FindFirst("sub")?.Value,
        username = user.FindFirst("username")?.Value,
        role = user.FindFirst("role")?.Value
    });
})
.RequireAuthorization()
.WithName("SecureEndpoint");

// Protected endpoint for standard users.
app.MapGet("/secure/user", () =>
{
    return Results.Ok(new
    {
        message = "User access granted"
    });
})
.RequireAuthorization(policy => policy.RequireRole("User", "Administrator"))
.WithName("UserSecureEndpoint");

// Protected endpoint for administrators.
app.MapGet("/secure/admin", () =>
{
    return Results.Ok(new
    {
        message = "Administrator access granted"
    });
})
.RequireAuthorization(policy => policy.RequireRole("Administrator"))
.WithName("AdminSecureEndpoint");

app.Run();

public sealed record TokenValidationResponse(bool Valid);