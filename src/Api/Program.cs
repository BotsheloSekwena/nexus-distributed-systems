using Api.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("NexusDatabase");

builder.Services.AddOpenApi();

builder.Services.AddDbContext<NexusDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/health", () =>
{
    return Results.Ok(new
    {
        status = "healthy",
        service = "nexus-api"
    });
})
.WithName("HealthCheck");

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
        app.Logger.LogError(
            ex,
            "Database health check failed"
        );

        return Results.Problem(
            detail: "The database health check failed.",
            statusCode: 500
        );
    }
})
.WithName("DatabaseHealthCheck");

app.Run();
