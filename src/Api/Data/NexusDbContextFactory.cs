using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Api.Data;

/// <summary>
/// Creates the application database context for Entity Framework design-time operations.
/// </summary>
public sealed class NexusDbContextFactory
    : IDesignTimeDbContextFactory<NexusDbContext>
{
    public NexusDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("NEXUS_DESIGNTIME_CONNECTION");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Set the NEXUS_DESIGNTIME_CONNECTION environment variable " +
                "before running Entity Framework design-time commands.");
        }

        var options = new DbContextOptionsBuilder<NexusDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new NexusDbContext(options);
    }
}