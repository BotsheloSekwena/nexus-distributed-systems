using Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Api.Data;

public class NexusDbContext : DbContext
{
    public NexusDbContext(DbContextOptions<NexusDbContext> options)
        : base(options)
    {
    }

    public DbSet<Service> Services => Set<Service>();
}