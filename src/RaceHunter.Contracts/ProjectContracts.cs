namespace RaceHunter.Contracts;

public sealed record CreateProjectRequest(string Name);
public sealed record ProjectResponse(Guid Id, string Name, DateTime CreatedAtUtc);
