using RaceHunter.Application.Abstractions;
using RaceHunter.Domain.Projects;

namespace RaceHunter.Application.Projects;

public sealed class ProjectService(IProjectRepository projects, IUnitOfWork unitOfWork)
{
    public async Task<Project> CreateAsync(string name, CancellationToken cancellationToken)
    {
        var project = Project.Create(Guid.NewGuid(), name);
        await projects.AddAsync(project, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return project;
    }

    public Task<Project?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        projects.GetAsync(id, cancellationToken);
}
