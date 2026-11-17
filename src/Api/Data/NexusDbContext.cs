using Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Api.Data;

/// <summary>
/// Provides database access for NEXUS application data.
/// </summary>
public class NexusDbContext : DbContext
{
    public NexusDbContext(DbContextOptions<NexusDbContext> options)
        : base(options)
    {
    }

    public DbSet<Service> Services => Set<Service>();

    public DbSet<TelemetryRecord> TelemetryRecords =>
        Set<TelemetryRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<TelemetryRecord>(entity =>
        {
            entity.HasKey(record => record.EventId);

            entity.Property(record => record.Category)
                .HasMaxLength(32)
                .IsRequired();

            entity.Property(record => record.Source)
                .HasMaxLength(150)
                .IsRequired();

            entity.Property(record => record.Metric)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(record => record.Unit)
                .HasMaxLength(32)
                .IsRequired();

            entity.Property(record => record.DimensionsJson)
                .HasColumnType("jsonb")
                .IsRequired();

            entity.HasIndex(record => record.OccurredAt);

            entity.HasIndex(record => record.Category);
        });
    }
}