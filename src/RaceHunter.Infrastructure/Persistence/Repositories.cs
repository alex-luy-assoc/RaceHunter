using Microsoft.EntityFrameworkCore;
using RaceHunter.Application.Abstractions;
using RaceHunter.Domain.Projects;

namespace RaceHunter.Infrastructure.Persistence;

internal sealed class ProjectRepository(RaceHunterDbContext context) : IProjectRepository
{
    public Task AddAsync(Project project, CancellationToken cancellationToken)
    {
        context.Projects.Add(new ProjectRecord
        {
            Id = project.Id,
            Name = project.Name,
            CreatedAtUtc = project.CreatedAtUtc
        });
        return Task.CompletedTask;
    }

    public async Task<Project?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var record = await context.Projects.AsNoTracking().SingleOrDefaultAsync(project => project.Id == id, cancellationToken);
        return record is null ? null : Project.Rehydrate(record.Id, record.Name, record.CreatedAtUtc);
    }
}
