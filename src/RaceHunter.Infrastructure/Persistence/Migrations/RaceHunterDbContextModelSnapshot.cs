using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace RaceHunter.Infrastructure.Persistence.Migrations;

[DbContext(typeof(RaceHunterDbContext))]
internal sealed class RaceHunterDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.4");
        modelBuilder.Entity("RaceHunter.Infrastructure.Persistence.ProjectRecord", entity =>
        {
            entity.Property<Guid>("Id").HasColumnType("uuid").HasColumnName("id");
            entity.Property<DateTime>("CreatedAtUtc").HasColumnType("timestamp with time zone").HasColumnName("created_at_utc");
            entity.Property<string>("Name").IsRequired().HasMaxLength(120).HasColumnType("character varying(120)").HasColumnName("name");
            entity.HasKey("Id");
            entity.HasIndex("Name").IsUnique();
            entity.ToTable("projects");
        });
    }
}
