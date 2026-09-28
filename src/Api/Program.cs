using Api.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.AddDbContext<NexusDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("NexusDatabase")));

// Swagger services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // Swagger UI
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

// Temporary
app.MapGet("/health/database", async (NexusDbContext db) =>
{
    await db.Database.OpenConnectionAsync();

    try
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT current_database()";

        var databaseName = await command.ExecuteScalarAsync();

        return Results.Ok(new
        {
            status = "healthy",
            database = databaseName
        });
    }
    finally
    {
        await db.Database.CloseConnectionAsync();
    }
})
.WithName("DatabaseHealthCheck");

app.Run();
