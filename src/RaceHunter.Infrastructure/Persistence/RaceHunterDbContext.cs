using Microsoft.EntityFrameworkCore;
using RaceHunter.Application.Abstractions;

namespace RaceHunter.Infrastructure.Persistence;

internal sealed class RaceHunterDbContext(DbContextOptions<RaceHunterDbContext> options)
    : DbContext(options), IUnitOfWork
{
    internal DbSet<ProjectRecord> Projects => Set<ProjectRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfiguration(new ProjectConfiguration());
}

internal sealed class ProjectRecord
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
