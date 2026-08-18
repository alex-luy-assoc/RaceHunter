using RaceHunter.Domain.Common;

namespace RaceHunter.Domain.Projects;

public sealed class Project
{
    private Project(Guid id, string name, DateTime createdAtUtc)
    {
        Id = id;
        Name = name;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; }
    public string Name { get; }
    public DateTime CreatedAtUtc { get; }

    public static Project Create(Guid id, string name)
    {
        if (id == Guid.Empty) throw new DomainException("A project ID is required.");
        var normalizedName = name.Trim();
        if (normalizedName.Length is < 3 or > 120) throw new DomainException("Project name must be between 3 and 120 characters.");

        var now = DateTime.UtcNow;
        var postgresPrecision = new DateTime(now.Ticks - (now.Ticks % 10), DateTimeKind.Utc);
        return new Project(id, normalizedName, postgresPrecision);
    }

    public static Project Rehydrate(Guid id, string name, DateTime createdAtUtc) =>
        new(id, name, DateTime.SpecifyKind(createdAtUtc, DateTimeKind.Utc));
}
